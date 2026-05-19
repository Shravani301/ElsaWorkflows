using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace MozartWorkflows.Elsa.Activities
{
    [Activity(
        DisplayName = "Generate GI Upload Error Excel",
        Description = "Generates a re-uploadable Excel (template layout) containing INVALID rows for a given UploadId.",
        Category = "GroupInsurance"
    )]
    public class GenerateGIUploadErrorExcel : Activity
    {
        private readonly IConfiguration _config;
        public GenerateGIUploadErrorExcel(IConfiguration config) => _config = config;

        [ActivityInput(Hint = "UploadId whose INVALID rows must be exported.", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Literal })]
        public long UploadId { get; set; }

        [ActivityOutput] public byte[]? ExcelFileBlob { get; set; }
        [ActivityOutput] public string? ExcelFileName { get; set; }

#pragma warning disable S3459, S1144 // Dapper maps these properties via reflection
        private sealed class GiUploadRow
        {
            public int RowNumber { get; set; }
            public string? EmployeeId { get; set; }
            public string? ErrorMessage { get; set; }
            public string? RowData { get; set; }
        }
#pragma warning restore S3459, S1144

        public override async ValueTask<IActivityExecutionResult> ExecuteAsync(ActivityExecutionContext context)
        {
            var connStr = _config.GetConnectionString("connectionString");
            await using var conn = new SqlConnection(connStr);

            _ = await conn.ExecuteScalarAsync<string?>(
                @"SELECT CaseId FROM dbo.GI_UploadHistory WHERE UploadId = @UploadId",
                new { UploadId }
            );

            var rows = (await conn.QueryAsync<GiUploadRow>(
                @"SELECT RowNumber, EmployeeId, ErrorMessage, RowData
                  FROM dbo.GI_UploadRows
                  WHERE UploadId = @UploadId AND Status = 'INVALID'
                  ORDER BY RowNumber",
                new { UploadId }
            )).AsList();

            // Helper columns (needed for stable updates)
            var helperCols = new List<string> { "RowNumber", "ErrorMessage" };

            // Template columns (WITHOUT *)
            var templateCols = new List<string>
            {
                "ApplicationId","ProductType","PlanVariant",
                "EmployeeId","EmployeeName","EmployeeGender","EmployeeDOB","EmployeeAge","Email","EmployeeLevel","Mobile","EmployeeAadhaarNo","EmployeePANNo",
                "AddressLine1","AddressLine2","City","State","Pincode",
                "AccountNo","IFSC","BankName","AccountHolder",
                "NomineeName","NomineeRelation","NomineeDOB","NomineeMobile",
                "PolicyNumber","PolicyStatus","SumInsured",
                "SpouseName","SpouseGender","SpouseDOB","SpouseAge","SpouseMobile","SpouseAadhaarNo","SpousePANNo",
                "Parent1Name","Parent1Gender","Parent1Relation","Parent1DOB","Parent1Age","Parent1Mobile","Parent1AadhaarNo","Parent1PANNo",
                "Parent2Name","Parent2Gender","Parent2Relation","Parent2DOB","Parent2Age","Parent2Mobile","Parent2AadhaarNo","Parent2PANNo",
                "Child1Name","Child1Gender","Child1DOB","Child1Age","Child1Mobile","Child1AadhaarNo","Child1PANNo",
                "Child2Name","Child2Gender","Child2DOB","Child2Age","Child2Mobile","Child2AadhaarNo","Child2PANNo"
            };

            // ✅ Required columns = your BULK UPLOAD required list
            var requiredCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
     "ApplicationId","EmployeeId","EmployeeName","EmployeeGender","EmployeeAge",
    "Email","Mobile",
    "AddressLine1",
    "AccountNo","IFSC",
    "NomineeName","NomineeRelation"
};


            var allCols = helperCols.Concat(templateCols).ToList();

            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("GroupInsurance_Upload");

            ws.Cell(1, 1).Value =
                "Fix the invalid rows and re-upload. Do NOT change RowNumber. Keep headers in Row 2. Data starts from Row 3.";
            ws.Range(1, 1, 1, allCols.Count).Merge();
            ws.Row(1).Style.Font.Bold = true;

            const int headerRow = 2;
            const int dataStartRow = 3;

            // Headers (helper cols without *, template cols with * based on requiredCols)
            for (int i = 0; i < allCols.Count; i++)
            {
                var colName = allCols[i];
                var isHelper = helperCols.Contains(colName);
                var headerText = (!isHelper && requiredCols.Contains(colName)) ? $"{colName}*" : colName;

                ws.Cell(headerRow, i + 1).Value = headerText;
                ws.Cell(headerRow, i + 1).Style.Font.Bold = true;
            }

            int r = dataStartRow;
            foreach (var row in rows)
            {
                var map = ParseToStringMap(row.RowData ?? "");

                // Helper col values
                ws.Cell(r, 1).Value = row.RowNumber;
                ws.Cell(r, 2).Value = row.ErrorMessage ?? "";

                for (int i = 0; i < templateCols.Count; i++)
                {
                    var key = templateCols[i];
                    string? val = "";

                    if (key.Equals("ApplicationId", StringComparison.OrdinalIgnoreCase))
                        val = "8";
                    else
                    {
                        map.TryGetValue(key, out val);
                        val ??= "";
                    }


                    ws.Cell(r, helperCols.Count + i + 1).Value = val;
                }

                r++;
            }

            ws.SheetView.FreezeRows(2);
            ws.Range(headerRow, 1, Math.Max(headerRow, r - 1), allCols.Count).SetAutoFilter();
            ws.Columns().AdjustToContents();

            // Optional: keep ErrorInfo sheet too
            var wsErr = wb.AddWorksheet("GI_ErrorInfo");
            wsErr.Cell(1, 1).Value = "RowNumber";
            wsErr.Cell(1, 2).Value = "EmployeeId";
            wsErr.Cell(1, 3).Value = "ErrorMessage";
            wsErr.Range(1, 1, 1, 3).Style.Font.Bold = true;

            int er = 2;
            foreach (var row in rows)
            {
                wsErr.Cell(er, 1).Value = row.RowNumber;
                wsErr.Cell(er, 2).Value = row.EmployeeId ?? "";
                wsErr.Cell(er, 3).Value = row.ErrorMessage ?? "";
                er++;
            }
            wsErr.SheetView.FreezeRows(1);
            wsErr.Range(1, 1, Math.Max(1, er - 1), 3).SetAutoFilter();
            wsErr.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);

            ExcelFileBlob = ms.ToArray();
            ExcelFileName = $"GI_Upload_{UploadId}_ErrorRows_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";

            context.SetVariable("ExcelFileBlob", ExcelFileBlob);
            context.SetVariable("ExcelFileName", ExcelFileName);

            return Done();
        }

        private static Dictionary<string, string> ParseToStringMap(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new();

            if (TryParseObject(json, out var map))
                return map;

            try
            {
                using var outer = JsonDocument.Parse(json);
                if (outer.RootElement.ValueKind == JsonValueKind.String)
                {
                    var inner = outer.RootElement.GetString();
                    if (!string.IsNullOrWhiteSpace(inner) && TryParseObject(inner!, out map))
                        return map;
                }
            }
            catch (JsonException) { /* non-JSON string — return empty map */ }

            return new();

            static bool TryParseObject(string s, out Dictionary<string, string> dict)
            {
                dict = new();
                try
                {
                    using var doc = JsonDocument.Parse(s);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

                    foreach (var prop in doc.RootElement.EnumerateObject())
                        dict[prop.Name] = ToString(prop.Value);

                    return dict.Count > 0;
                }
                catch
                {
                    dict = new();
                    return false;
                }
            }

            static string ToString(JsonElement v) =>
                v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString() ?? "",
                    JsonValueKind.Number => v.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "",
                    _ => v.GetRawText()
                };
        }
    }
}
