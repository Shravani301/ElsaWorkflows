using System.Data;
using System.Globalization;
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

public abstract partial class BaseGIEndorsementRowwiseUpload : Activity
{
    private const string ColEmployeeId = "EmployeeId";
    private const string ColMemberName = "MemberName";
    private const string ColRelationship = "Relationship";
    private const string ColGender = "Gender";
    private const string ColDateOfJoining = "DateOfJoining";
    private const string ColDateOfBirth = "DateOfBirth";
    private const string ColSumInsured = "SumInsured";
    private const string ColMobile = "Mobile";
    private const string ColEmail = "Email";
    private const string ColDesignationOrGrade = "DesignationOrGrade";
    private const string RelSelf = "Self";
    private const string RelEmployee = "Employee";
    private const string SpGetUploadSummary = "dbo.sp_GI_EndRw_GetUploadSummary";
    private const string SpGetUploadErrors = "dbo.sp_GI_EndRw_GetUploadErrors";
    private const string SpRecalcSummary = "dbo.sp_GI_EndRw_RecalculateUploadSummary";

    private readonly IConfiguration _config;
    protected BaseGIEndorsementRowwiseUpload(IConfiguration config) => _config = config;

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
        public int SpouseCount { get; set; }
        public int ParentCount { get; set; }
        public int ChildCount { get; set; }
        public int MaleCount { get; set; }
        public int FemaleCount { get; set; }
        public int OtherCount { get; set; }
        public int Age3To18Count { get; set; }
        public int Age19To35Count { get; set; }
        public int Age36To45Count { get; set; }
        public int Age46To60Count { get; set; }
        public int Age61To80Count { get; set; }
    }
#pragma warning restore S3459, S1144

    private sealed class ParsedRow
    {
        public int RowNumber { get; set; }
        public Dictionary<string, string?> Payload { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<string> Errors { get; set; } = new();
    }

#pragma warning disable S3459, S1144 // Dapper maps these properties via reflection
    private sealed class ExistingEmployeeInfo
    {
        public string EmployeeId { get; set; } = default!;
        public bool HasActiveEmployeeMember { get; set; }
    }
#pragma warning restore S3459, S1144

    private static readonly Dictionary<string, string> HeaderMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Employee Id"] = ColEmployeeId,
            ["Employee ID"] = ColEmployeeId,
            ["Member Name"] = ColMemberName,
            ["Relationship"] = ColRelationship,
            ["Gender (Male/Female)"] = ColGender,
            ["Date of Joining (DD/MM/YYYY)"] = ColDateOfJoining,
            ["Date of Birth (DD/MM/YYYY)"] = ColDateOfBirth,
            ["Sum Insured"] = ColSumInsured,
            ["Mobile"] = ColMobile,
            ["Mobile "] = ColMobile,
            ["Email"] = ColEmail,
            ["Designation or grade"] = ColDesignationOrGrade,
            ["Designation or Grade"] = ColDesignationOrGrade,
            ["Designation/Grade"] = ColDesignationOrGrade
        };

    private static readonly HashSet<string> RequiredColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ColEmployeeId, ColMemberName, ColRelationship, ColGender,
            ColDateOfJoining, ColDateOfBirth, ColSumInsured
        };

    private static readonly HashSet<string> DateKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ColDateOfJoining, ColDateOfBirth, "DateOfLeaving"
        };

    protected abstract string NormalizeProcName { get; }
    protected abstract string ResponseVariableName { get; }

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

            if (string.Equals(raw, "S.No.", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "S. No.", StringComparison.OrdinalIgnoreCase))
                continue;

            if (HeaderMap.TryGetValue(raw, out var jsonKey))
                colMap[i + 1] = jsonKey;
        }

        foreach (var req in RequiredColumns)
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
        var parsedRows = new List<ParsedRow>();

        for (int excelRow = dataStartRow; excelRow <= lastRow; excelRow++)
        {
            var row = ws.Row(excelRow);
            bool anyValue = false;

            var payload = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in colMap)
            {
                var cell = row.Cell(kv.Key);
                var val = ReadCell(cell, kv.Value);
                if (!string.IsNullOrWhiteSpace(val))
                    anyValue = true;

                payload[kv.Value] = NormalizeNull(val);
            }

            if (!anyValue)
                continue;

            var errors = ValidateRow(payload, RequiredColumns);

            parsedRows.Add(new ParsedRow
            {
                RowNumber = excelRow,
                Payload = payload,
                Errors = errors
            });
        }

        var employeeIdsInUpload = parsedRows
            .Select(x => x.Payload.GetValueOrDefault(ColEmployeeId))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .ToList();

        var employeeRelationshipMap = parsedRows
            .Where(x => !string.IsNullOrWhiteSpace(x.Payload.GetValueOrDefault(ColEmployeeId)))
            .GroupBy(x => x.Payload.GetValueOrDefault(ColEmployeeId)!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.Payload.GetValueOrDefault(ColRelationship)?.Trim())
                      .Where(r => !string.IsNullOrWhiteSpace(r))
                      .ToList(),
                StringComparer.OrdinalIgnoreCase);

        Dictionary<string, ExistingEmployeeInfo> existingEmployees;
        await using (var conn = new SqlConnection(connStr))
        {
            var idsToCheck = employeeIdsInUpload.Count == 0
                ? new List<string> { "__NO_MATCH__" }
                : employeeIdsInUpload.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var rows = await conn.QueryAsync<ExistingEmployeeInfo>(
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
            var relation = row.Payload.GetValueOrDefault(ColRelationship)?.Trim();
            if (string.IsNullOrWhiteSpace(empId))
                continue;

            var isSelfRow =
                string.Equals(relation, RelSelf, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(relation, RelEmployee, StringComparison.OrdinalIgnoreCase);

            if (!existingEmployees.TryGetValue(empId, out var existing))
            {
                if (employeeRelationshipMap.TryGetValue(empId, out var relations))
                {
                    var selfCount = relations.Count(r =>
                        string.Equals(r, RelSelf, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r, RelEmployee, StringComparison.OrdinalIgnoreCase));

                    if (selfCount == 0)
                        row.Errors.Add($"EmployeeId '{empId}' is new, so one Self/Employee row is required.");

                    if (selfCount > 1)
                        row.Errors.Add($"Multiple Self/Employee rows found for EmployeeId '{empId}'.");
                }
            }
            else
            {
                if (!existing.HasActiveEmployeeMember)
                    row.Errors.Add($"EmployeeId '{empId}' exists but has no active Employee/Self member in enrollment.");

                if (isSelfRow)
                    row.Errors.Add($"Self/Employee row is not allowed for existing EmployeeId '{empId}' in endorsement add. Upload only dependent rows.");
            }
        }

        var rowTvp = BuildRowTvp();
        foreach (var row in parsedRows)
        {
            row.Payload.TryGetValue(ColEmployeeId, out var empId);
            var status = row.Errors.Count == 0 ? "VALID" : "INVALID";
            var errorMessage = row.Errors.Count == 0 ? null : string.Join(" | ", row.Errors);
            var rowJson = JsonConvert.SerializeObject(row.Payload);

            rowTvp.Rows.Add(
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
                new { UploadId = uploadId, Rows = rowTvp.AsTableValuedParameter("dbo.GI_UploadRowType") },
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
            var errorFile = BuildErrorWorkbook(File.FileName, invalidRows);

            await using var conn = new SqlConnection(connStr);

            var hist = await conn.QuerySingleAsync<GiUploadHistoryDto>(
                SpGetUploadSummary,
                new { UploadId = uploadId },
                commandType: CommandType.StoredProcedure
            );

            var errs = (await conn.QueryAsync<UploadRowErrorDto>(
                SpGetUploadErrors,
                new { UploadId = uploadId, Take = 5000 },
                commandType: CommandType.StoredProcedure
            )).AsList();

            OutputJson = JsonConvert.SerializeObject(new
            {
                success = false,
                message = "Validation failed. No records were inserted into normalized tables.",
                data = new
                {
                    hist.UploadId,
                    hist.FileName,
                    hist.Status,
                    hist.TotalRows,
                    hist.ValidRows,
                    hist.InvalidRows,
                    MemberCounts = new
                    {
                        hist.EmployeeCount,
                        hist.SpouseCount,
                        hist.ParentCount,
                        hist.ChildCount
                    },
                    GenderCounts = new
                    {
                        hist.MaleCount,
                        hist.FemaleCount,
                        hist.OtherCount
                    },
                    AgeBands = new
                    {
                        hist.Age3To18Count,
                        hist.Age19To35Count,
                        hist.Age36To45Count,
                        hist.Age46To60Count,
                        hist.Age61To80Count
                    },
                    Errors = errs,
                    ErrorFile = errorFile
                }
            });

            context.SetVariable(ResponseVariableName, OutputJson);
            return Done();
        }

        await using (var conn = new SqlConnection(connStr))
        {
            await conn.ExecuteAsync(
                NormalizeProcName,
                new { UploadId = uploadId, ApplicationId = 8, UserId = (string?)null },
                commandType: CommandType.StoredProcedure
            );

            await conn.ExecuteAsync(
                SpRecalcSummary,
                new { UploadId = uploadId },
                commandType: CommandType.StoredProcedure
            );

            var hist = await conn.QuerySingleAsync<GiUploadHistoryDto>(
                SpGetUploadSummary,
                new { UploadId = uploadId },
                commandType: CommandType.StoredProcedure
            );

            var errs = (await conn.QueryAsync<UploadRowErrorDto>(
                SpGetUploadErrors,
                new { UploadId = uploadId, Take = 5000 },
                commandType: CommandType.StoredProcedure
            )).AsList();

            OutputJson = JsonConvert.SerializeObject(new
            {
                success = true,
                message = "Endorsement row-wise upload processed successfully.",
                data = new
                {
                    hist.UploadId,
                    hist.FileName,
                    hist.Status,
                    hist.TotalRows,
                    hist.ValidRows,
                    hist.InvalidRows,
                    MemberCounts = new
                    {
                        hist.EmployeeCount,
                        hist.SpouseCount,
                        hist.ParentCount,
                        hist.ChildCount
                    },
                    GenderCounts = new
                    {
                        hist.MaleCount,
                        hist.FemaleCount,
                        hist.OtherCount
                    },
                    AgeBands = new
                    {
                        hist.Age3To18Count,
                        hist.Age19To35Count,
                        hist.Age36To45Count,
                        hist.Age46To60Count,
                        hist.Age61To80Count
                    },
                    Errors = errs
                }
            });

            context.SetVariable(ResponseVariableName, OutputJson);
        }

        return Done();
    }
#pragma warning restore S3776

    private static string? NormalizeNull(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static byte[] DecodeBase64(string input)
    {
        var match = DataUrlRegex().Match(input);

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

    private static string? ReadCell(IXLCell cell, string jsonKey)
    {
        if (cell.IsEmpty())
            return null;

        if (DateKeys.Contains(jsonKey))
        {
            if (cell.TryGetValue<DateTime>(out var dt))
                return dt.ToString("yyyy-MM-dd");

            var s = cell.GetFormattedString().Trim();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        if (jsonKey.Equals(ColSumInsured, StringComparison.OrdinalIgnoreCase))
        {
            if (cell.TryGetValue<decimal>(out var num))
            {
                return num % 1 == 0
                    ? decimal.Truncate(num).ToString(CultureInfo.InvariantCulture)
                    : num.ToString(CultureInfo.InvariantCulture);
            }

            var s = cell.GetFormattedString().Trim();
            if (string.IsNullOrWhiteSpace(s))
                return null;

            return s.Replace(",", "");
        }

        var str = cell.GetFormattedString().Trim();
        return string.IsNullOrWhiteSpace(str) ? null : str;
    }

#pragma warning disable S3776
    private static List<string> ValidateRow(
        Dictionary<string, string?> d,
        HashSet<string> required)
    {
        var errs = new List<string>();

        foreach (var req in required)
        {
            if (!d.TryGetValue(req, out var v) || string.IsNullOrWhiteSpace(v))
                errs.Add($"{req} is required");
        }

        var rel = d.GetValueOrDefault(ColRelationship);
        if (!string.IsNullOrWhiteSpace(rel))
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                RelSelf, RelEmployee, "Spouse", "Child",
                "Parent", "Parent in law", "ParentInLaw", "ParentIn-Law"
            };

            if (!allowed.Contains(rel.Trim()))
                errs.Add("Relationship must be one of Self/Employee/Spouse/Child/Parent/Parent in law");
        }

        var gender = d.GetValueOrDefault(ColGender);
        if (!string.IsNullOrWhiteSpace(gender))
        {
            var allowedG = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Male", "Female", "M", "F", "Other"
            };

            if (!allowedG.Contains(gender.Trim()))
                errs.Add("Gender must be Male/Female/Other");
        }

        var mobile = d.GetValueOrDefault(ColMobile);
        if (!string.IsNullOrWhiteSpace(mobile) && !MobileRegex().IsMatch(mobile))
            errs.Add("Mobile must be 10 digits");

        var email = d.GetValueOrDefault(ColEmail);
        if (!string.IsNullOrWhiteSpace(email) && !EmailRegex().IsMatch(email))
            errs.Add("Email is invalid");

        foreach (var dateKey in new[] { ColDateOfJoining, ColDateOfBirth })
        {
            var v = d.GetValueOrDefault(dateKey);
            if (!string.IsNullOrWhiteSpace(v) && !DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                errs.Add($"{dateKey} is invalid");
        }

        var si = d.GetValueOrDefault(ColSumInsured);
        if (!string.IsNullOrWhiteSpace(si))
        {
            var norm = si.Replace(",", "");
            if (!decimal.TryParse(norm, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                errs.Add("SumInsured must be numeric");
            else
                d[ColSumInsured] = norm;
        }

        return errs;
    }
#pragma warning restore S3776

    private static ErrorFileData BuildErrorWorkbook(string originalFileName, List<ParsedRow> invalidRows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ValidationErrors");

        var headers = new[]
        {
            "RowNumber",
            ColEmployeeId,
            ColMemberName,
            ColRelationship,
            ColGender,
            ColDateOfJoining,
            ColDateOfBirth,
            ColDesignationOrGrade,
            ColSumInsured,
            ColMobile,
            ColEmail,
            "ErrorMessage"
        };

        for (int i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        int r = 2;
        foreach (var item in invalidRows.OrderBy(x => x.RowNumber))
        {
            ws.Cell(r, 1).Value = item.RowNumber;
            ws.Cell(r, 2).Value = item.Payload.GetValueOrDefault(ColEmployeeId);
            ws.Cell(r, 3).Value = item.Payload.GetValueOrDefault(ColMemberName);
            ws.Cell(r, 4).Value = item.Payload.GetValueOrDefault(ColRelationship);
            ws.Cell(r, 5).Value = item.Payload.GetValueOrDefault(ColGender);
            ws.Cell(r, 6).Value = item.Payload.GetValueOrDefault(ColDateOfJoining);
            ws.Cell(r, 7).Value = item.Payload.GetValueOrDefault(ColDateOfBirth);
            ws.Cell(r, 8).Value = item.Payload.GetValueOrDefault(ColDesignationOrGrade);
            ws.Cell(r, 9).Value = item.Payload.GetValueOrDefault(ColSumInsured);
            ws.Cell(r, 10).Value = item.Payload.GetValueOrDefault(ColMobile);
            ws.Cell(r, 11).Value = item.Payload.GetValueOrDefault(ColEmail);
            ws.Cell(r, 12).Value = string.Join(" | ", item.Errors);
            r++;
        }

        ws.Columns().AdjustToContents();
        ws.Row(1).Style.Font.Bold = true;

        using var outMs = new MemoryStream();
        wb.SaveAs(outMs);

        var cleanName = Path.GetFileNameWithoutExtension(originalFileName);
        return new ErrorFileData
        {
            FileName = $"{cleanName}_validation_errors.xlsx",
            Base64Data = Convert.ToBase64String(outMs.ToArray())
        };
    }

    [GeneratedRegex(@"^data:(?<mime>[^;]+);base64,(?<data>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DataUrlRegex();

    [GeneratedRegex(@"^\d{10}$", RegexOptions.None)]
    private static partial Regex MobileRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None)]
    private static partial Regex EmailRegex();
}

[Action(Category = "GroupInsurance.Endorsement", Description = "Row-wise endorsement BULK ADD (A).")]
public class ProcessGIEndorsementRowwiseBulkAdd : BaseGIEndorsementRowwiseUpload
{
    public ProcessGIEndorsementRowwiseBulkAdd(IConfiguration config) : base(config) { }
    protected override string NormalizeProcName => "dbo.sp_GI_EndRw_BulkAdd_FromUpload";
    protected override string ResponseVariableName => "giEndorsementAddResponse";
}

} // namespace MozartWorkflows.Elsa.Activities
