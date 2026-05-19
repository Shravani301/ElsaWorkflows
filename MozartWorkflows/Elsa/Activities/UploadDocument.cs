using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Design;
using Elsa.Expressions;
using MozartWorkflows.Models;
using MozartWorkflows.Services;
using Elsa.Services;
using Elsa.Services.Models;
using Newtonsoft.Json;
using NLog;
using ILogger = NLog.ILogger;
using Elsa;

namespace MozartWorkflows.Elsa.Activities;

[Action(
    Category = "MyActivity",
    Description = "Uploding Files."
)]
public class UploadDocument : Activity
{
    public readonly ConfigManager _configManager;
    private readonly ILogger _logger;
    public UploadDocument(ConfigManager configManager)
    {
        _configManager = configManager; 
        _logger =LogManager.GetCurrentClassLogger();
    }

    [ActivityInput(
        Label = "FieldInvestigatorId",
        SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
    )]
    public string FI_Id { get; set; } = default!;

    [ActivityInput(
        Label = "ClaimID",
        SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
    )]
    public string ClaimID { get; set; } = default!;
    [ActivityInput(
        Label = "DocumentType",
        SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
    )]
    public string DocumentType { get; set; } = default!;
    [ActivityInput(
        Label = "SubFolder",
        SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
    )]
    public string SubFolder { get; set; } = default!;


    [ActivityInput(Hint = "The Uploded Document.", UIHint = ActivityInputUIHints.Json, DefaultSyntax = SyntaxNames.Json, SupportedSyntaxes = new[] { SyntaxNames.Json, SyntaxNames.JavaScript })]
    public UplodedFile? UplodedDocuments { get; set; }
    [ActivityOutput] public string? Output { get; set; }
    protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
    {
        var resVal = new UploadFileResponse { UploadFileStatus = false, FilePath = "", StatusCode = 404, Message = "Path not Found" };
        try
        {
            var path = _configManager.GetConfigurationItem("DocumentFolder");
            if (UplodedDocuments == null || path == null)
            {
                _logger.Warn("UplodedDocuments is Null or Path is Empty.");
                Output = JsonConvert.SerializeObject(resVal);
                return Outcomes(OutcomeNames.False);
            }
            var uploads = Path.Combine(path, "Uploads");
            if (!Directory.Exists(uploads))
            {
                Directory.CreateDirectory(uploads);
            }
            var uploadsFolder = Path.Combine(uploads, ClaimID);
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }
            var fiFolder = Path.Combine(uploadsFolder, FI_Id);
            if (!Directory.Exists(fiFolder))
            {
                Directory.CreateDirectory(fiFolder);
            }
            var subFolder = Path.Combine(fiFolder, SubFolder);
            if (!Directory.Exists(subFolder))
            {
                Directory.CreateDirectory(fiFolder);
            }
            var documnetFolder = Path.Combine(subFolder, DocumentType);
            if (!Directory.Exists(documnetFolder))
            {
                Directory.CreateDirectory(documnetFolder);
            }
            if (UplodedDocuments.FileName != null && UplodedDocuments.Base64String != null)
            {
                var filePath = Path.Combine(documnetFolder, UplodedDocuments.FileName);
                var ByteArray = Convert.FromBase64String(UplodedDocuments.Base64String);
                await File.WriteAllBytesAsync(filePath, ByteArray);
                _logger.Info("{FileName} uploaded in {Folder} successfully", UplodedDocuments.FileName, documnetFolder);
                resVal.UploadFileStatus = true;
                resVal.FilePath = subFolder;
                resVal.StatusCode = 200;    
                resVal.Message="File Uploded Successfully.";
            }
            Output = JsonConvert.SerializeObject(resVal);
            return Outcomes(OutcomeNames.Done);
        }
        catch (Exception error)
        {
            _logger.Error(error, "Error Status Code: {StatusCode}", 500);
            resVal.StatusCode=500;
            resVal.Message = error.Message; 
            Output = JsonConvert.SerializeObject(resVal);
            return Outcomes("Faulted");
        }
    }
}
