using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace MozartWorkflows.Services
{
    public partial class AzureOcrService
    {
        private const string AnalyzeResultKey = "analyzeResult";
        private const string ContentKey = "content";
        private const string FieldsKey = "fields";
        private const string FieldConfidenceKey = "fieldConfidence";

        private readonly DocumentIntelligenceClient _client;
        private readonly string _modelId;

        public AzureOcrService(IConfiguration config)
        {
            var sec = config.GetSection("AzureDocumentIntelligence");

            _client = new DocumentIntelligenceClient(
                new Uri(sec["Endpoint"] ?? throw new InvalidOperationException("AzureDocumentIntelligence:Endpoint is required")),
                new AzureKeyCredential(sec["ApiKey"] ?? throw new InvalidOperationException("AzureDocumentIntelligence:ApiKey is required"))
            );

            _modelId = sec["ModelId"] ?? "prebuilt-document";
        }

        // --------------------------------------------------------------------
        // GENERIC LAYOUT + KV + TABLES
        // --------------------------------------------------------------------
#pragma warning disable S3776
        public async Task<AzureOcrResult> AnalyzeFromBytesAsync(byte[] bytes)
        {
            string base64 = Convert.ToBase64String(bytes);
            string jsonPayload = $@"{{ ""base64Source"": ""{base64}"" }}";

            RequestContent requestContent = RequestContent.Create(jsonPayload);

            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                _modelId,
                requestContent,
                features: new[] { DocumentAnalysisFeature.KeyValuePairs }
            );

            BinaryData responseJson = operation.Value;
            string rawJson = responseJson.ToString();
            JObject root = JObject.Parse(rawJson);

            // -------------------------- Full text -----------------------------
            string extractedText =
                root[AnalyzeResultKey]?[ContentKey]?.ToString() ?? string.Empty;

            // -------------------------- KeyValuePairs -------------------------
            JObject extractedFields = new();
            JObject confidenceMap = new();

            var kvPairs = root[AnalyzeResultKey]?["keyValuePairs"];
            if (kvPairs != null)
            {
                foreach (var kv in kvPairs)
                {
                    string keyRaw = kv["key"]?[ContentKey]?.ToString()?.Trim() ?? "";
                    string valueRaw = kv["value"]?[ContentKey]?.ToString()?.Trim() ?? "";
                    double conf = kv["confidence"]?.Value<double>() ?? 0;

                    string key = NormalizeKey(keyRaw);
                    string value = NormalizeKey(valueRaw);

                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    if (extractedFields.ContainsKey(key))
                    {
                        var existing = extractedFields[key]?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(value) && !existing.Contains(value))
                            extractedFields[key] = existing + " | " + value;

                        var existingConf = confidenceMap[key]?.Value<double>() ?? 0;
                        if (conf > existingConf)
                            confidenceMap[key] = conf;
                    }
                    else
                    {
                        extractedFields[key] = value;
                        confidenceMap[key] = conf;
                    }
                }
            }

            // -------------------------- Tables -------------------------------
            JArray tablesArr = new();

            var tables = root[AnalyzeResultKey]?["tables"];
            if (tables != null)
            {
                foreach (var table in tables)
                {
                    int rowCount = table["rowCount"]?.Value<int>() ?? 0;
                    int colCount = table["columnCount"]?.Value<int>() ?? 0;

                    JArray rows = new();

                    for (int r = 0; r < rowCount; r++)
                    {
                        JArray row = new();
                        for (int c = 0; c < colCount; c++)
                        {
                            var cell = table["cells"]?
                                .FirstOrDefault(x =>
                                    x["rowIndex"]?.Value<int>() == r &&
                                    x["columnIndex"]?.Value<int>() == c);

                            string cellText = CleanCell(cell?[ContentKey]?.ToString());
                            row.Add(cellText);
                        }
                        rows.Add(row);
                    }

                    tablesArr.Add(rows);
                }
            }

            // ---------------------- Handwriting spans ------------------------
            JArray handwritingSpans = new();

            var styles = root[AnalyzeResultKey]?["styles"];
            if (styles != null)
            {
                foreach (var style in styles)
                {
                    if (style["isHandwritten"]?.Value<bool>() == true)
                    {
                        var spans = style["spans"];
                        if (spans != null)
                        {
                            foreach (var sp in spans)
                            {
                                handwritingSpans.Add(new JObject
                                {
                                    ["offset"] = sp["offset"]?.Value<int>() ?? 0,
                                    ["length"] = sp["length"]?.Value<int>() ?? 0
                                });
                            }
                        }
                    }
                }
            }

            // ---------------------- Final compact JSON ------------------------
            JObject finalResult = new()
            {
                [ContentKey] = extractedText,
                [FieldsKey] = extractedFields,
                [FieldConfidenceKey] = confidenceMap,
                ["tables"] = tablesArr,
                ["handwritingSpans"] = handwritingSpans
            };

            return new AzureOcrResult
            {
                FullText = extractedText,
                Fields = extractedFields,
                Payload = finalResult
            };
        }
#pragma warning restore S3776

        // --------------------------------------------------------------------
        // ID DOCUMENT MODEL (Aadhaar, PAN, etc.)
        // --------------------------------------------------------------------
        public async Task<AzureOcrResult> AnalyzeIdDocumentFromBytesAsync(byte[] bytes)
        {
            string base64 = Convert.ToBase64String(bytes);
            string jsonPayload = $@"{{ ""base64Source"": ""{base64}"" }}";
            RequestContent requestContent = RequestContent.Create(jsonPayload);

            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-idDocument",
                requestContent
            );

            BinaryData responseJson = operation.Value;
            string rawJson = responseJson.ToString();
            JObject root = JObject.Parse(rawJson);

            string extractedText =
                root[AnalyzeResultKey]?[ContentKey]?.ToString() ?? string.Empty;

            JObject extractedFields = new();
            JObject confidenceMap = new();

            var docs = root[AnalyzeResultKey]?["documents"];
            if (docs != null && docs.Any())
            {
                var fields = docs[0]?[FieldsKey] as JObject;
                if (fields != null)
                {
                    foreach (var f in fields)
                    {
                        string key = NormalizeKey(f.Key);

                        string value =
                            f.Value?[ContentKey]?.ToString() ??
                            f.Value?["valueString"]?.ToString() ??
                            f.Value?["valueDate"]?.ToString() ??
                            f.Value?["valueNumber"]?.ToString() ??
                            "";

                        double conf = f.Value?["confidence"]?.Value<double>() ?? 0;

                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            extractedFields[key] = value?.Trim() ?? "";
                            confidenceMap[key] = conf;
                        }
                    }
                }
            }

            JObject finalResult = new()
            {
                [ContentKey] = extractedText,
                [FieldsKey] = extractedFields,
                [FieldConfidenceKey] = confidenceMap,
                ["tables"] = new JArray(),
                ["handwritingSpans"] = new JArray()
            };

            return new AzureOcrResult
            {
                FullText = extractedText,
                Fields = extractedFields,
                Payload = finalResult
            };
        }

        // --------------------------------------------------------------------
        // HYBRID: LAYOUT + (OPTIONAL) ID ENRICHMENT
        // --------------------------------------------------------------------
        public async Task<AzureOcrResult> AnalyzeHybridAsync(byte[] bytes)
        {
            // 1) Always run generic layout model
            var layoutResult = await AnalyzeFromBytesAsync(bytes);

            AzureOcrResult? idResult = null;
            try
            {
                // 2) Try ID document model (PAN, Aadhaar, passport, etc.)
                idResult = await AnalyzeIdDocumentFromBytesAsync(bytes);
            }
            catch
            {
                // If id model fails, just fall back to layout only.
                idResult = null;
            }

            // 3) If ID model returned nothing meaningful -> just return layout.
            if (idResult == null ||
                idResult.Fields == null ||
                !idResult.Fields.Properties().Any())
            {
                return layoutResult;
            }

            // 4) Merge layout fields + ID fields
            var mergedFields = new JObject(layoutResult.Fields);
            var mergedConfidence = new JObject(
                layoutResult.Payload?[FieldConfidenceKey] as JObject ?? new JObject()
            );

            // ID confidence map (may be null)
            var idConfidenceObj = idResult.Payload?[FieldConfidenceKey] as JObject ?? new JObject();

            foreach (var prop in idResult.Fields.Properties())
            {
                // Overwrite or add ID-specific field (like DocumentNumber, FirstName, etc.)
                mergedFields[prop.Name] = prop.Value;

                // If ID model has a confidence for this field, use it.
                if (idConfidenceObj.TryGetValue(prop.Name, out var confToken))
                    mergedConfidence[prop.Name] = confToken;
            }

            // 5) Build final payload
            var finalPayload = new JObject(layoutResult.Payload ?? new JObject());
            finalPayload["fields"] = mergedFields;
            finalPayload[FieldConfidenceKey] = mergedConfidence;
            finalPayload["idModelUsed"] = true; // optional flag if you like

            return new AzureOcrResult
            {
                FullText = layoutResult.FullText,
                Fields = mergedFields,
                Payload = finalPayload
            };
        }


        // Helpers
        private static string NormalizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";

            key = key.Trim();
            key = key.TrimEnd(':', '：');
            key = MultiSpaceRegex().Replace(key, " ");

            return key;
        }

        private static string CleanCell(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return "";

            s = s.Replace(":unselected:", "", StringComparison.OrdinalIgnoreCase);
            s = CellWhitespaceRegex().Replace(s, " ");

            return s.Trim();
        }

        [GeneratedRegex(@"\s{2,}", RegexOptions.None)]
        private static partial Regex MultiSpaceRegex();

        [GeneratedRegex(@"\s+", RegexOptions.None)]
        private static partial Regex CellWhitespaceRegex();
    }

    public class AzureOcrResult
    {
        public string FullText { get; set; } = "";
        public JObject Fields { get; set; } = new();
        public JObject Payload { get; set; } = new();
    }
}
