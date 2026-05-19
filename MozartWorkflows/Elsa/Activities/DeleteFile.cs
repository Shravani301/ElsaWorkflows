using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using MozartWorkflows.Models;
using Elsa.Services;
using Elsa.Services.Models;
using Newtonsoft.Json;
using NLog;
using ILogger = NLog.ILogger;

namespace MozartWorkflows.Elsa.Activities
{
    [Action(
    Category = "MyActivity",
    Description = "Delete File."
    )]
    public class DeleteFile : Activity
    {
        private readonly ILogger _logger;
        public DeleteFile()
        {
            _logger = LogManager.GetCurrentClassLogger();
        }
        [ActivityInput(
            Label = "Root FolderPath",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string Folder_Path { get; set; } = default!;
        [ActivityInput(
            Label = "DocumentType",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string DocumentType { get; set; } = default!;
        [ActivityInput(
            Label = "FileName",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string FileName { get; set; } = default!;

        [ActivityOutput] public string Output { get; set; } = default!;

        protected override ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
        {
            var resVal = new DeleteFileResponse { Success = false, StatusCode = 400, Message = "File Not Found." };
            try
            {
                string[] folders = Directory.GetDirectories(Folder_Path);
                int deletedFileCount = folders
                    .Where(folder => !string.IsNullOrEmpty(folder))
                    .SelectMany(folder => Directory.GetDirectories(folder))
                    .Where(subfolder =>
                    {
                        string subfolderName = Path.GetFileName(subfolder);
                        return subfolderName == DocumentType;
                    })
                    .SelectMany(subfolder => Directory.GetFiles(subfolder, FileName))
                    .Where(fileToDelete => !fileToDelete.StartsWith("Delete_"))
                    .Select(fileToDelete =>
                    {
                        string newFileName = "Delete_" + Path.GetFileName(fileToDelete);
                        string newPath = Path.Combine(Path.GetDirectoryName(fileToDelete) ?? string.Empty, newFileName);
                        File.Move(fileToDelete, newPath);
                        _logger.Info("{FilePath} file marked as deleted.", fileToDelete);
                        return fileToDelete;
                    })
                    .Count();

                if (deletedFileCount > 0)
                {
                    resVal.Success = true;
                    resVal.StatusCode = 200;
                    resVal.Message = "Files deleted successfully.";
                }
                Output = JsonConvert.SerializeObject(resVal);
                return new ValueTask<IActivityExecutionResult>(Done());
            }
            catch (Exception error)
            {
                _logger.Error(error, "Error Status Code: {StatusCode}", 500);
                Output = JsonConvert.SerializeObject(resVal);
                return new ValueTask<IActivityExecutionResult>(Done());
            }
        }
    }
}
