using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using MozartWorkflows.Storage.Factory;

namespace MozartWorkflows.Elsa.Activities;

[Activity(
     Category = "File Management",
     DisplayName = "Download File",
     Description = "Downloads a private file from the configured storage provider (AWS/Azure).")]
public class DownloadFileActivity : Activity
{
    private readonly IStorageServiceFactory _factory;
    public DownloadFileActivity(IStorageServiceFactory factory) => _factory = factory;

    // ─── NEW INPUT ─────────────────────────────────────────────
    [ActivityInput(
        Hint = "Application ID",
        SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript })]
    public long? ApplicationId { get; set; }
    // ───────────────────────────────────────────────────────────

    [ActivityInput(
        Hint = "Full URL stored in DB",
        SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript })]
    public string DocumentLink { get; set; } = default!;

    [ActivityInput(
        Hint = "Document MIME type (e.g., application/pdf)",
        SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript })]
    public string DocumentType { get; set; } = default!; // kept for parity

    [ActivityOutput(Hint = "Downloaded file as byte array")]
    public byte[] FileBlob { get; set; } = default!;

    protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(DocumentLink))
            return Fault("Document link is empty.");

        try
        {
            var store = _factory.Get(ApplicationId);
            FileBlob = await store.DownloadAsync(DocumentLink);
            context.SetVariable("FileBlob", FileBlob);
            return Done();
        }
        catch (Exception ex)
        {
            return Fault($"Error fetching file: {ex.Message}");
        }
    }
}
