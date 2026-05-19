using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using ClosedXML.Excel;
using System.Text.Json;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace MCWorkflow.Elsa.Activities
{
    [Activity(
        DisplayName = "Generate Multiple Excel Files and Zip",
        Description = "Generates multiple Excel files from JSON, zips them, and returns as byte[].",
        Category = "Custom"
    )]
    public class GenerateMultipleExcelFeedFilesAndZip : Activity
    {
        [ActivityInput(Hint = "List of JSON strings (one per claim)", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Literal })]
        public List<string> FormsJsonList { get; set; } = new();

        [ActivityInput(Hint = "List of filenames for Excel files", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Literal })]
        public List<string> FileNames { get; set; } = new();

        [ActivityOutput] public byte[]? ZipFileBlob { get; set; }
        [ActivityOutput] public string? ZipFileName { get; set; }

        public override ValueTask<IActivityExecutionResult> ExecuteAsync(ActivityExecutionContext context)
        {
            if (FormsJsonList == null || FileNames == null || FormsJsonList.Count != FileNames.Count)
                return new ValueTask<IActivityExecutionResult>(Fault("Mismatch or null in FormsJsonList and FileNames."));

            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                for (int i = 0; i < FormsJsonList.Count; i++)
                {
                    var raw = FormsJsonList[i] ?? "";
                    var name = string.IsNullOrWhiteSpace(FileNames[i]) ? $"File_{i + 1}" : FileNames[i];
                    name = Sanitize(name) + ".xlsx";

                    var map = ParseToStringMap(raw);   // <-- robust parser (see below)

                    using var wb = new XLWorkbook();
                    var ws = wb.AddWorksheet("Finance Feed");

                    if (map.Count == 0)
                    {
                        // Helpful breadcrumb so "empty" still shows why
                        ws.Cell(1, 1).Value = "No fields parsed";
                        ws.Cell(2, 1).Value = raw.Length > 0 ? "Check JSON/value types" : "Empty JSON";
                    }
                    else
                    {
                        int col = 1;
                        foreach (var kv in map)
                        {
                            ws.Cell(1, col).Value = kv.Key;
                            ws.Cell(1, col).Style.Font.Bold = true;
                            ws.Cell(2, col).Value = kv.Value ?? "";
                            col++;
                        }
                        ws.Columns().AdjustToContents();
                    }

                    var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    wb.SaveAs(entryStream);  // stream straight into the zip
                }
            }

            zipStream.Position = 0;
            ZipFileBlob = zipStream.ToArray();
            ZipFileName = $"FinanceClaimsFeed_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";

            context.SetVariable("ZipFileBlob", ZipFileBlob);
            context.SetVariable("ZipFileName", ZipFileName);

            return new ValueTask<IActivityExecutionResult>(Done());
        }

        private static string Sanitize(string fileName)
        {
            var invalid = new string(Path.GetInvalidFileNameChars()) + "/\\";
            return Regex.Replace(fileName, $"[{Regex.Escape(invalid)}]+", "_", RegexOptions.None, TimeSpan.FromSeconds(2));
        }

        // Accepts:
        //  - {"A":"1","B":2,"C":true,"D":{"x":1},"E":[1,2]}
        //  - "\"{\\\"A\\\":\\\"1\\\"}\"" (double-encoded)
        private static Dictionary<string, string> ParseToStringMap(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new();

            if (TryParseObject(json, out var map))
                return map;

            // unwrap double-encoded
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
            catch { /* ignore */ }

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
                catch { dict = new(); return false; }
            }

            static string ToString(JsonElement v) =>
                v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString() ?? "",
                    JsonValueKind.Number => v.GetRawText(), // preserves 600.00
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "",
                    _ => v.GetRawText()  // objects/arrays → JSON text
                };
        }
    }
}
