using System.Data;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Dapper;
using Elsa;
using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Design;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace MozartWorkflows.Elsa.Activities
{

[Action(Category = "GroupInsurance.Endorsement", Description = "Row-wise employee offboard BULK DELETE (D).")]
public class ProcessGIEndorsementRowwiseOffboardDelete : Activity
{
    private const string ColEmployeeId = "EmployeeId";
    private const string ColDateOfLeaving = "DateOfLeaving";
    private const string SpGetUploadSummary = "dbo.sp_GI_EndRw_GetUploadSummary";
    private const string SpRecalcSummary = "dbo.sp_GI_EndRw_RecalculateUploadSummary";
    private const string ResponseVar = "giEndorsementDeleteResponse";
    private static readonly Regex Base64DataUriRegex = new(@"^data:(?<mime>[^;]+);base64,(?<data>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private readonly IConfiguration _config;
    public ProcessGIEndorsementRowwiseOffboardDelete(IConfiguration config) => _config = config;

    public class FileData
    {
        public string FileName { get; set; } = default!;
        public string Base64Data { get; set; } = default!;
        public string? FileType { get; set; }
    }

    public class UploadRowErrorDto
    {
        public int RowNumber { get; set; }
        public string? EmployeeId { get; set; }
        public string Message { get; set; } = default!;
    }

    public class ErrorFileData
    {
        public string FileName { get; set; } = default!;
        public string Base64Data { get; set; } = default!;
        public string FileType { get; set; } =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    }

#pragma warning disable S3459, S1144 // Dapper maps these properties via reflection
    private sealed class GiUploadHistoryDto
    {
        public long UploadId { get; set; }
        public string FileName { get; set; } = default!;
        public string Status { get; set; } = default!;
        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public int InvalidRows { get; set; }
        public int EmployeeCount { get; set; }
    }
#pragma warning restore S3459, S1144

    private sealed class ParsedDeleteRow
    {
        public int RowNumber { get; set; }
        public Dictionary<string, string?> Payload { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<string> Errors { get; set; } = new();
    }

#pragma warning disable S3459, S1144 // Dapper maps these properties via reflection
    private sealed class ExistingEmployeeDeleteInfo
    {
        public string EmployeeId { get; set; } = default!;
        public bool HasActiveEmployeeMember { get; set; }
    }
#pragma warning restore S3459, S1144

    [ActivityInput(
        Hint = "CaseId to link this upload with RFQ case.",
        SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid },
        DefaultSyntax = SyntaxNames.JavaScript)]
    public string? CaseId { get; set; }

    [ActivityInput(
        UIHint = ActivityInputUIHints.Json,
        Hint = "Uploaded file object (Base64 + FileName).",
        SupportedSyntaxes = new[] { SyntaxNames.Json, SyntaxNames.JavaScript },
        DefaultSyntax = SyntaxNames.JavaScript)]
    public FileData File { get; set; } = default!;

    [ActivityInput(
        Hint = "UploadedBy (optional).",
        SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid },
        DefaultSyntax = SyntaxNames.JavaScript)]
    public string? UploadedBy { get; set; }

    [ActivityInput(
        Hint = "Uploaded file URL/path returned from UploadFileActivity",
        SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid },
        DefaultSyntax = SyntaxNames.JavaScript)]
    public string? FileUrl { get; set; }

    [ActivityOutput]
    public string? OutputJson { get; set; }

    private static readonly Dictionary<string, string> HeaderMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Employee ID"] = ColEmployeeId,
            ["Employee Id"] = ColEmployeeId,
            ["Date of Leaving (DD/MM/YYYY)"] = ColDateOfLeaving,
            ["Reason of Leaving"] = "ReasonOfLeaving",
            ["S. No."] = "SNo",
            ["S.No."] = "SNo"
        };

#pragma warning disable S3776
    protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
    {
        var connStr = _config.GetConnectionString("connectionString");
        var bytes = DecodeBase64(File.Base64Data);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet(1);

        const int headerRowNum = 2;
        const int dataStartRow = 3;

        var headerCells = ws.Row(headerRowNum).CellsUsed().ToList();
        if (headerCells.Count == 0)
            throw new InvalidOperationException("No headers found in Excel (Row 2).");

        var colMap = new Dictionary<int, string>();
        for (int i = 0; i < headerCells.Count; i++)
        {
            var raw = headerCells[i].GetString().Trim();
            if (HeaderMap.TryGetValue(raw, out var jsonKey) && jsonKey != "SNo")
                colMap[i + 1] = jsonKey;
        }

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ColEmployeeId, ColDateOfLeaving
        };

        foreach (var req in required)
        {
            if (!colMap.Values.Contains(req, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Missing required column in template for key: {req}");
        }

        long uploadId;
        await using (var conn = new SqlConnection(connStr))
        {
            var p = new DynamicParameters();
            p.Add("@FileName", File.FileName);
            p.Add("@UploadedBy", UploadedBy);
            p.Add("@FileUrl", FileUrl);
            p.Add("@CaseId", CaseId);
            p.Add("@UploadId", dbType: DbType.Int64, direction: ParameterDirection.Output);

            await conn.ExecuteAsync(
                "dbo.sp_GI_EndRw_CreateUpload",
                p,
                commandType: CommandType.StoredProcedure);

            uploadId = p.Get<long>("@UploadId");
        }

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRowNum;
        var parsedRows = new List<ParsedDeleteRow>();

        for (int excelRow = dataStartRow; excelRow <= lastRow; excelRow++)
        {
            var row = ws.Row(excelRow);
            bool anyValue = false;

            var payload = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in colMap)
            {
                var val = ReadCell(row.Cell(kv.Key));
                if (!string.IsNullOrWhiteSpace(val))
                    anyValue = true;

                payload[kv.Value] = string.IsNullOrWhiteSpace(val) ? null : val.Trim();
            }

            if (!anyValue)
                continue;

            var errs = new List<string>();

            foreach (var req in required)
            {
                if (!payload.TryGetValue(req, out var v) || string.IsNullOrWhiteSpace(v))
                    errs.Add($"{req} is required");
            }

            if (payload.TryGetValue(ColDateOfLeaving, out var dol) &&
                !string.IsNullOrWhiteSpace(dol) &&
                !DateTime.TryParse(dol, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            {
                errs.Add($"{ColDateOfLeaving} is invalid");
            }

            parsedRows.Add(new ParsedDeleteRow
            {
                RowNumber = excelRow,
                Payload = payload,
                Errors = errs
            });
        }

        var employeeIds = parsedRows
            .Select(x => x.Payload.GetValueOrDefault(ColEmployeeId))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dictionary<string, ExistingEmployeeDeleteInfo> existingEmployees;
        await using (var conn = new SqlConnection(connStr))
        {
            var idsToCheck = employeeIds.Count == 0
                ? new List<string> { "__NO_MATCH__" }
                : employeeIds;

            var rows = await conn.QueryAsync<ExistingEmployeeDeleteInfo>(
                @"
SELECT
    m.EmployeeId,
    m.EnrollmentId,
    CAST(CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.InsuranceEnrollmentMembers mem WITH (NOLOCK)
            WHERE mem.EnrollmentId = m.EnrollmentId
              AND mem.IsActive = 1
              AND mem.Relation = 'Employee'
        ) THEN 1 ELSE 0 END AS bit) AS HasActiveEmployeeMember
FROM dbo.InsuranceEnrollmentMaster m WITH (NOLOCK)
WHERE m.ApplicationId = 8
  AND m.IsActive = 1
  AND m.EmployeeId IN @EmployeeIds",
                new { EmployeeIds = idsToCheck });

            existingEmployees = rows
                .GroupBy(x => x.EmployeeId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToDictionary(x => x.EmployeeId, x => x, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var row in parsedRows)
        {
            var empId = row.Payload.GetValueOrDefault(ColEmployeeId)?.Trim();
            if (string.IsNullOrWhiteSpace(empId))
                continue;

            if (!existingEmployees.TryGetValue(empId, out var existing))
            {
                row.Errors.Add($"EmployeeId '{empId}' does not exist.");
                continue;
            }

            if (!existing.HasActiveEmployeeMember)
                row.Errors.Add($"EmployeeId '{empId}' exists but has no active Employee/Self member in enrollment.");
        }

        var tvp = BuildRowTvp();
        foreach (var row in parsedRows)
        {
            row.Payload.TryGetValue(ColEmployeeId, out var empId);
            var status = row.Errors.Count == 0 ? "VALID" : "INVALID";
            var errorMessage = row.Errors.Count == 0 ? null : string.Join(" | ", row.Errors);
            var rowJson = JsonConvert.SerializeObject(row.Payload);

            tvp.Rows.Add(
                row.RowNumber,
                (object?)empId ?? DBNull.Value,
                rowJson,
                status,
                (object?)errorMessage ?? DBNull.Value
            );
        }

        await using (var conn = new SqlConnection(connStr))
        {
            await conn.ExecuteAsync(
                "dbo.sp_GI_EndRw_InsertUploadRows",
                new { UploadId = uploadId, Rows = tvp.AsTableValuedParameter("dbo.GI_UploadRowType") },
                commandType: CommandType.StoredProcedure
            );

            await conn.ExecuteAsync(
                SpRecalcSummary,
                new { UploadId = uploadId },
                commandType: CommandType.StoredProcedure
            );
        }

        var invalidRows = parsedRows.Where(x => x.Errors.Count > 0).ToList();
        if (invalidRows.Count > 0)
        {
            var errorFile = BuildDeleteErrorWorkbook(File.FileName, invalidRows);

            await using var conn = new SqlConnection(connStr);

            var hist = await conn.QuerySingleAsync<GiUploadHistoryDto>(
                SpGetUploadSummary,
                new { UploadId = uploadId },
                commandType: CommandType.StoredProcedure
            );

            var errs = (await conn.QueryAsync<UploadRowErrorDto>(
                "dbo.sp_GI_EndRw_GetUploadErrors",
                new { UploadId = uploadId, Take = 5000 },
                commandType: CommandType.StoredProcedure
            )).AsList();

            OutputJson = JsonConvert.SerializeObject(new
            {
                success = false,
                message = "Validation failed. No delete operation was performed.",
                data = new
                {
                    hist.UploadId,
                    hist.FileName,
                    hist.Status,
                    hist.TotalRows,
                    hist.ValidRows,
                    hist.InvalidRows,
                    EmployeeCount = hist.EmployeeCount,
                    Errors = errs,
                    ErrorFile = errorFile
                }
            });

            context.SetVariable(ResponseVar, OutputJson);
            return Done();
        }

        await using (var conn = new SqlConnection(connStr))
        {
            await conn.ExecuteAsync(
                "dbo.sp_GI_EndRw_BulkDelete_FromUpload",
                new { UploadId = uploadId, ApplicationId = 8 },
                commandType: CommandType.StoredProcedure);

            await conn.ExecuteAsync(
                SpRecalcSummary,
                new { UploadId = uploadId },
                commandType: CommandType.StoredProcedure);

            var hist = await conn.QuerySingleAsync<GiUploadHistoryDto>(
                SpGetUploadSummary,
                new { UploadId = uploadId },
                commandType: CommandType.StoredProcedure
            );

            var errs = (await conn.QueryAsync<UploadRowErrorDto>(
                "dbo.sp_GI_EndRw_GetUploadErrors",
                new { UploadId = uploadId, Take = 5000 },
                commandType: CommandType.StoredProcedure
            )).AsList();

            OutputJson = JsonConvert.SerializeObject(new
            {
                success = true,
                message = "Endorsement delete upload processed successfully.",
                data = new
                {
                    hist.UploadId,
                    hist.FileName,
                    hist.Status,
                    hist.TotalRows,
                    hist.ValidRows,
                    hist.InvalidRows,
                    EmployeeCount = hist.EmployeeCount,
                    Errors = errs
                }
            });

            context.SetVariable(ResponseVar, OutputJson);
        }

        return Done();
    }
#pragma warning restore S3776

    private static byte[] DecodeBase64(string input)
    {
        var match = Base64DataUriRegex.Match(input);

        var base64 = match.Success ? match.Groups["data"].Value : input;
        return Convert.FromBase64String(base64);
    }

    private static DataTable BuildRowTvp()
    {
        var dt = new DataTable();
        dt.Columns.Add("RowNumber", typeof(int));
        dt.Columns.Add(ColEmployeeId, typeof(string));
        dt.Columns.Add("RowData", typeof(string));
        dt.Columns.Add("Status", typeof(string));
        dt.Columns.Add("ErrorMessage", typeof(string));
        return dt;
    }

    private static string? ReadCell(IXLCell cell)
    {
        if (cell.IsEmpty())
            return null;

        if (cell.TryGetValue<DateTime>(out var dt))
            return dt.ToString("yyyy-MM-dd");

        return cell.GetFormattedString()?.Trim();
    }

    private static ErrorFileData BuildDeleteErrorWorkbook(string originalFileName, List<ParsedDeleteRow> invalidRows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ValidationErrors");

        var headers = new[]
        {
            "RowNumber",
            ColEmployeeId,
            ColDateOfLeaving,
            "ReasonOfLeaving",
            "ErrorMessage"
        };

        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        int r = 2;
        foreach (var item in invalidRows.OrderBy(x => x.RowNumber))
        {
            ws.Cell(r, 1).Value = item.RowNumber;
            ws.Cell(r, 2).Value = item.Payload.GetValueOrDefault(ColEmployeeId);
            ws.Cell(r, 3).Value = item.Payload.GetValueOrDefault(ColDateOfLeaving);
            ws.Cell(r, 4).Value = item.Payload.GetValueOrDefault("ReasonOfLeaving");
            ws.Cell(r, 5).Value = string.Join(" | ", item.Errors);
            r++;
        }

        ws.Columns().AdjustToContents();
        ws.Row(1).Style.Font.Bold = true;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var cleanName = Path.GetFileNameWithoutExtension(originalFileName);
        return new ErrorFileData
        {
            FileName = $"{cleanName}_validation_errors.xlsx",
            Base64Data = Convert.ToBase64String(ms.ToArray())
        };
    }
}

} // namespace MozartWorkflows.Elsa.Activities
