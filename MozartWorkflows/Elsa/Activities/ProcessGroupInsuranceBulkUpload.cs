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

[Action(Category = "GroupInsurance", Description = "Validates row-wise Group Insurance Excel, blocks existing EmployeeIds, and generates error Excel when validation fails.")]
public class ProcessGINormalQuoteRowwiseBulkUpload : Activity
{
    private const string ColEmployeeId = "EmployeeId";
    private const string ColMemberName = "MemberName";
    private const string ColRelationship = "Relationship";
    private const string ColIsNominee = "IsNominee";
    private const string ColGender = "Gender";
    private const string ColDateOfJoining = "DateOfJoining";
    private const string ColDateOfBirth = "DateOfBirth";
    private const string ColSumInsured = "SumInsured";
    private const string ColMobile = "Mobile";
    private const string ColEmail = "Email";
    private const string ColDesignationOrGrade = "DesignationOrGrade";
    private const string ColDateOfLeaving = "DateOfLeaving";
    private const string RelSelf = "Self";
    private const string RelEmployee = "Employee";
    private const string ResponseVar = "giUploadResponse";
    private const string SpGetUploadSummary = "dbo.sp_GI_EndRw_GetUploadSummary";
    private const string SpGetUploadErrors = "dbo.sp_GI_EndRw_GetUploadErrors";
    private const string SpRecalcSummary = "dbo.sp_GI_EndRw_RecalculateUploadSummary";
    private static readonly Regex Base64DataUriRegex = new(@"^data:(?<mime>[^;]+);base64,(?<data>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly Regex MobileRegex = new(@"^\d{10}$", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private readonly IConfiguration _config;
    public ProcessGINormalQuoteRowwiseBulkUpload(IConfiguration config) => _config = config;

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
        public string FileType { get; set; } = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
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
        public Dictionary<string, string?> Payload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Errors { get; set; } = new();
    }

    private static readonly Dictionary<string, string> HeaderMap = new(StringComparer.OrdinalIgnoreCase)
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
        ["Designation/Grade"] = ColDesignationOrGrade,
        ["IsNominee"] = ColIsNominee
    };

    private static readonly HashSet<string> RequiredColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ColEmployeeId, ColMemberName, ColRelationship, ColGender, ColDateOfJoining, ColDateOfBirth, ColSumInsured
    };

    private static readonly HashSet<string> DateKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ColDateOfJoining, ColDateOfBirth, ColDateOfLeaving
    };

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

    [ActivityOutput] public object? OutputJson { get; set; }
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

            await conn.ExecuteAsync("dbo.sp_GI_EndRw_CreateUpload", p, commandType: CommandType.StoredProcedure);
            uploadId = p.Get<long>("@UploadId");

            // 🔥 DUPLICATE HIT PROTECTION
          

            var status = await conn.ExecuteScalarAsync<string>(
      "SELECT Status FROM dbo.GI_UploadHistory WHERE UploadId = @UploadId",
      new { UploadId = uploadId }
  );

            if (status == "COMPLETED")
            {
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

                OutputJson = new
                {
                    success = true,
                    message = "Upload already processed. Returning existing result.",
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
                };

                context.SetVariable(ResponseVar, OutputJson);
                return Done();
            }
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
                if (!string.IsNullOrWhiteSpace(val)) anyValue = true;
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


        // 🔥 Build EmployeeId → Relationships map
        var empRelationMap = parsedRows
            .Where(x => x.Payload.TryGetValue(ColEmployeeId, out var id) && !string.IsNullOrWhiteSpace(id))
            .GroupBy(x => x.Payload[ColEmployeeId]!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.Payload.GetValueOrDefault(ColRelationship)?.Trim())
                      .Where(r => !string.IsNullOrWhiteSpace(r))
                      .ToList(),
                StringComparer.OrdinalIgnoreCase
            );

        var empNomineeCountMap = parsedRows
    .Where(x => x.Payload.TryGetValue(ColEmployeeId, out var id) && !string.IsNullOrWhiteSpace(id))
    .GroupBy(x => x.Payload[ColEmployeeId]!, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(
        g => g.Key,
        g => g.Count(r =>
        {
            var nominee = r.Payload.GetValueOrDefault(ColIsNominee)?.Trim();
            return nominee != null &&
                   (nominee.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                    nominee.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                    nominee.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                    nominee.Equals("1", StringComparison.OrdinalIgnoreCase));
        }),
        StringComparer.OrdinalIgnoreCase
    );

        var employeeIdsInUpload = parsedRows
            .Select(x => x.Payload.GetValueOrDefault(ColEmployeeId))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        HashSet<string> existingEmployeeIds;
        await using (var conn = new SqlConnection(connStr))
        {
            existingEmployeeIds = (await conn.QueryAsync<string>(
                @"
        SELECT DISTINCT EmployeeId
        FROM dbo.InsuranceEnrollmentMaster WITH (NOLOCK)
        WHERE ApplicationId = 8
          AND IsActive = 1
          AND EmployeeId IN @EmployeeIds",
                new
                {
                    CaseId = CaseId,
                    EmployeeIds = employeeIdsInUpload.Count == 0
                        ? new List<string> { "__NO_MATCH__" }
                        : employeeIdsInUpload
                }))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var row in parsedRows)
        {
            var empId = row.Payload.GetValueOrDefault(ColEmployeeId)?.Trim();

            if (!string.IsNullOrWhiteSpace(empId))
            {
                if (existingEmployeeIds.Contains(empId))
                    row.Errors.Add($"EmployeeId '{empId}' already exists for this case.");

                if (empRelationMap.TryGetValue(empId, out var relations))
                {
                    var selfCount = relations.Count(r =>
                        string.Equals(r, RelSelf, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r, RelEmployee, StringComparison.OrdinalIgnoreCase));

                    if (selfCount == 0)
                        row.Errors.Add("Self/Employee row is required for each EmployeeId in fresh upload.");

                    if (selfCount > 1)
                        row.Errors.Add("Multiple Self/Employee rows found for same EmployeeId.");
                }
            }

            // ADD THIS BLOCK HERE
            var relation = row.Payload.GetValueOrDefault(ColRelationship)?.Trim();
            var isNominee = row.Payload.GetValueOrDefault(ColIsNominee)?.Trim();

            var yesNominee =
                string.Equals(isNominee, "Yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(isNominee, "Y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(isNominee, "True", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(isNominee, "1", StringComparison.OrdinalIgnoreCase);

            if (yesNominee &&
                (string.Equals(relation, RelSelf, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(relation, RelEmployee, StringComparison.OrdinalIgnoreCase)))
            {
                row.Errors.Add("Employee/Self cannot be marked as nominee.");
            }

            if (!string.IsNullOrWhiteSpace(empId) &&
                empNomineeCountMap.TryGetValue(empId, out var nomineeCount) &&
                nomineeCount > 1)
            {
                row.Errors.Add("Only one nominee is allowed per EmployeeId.");
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

            OutputJson = new
            {
                success = false,
                message = "Validation failed. No records were inserted into master tables.",
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
            };

            context.SetVariable(ResponseVar, OutputJson);
            

            
            return Done();
        }

        await using (var conn = new SqlConnection(connStr))
        {
            await conn.ExecuteAsync(
                "dbo.sp_GI_NormalizeRowwiseUploadToEnrollment",
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

            OutputJson = new
            {
                success = true,
                message = "Row-wise group insurance upload processed.",
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
            };
            context.SetVariable(ResponseVar, OutputJson);
        }

        return Done();
    }
#pragma warning restore S3776

    private static string? NormalizeNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

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

    private static string? ReadCell(IXLCell cell, string jsonKey)
    {
        if (cell.IsEmpty()) return null;

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
                return num % 1 == 0
                    ? decimal.Truncate(num).ToString(CultureInfo.InvariantCulture)
                    : num.ToString(CultureInfo.InvariantCulture);

            var s = cell.GetFormattedString().Trim();
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Replace(",", "");
        }

        var str = cell.GetFormattedString().Trim();
        return string.IsNullOrWhiteSpace(str) ? null : str;
    }

#pragma warning disable S3776
    private static List<string> ValidateRow(Dictionary<string, string?> d, HashSet<string> required)
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
                RelSelf, RelEmployee, "Spouse", "Child", "Parent", "Parent in law", "ParentInLaw", "ParentIn-Law"
            };

            if (!allowed.Contains(rel.Trim()))
                errs.Add("Relationship must be one of Self/Employee/Spouse/Child/Parent/Parent in law");
        }
        var isNominee = d.GetValueOrDefault(ColIsNominee);
        if (!string.IsNullOrWhiteSpace(isNominee))
        {
            var allowedNominee = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Yes", "No", "Y", "N", "True", "False", "1", "0"
    };

            if (!allowedNominee.Contains(isNominee.Trim()))
                errs.Add("IsNominee must be Yes/No");
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
        if (!string.IsNullOrWhiteSpace(mobile) && !MobileRegex.IsMatch(mobile))
            errs.Add("Mobile must be 10 digits");

        var email = d.GetValueOrDefault(ColEmail);
        if (!string.IsNullOrWhiteSpace(email) && !EmailRegex.IsMatch(email))
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
            ColIsNominee,
            ColGender,
            ColDateOfJoining,
            ColDateOfBirth,
            ColDesignationOrGrade,
            "SumInsured",
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
            ws.Cell(r, 5).Value = item.Payload.GetValueOrDefault(ColIsNominee);
            ws.Cell(r, 6).Value = item.Payload.GetValueOrDefault(ColGender);
            ws.Cell(r, 7).Value = item.Payload.GetValueOrDefault(ColDateOfJoining);
            ws.Cell(r, 8).Value = item.Payload.GetValueOrDefault(ColDateOfBirth);
            ws.Cell(r, 9).Value = item.Payload.GetValueOrDefault(ColDesignationOrGrade);
            ws.Cell(r, 10).Value = item.Payload.GetValueOrDefault(ColSumInsured);
            ws.Cell(r, 11).Value = item.Payload.GetValueOrDefault(ColMobile);
            ws.Cell(r, 12).Value = item.Payload.GetValueOrDefault(ColEmail);
            ws.Cell(r, 13).Value = string.Join(" | ", item.Errors);
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
