using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using MozartWorkflows.Storage.Factory;
using System.IO.Compression;

namespace MozartWorkflows.Elsa.Activities;

[Activity(
    Category = "File Management",
    DisplayName = "Download & Zip Files",
    Description = "Downloads multiple files from the configured storage provider, zips them, and stores the ZIP in a workflow variable called ZipFileBlob.")]
public class DownloadAndZipFilesActivity : Activity
{
    private readonly IStorageServiceFactory _factory;
    public DownloadAndZipFilesActivity(IStorageServiceFactory factory) => _factory = factory;

    [ActivityInput(
        Hint = "Application ID",
        SupportedSyntaxes = new[] { SyntaxNames.Literal, SyntaxNames.JavaScript })]
    public long? ApplicationId { get; set; }

    [ActivityInput(
        Hint = "Array of object URLs to include in the ZIP.",
        SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Literal })]
    public List<string> DocumentLinks { get; set; } = new();

    private byte[] ZipFileBlob { get; set; } = default!; // internal

    protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
    {
        if (DocumentLinks is null || DocumentLinks.Count == 0)
            return Fault("DocumentLinks list is empty.");

        var store = _factory.Get(ApplicationId);

        try
        {
            await using var zipBuffer = new MemoryStream();

            // leaveOpen: true so the MemoryStream remains readable after disposing the archive.
            using (var archive = new ZipArchive(zipBuffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var url in DocumentLinks)
                {
                    var fileName = Path.GetFileName(Uri.UnescapeDataString(new Uri(url).AbsolutePath));

                    try
                    {
                        // Download as byte[] since IStorageService lacks a streaming API.
                        var bytes = await store.DownloadAsync(url);

                        // Wrap bytes in a temporary MemoryStream to stream into the ZIP entry.
                        await using var sourceStream = new MemoryStream(bytes, writable: false);

                        var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
                        await using var entryStream = entry.Open();

                        // Stream copy to avoid duplicating buffers and to ensure proper flushing.
                        await sourceStream.CopyToAsync(entryStream);
                    }
                    catch (Exception ex)
                    {
                        context.JournalData.Add($"Failed:{fileName}", ex.Message);
                    }
                }
            } // Disposing ZipArchive flushes all pending data to zipBuffer. [8][7]

            // Now read the finalized ZIP content from the underlying MemoryStream.
            zipBuffer.Seek(0, SeekOrigin.Begin);
            ZipFileBlob = zipBuffer.ToArray(); // Materialize for workflow variable consumers. [7]

            context.SetVariable("ZipFileBlob", ZipFileBlob);
            return Done();
        }
        catch (Exception ex)
        {
            return Fault($"Error while zipping files: {ex.Message}");
        }
    }
}
