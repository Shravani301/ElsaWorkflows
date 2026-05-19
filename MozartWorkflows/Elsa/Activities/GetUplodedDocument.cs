using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using Newtonsoft.Json.Linq;
using NLog;
using ILogger = NLog.ILogger;

namespace MozartWorkflows.Elsa.Activities;

[Action(
    Category = "MyActivity",
    Description = "GetUploded Document."
)]
public class GetUplodedDocument: Activity
{
    private readonly ILogger _logger;
    public GetUplodedDocument()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }
    [ActivityInput(
            Label = "Root Folder Path",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
    public string FilePath { get; set; } = default!;
    [ActivityOutput] public List<JObject> Output { get; set; } = default!;
    protected override ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
    {
        List<JObject> uplodedDocument = new List<JObject>();
        try
        {
            if(Directory.Exists(FilePath))
            {
                string[] allFiles = Directory.GetFiles(FilePath, "*.*", SearchOption.AllDirectories);
                if (allFiles.Length > 0)
                {
                    Parallel.ForEach(allFiles, fpath =>
                    {
                        string fileName = Path.GetFileName(fpath);
                        if (!fileName.StartsWith("Delete_", StringComparison.Ordinal))
                        {
                            var parentFolderName = Path.GetFileName(Path.GetDirectoryName(fpath));
                            var fieldInvestigatorID = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(fpath)));
                            _logger.Info("{FileName} from {ParentFolder} downloaded.", fileName, parentFolderName);
                            JObject jsonObject = new JObject();
                            jsonObject.Add("fileName", fileName);
                            jsonObject.Add("FilePath", fpath);
                            jsonObject.Add("folderName", parentFolderName);
                            jsonObject.Add("fieldInvestigatorID", fieldInvestigatorID);
                            lock (uplodedDocument)
                            {
                                uplodedDocument.Add(jsonObject);
                            }
                        }
                    });
                }
            }
            _logger.Info("Folder Path Not Found.");
            Output = uplodedDocument;
            return new ValueTask<IActivityExecutionResult>(Done());
        }
        catch (Exception error)
        {
            _logger.Error(error, "Error Status Code: {StatusCode}", 500);
            Output = uplodedDocument;
            return new ValueTask<IActivityExecutionResult>(Done());
        }
    }
}
