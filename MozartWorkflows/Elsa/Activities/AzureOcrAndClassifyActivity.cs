using System;
using System.Threading.Tasks;
using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using MozartWorkflows.Services;
using Newtonsoft.Json.Linq;

namespace MozartWorkflows.Elsa.Activities
{
    [Activity(
        Category = "AI Document Intelligence",
        DisplayName = "Azure OCR + Classification (Base64)",
        Description = "Performs OCR using Azure Document Intelligence using Base64 input (no URL needed)."
    )]
    public class AzureOcrAndClassifyActivity : Activity
    {
        private readonly AzureOcrService _ocr;

        public AzureOcrAndClassifyActivity(AzureOcrService ocr)
        {
            _ocr = ocr;
        }

        // INPUT: Base64 from DownloadFileActivity
        [ActivityInput(
            Label = "File Base64 Data",
            Hint = "Pass the FileBlob output from DownloadFileActivity",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Literal }
        )]
        public string FileBase64 { get; set; } = default!;

        // OUTPUTS
        [ActivityOutput] public string ExtractedText { get; set; } = "";
        [ActivityOutput] public string ExtractionJson { get; set; } = "";
        [ActivityOutput] public string DocumentType { get; set; } = "";
        [ActivityOutput] public double Confidence { get; set; } = 0;

        protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(FileBase64))
                return Fault("FileBase64 input cannot be empty.");

            try
            {
                // Strip data URI prefix (data:application/pdf;base64,...)
                string cleanBase64 =
                    FileBase64.Contains(',')
                        ? FileBase64.Split(',')[1]
                        : FileBase64;

                byte[] fileBytes = Convert.FromBase64String(cleanBase64);

                // 1) Call Azure OCR (hybrid)
                var result = await _ocr.AnalyzeHybridAsync(fileBytes);

                ExtractedText = result.FullText;

                //----------------------------------------------------------------
                // Classification (using text + KV fields)
                //----------------------------------------------------------------
                var (label, confidence, extraScores) =
                    Classification.Classify(ExtractedText, result.Fields);

                DocumentType = label;
                Confidence = confidence;

                //----------------------------------------------------------------
                // Enrich payload JSON with classification
                //----------------------------------------------------------------
                try
                {
                    var payloadObj = JObject.FromObject(result.Payload);

                    payloadObj["documentType"] = label;
                    payloadObj["documentTypeConfidence"] = confidence;

                    if (extraScores != null && extraScores.Count > 0)
                    {
                        // convert Dictionary -> JObject explicitly
                        payloadObj["documentTypeDebug"] = JObject.FromObject(extraScores);
                    }

                    ExtractionJson = payloadObj.ToString();
                }
                catch
                {
                    // fallback – at least return base payload
                    ExtractionJson = result.Payload.ToString();
                }

                //----------------------------------------------------------------
                // Store Elsa Variables
                //----------------------------------------------------------------
                context.SetVariable("ocr_text", ExtractedText);
                context.SetVariable("ocr_json", ExtractionJson);
                context.SetVariable("ocr_label", DocumentType);
                context.SetVariable("ocr_confidence", Confidence);

                return Done();

            }
            catch (Exception ex)
            {
                return Fault($"OCR failed: {ex.Message}");
            }
        }
    }
}
