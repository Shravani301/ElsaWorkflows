using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using MozartWorkflows.Storage.Factory;
using MozartWorkflows.Notifications.Interfaces; // <-- Added namespace
using System.Text.RegularExpressions;

namespace MozartWorkflows.Elsa.Activities
{
    [Activity(
        Category = "File Management",
        DisplayName = "Upload File (With Notification)",
        Description = "Uploads any file type to the configured storage provider "
                     + "(AWS / Azure / GCS), returns the URL(s), and sends a notification.")]
    public class UploadFileActivity : Activity
    {
        private readonly IStorageServiceFactory _factory;
        private readonly INotificationOrchestrator _orchestrator; // <-- Added Dependency

        private static readonly Regex DataUri =
            new(@"^data:(?<mime>[^;]+);base64,(?<data>.+)$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(2));

        // Inject INotificationOrchestrator in the constructor
        public UploadFileActivity(IStorageServiceFactory factory, INotificationOrchestrator orchestrator)
        {
            _factory = factory;
            _orchestrator = orchestrator;
        }

        // ─── Inputs (Existing) ──────────────────────────────────────────
        [ActivityInput(Label = "ApplicationId",
                       SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid })]
        public long? ApplicationId { get; set; }

        [ActivityInput(Hint = "List of files (each Data-URI or raw Base64)",
                       SupportedSyntaxes = new[] { SyntaxNames.Json, SyntaxNames.JavaScript })]
        public List<FileData> Files { get; set; } = new();

        [ActivityInput(Hint = "Reference ID used to prefix the object key",
                       SupportedSyntaxes = new[] { SyntaxNames.Liquid, SyntaxNames.JavaScript })]
        public string ReferenceId { get; set; } = default!;

        // ─── Inputs (New for Notification) ──────────────────────────────
        [ActivityInput(Label = "User ID", Hint = "User ID to receive the notification",
                       SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid })]
        public string UserId { get; set; } = null!;

        [ActivityInput(Label = "User Email", Hint = "Used if mode is Email",
                       SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid })]
        public string UserEmail { get; set; } = null!;

        [ActivityInput(Label = "Notification Mode", Hint = "e.g., 'Push' for SignalR or 'Email'",
                       SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid, SyntaxNames.Json })]
        public string NotificationMode { get; set; } = null!;

        // ─── Output ─────────────────────────────────────────
        [ActivityOutput]
        public List<UploadedFileResult> UploadedFiles { get; set; } = new();

        // ─── Execute ────────────────────────────────────────
        protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(
            ActivityExecutionContext context)
        {
            var store = _factory.Get(ApplicationId);

            // To collect filenames for the notification
            var fileNamesUploaded = new List<string>();

            foreach (var file in Files)
            {
                // 1. Decode data-URI (if any)
                (byte[] bytes, string? mime) = Decode(file);
                var cType = mime ?? file.FileType ?? "application/octet-stream";

                // 2. Build folder & file names
                var label = string.IsNullOrWhiteSpace(file.LabelName)
                              ? Path.GetFileNameWithoutExtension(file.FileName)
                              : file.LabelName;
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                var ext = Path.GetExtension(file.FileName);
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file.FileName);
                var newName = $"{fileNameWithoutExtension}_{timestamp}{ext}";
                var keyPrefix = $"Uploads/{ReferenceId}/{label}";

                // 3. Upload
                var url = await store.UploadAsync(bytes, newName, cType, keyPrefix);

                // 4. Collect result
                UploadedFiles.Add(new UploadedFileResult
                {
                    ApplicationId = ApplicationId,
                    FileName = fileNameWithoutExtension,
                    FileUrl = url,
                    FileType = cType,
                    FileLabel = label
                });

                fileNamesUploaded.Add(fileNameWithoutExtension);
            }

            context.SetVariable("uploadedFiles", UploadedFiles);

            // 5. Build and Send the Notification
            if (!string.IsNullOrEmpty(UserId) && !string.IsNullOrEmpty(NotificationMode))
            {
                var filesStr = string.Join(", ", fileNamesUploaded);
                var messageTemplate = $"Successfully uploaded {UploadedFiles.Count} file(s): {filesStr}";

                // Variables can be empty if not using any dynamic {{}} template replacements
                var variables = new Dictionary<string, string>();

                await _orchestrator.SendNotificationAsync(
                    userId: UserId,
                    userEmail: UserEmail,
                    messageTemplate: messageTemplate,
                    variables: variables,
                    mode: NotificationMode,
                    subjectTemplate: "Files Uploaded Successfully"
                );
            }

            return Done();
        }

        // ─── Helpers ────────────────────────────────────────
        private static (byte[] Bytes, string? Mime) Decode(FileData file)
        {
            var raw = file.Base64Data;
            var match = DataUri.Match(raw);
            var mime = match.Success ? match.Groups["mime"].Value : null;
            var base64 = match.Success ? match.Groups["data"].Value : raw;
            return (Convert.FromBase64String(base64), mime);
        }
    }

    // DTOs (unchanged)
    public class FileData
    {
        public string FileName { get; set; } = default!;
        public string? FileType { get; set; }
        public string Base64Data { get; set; } = default!;
        public string? LabelName { get; set; }
    }

    public class UploadedFileResult
    {
        public long? ApplicationId { get; set; }
        public string FileName { get; set; } = default!;
        public string FileUrl { get; set; } = default!;
        public string FileType { get; set; } = default!;
        public string? FileLabel { get; set; }
    }
}
