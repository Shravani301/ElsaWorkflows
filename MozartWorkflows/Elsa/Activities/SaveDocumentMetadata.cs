using Elsa.Services;
using Elsa.Services.Models;
using Elsa.Attributes;
using Elsa.ActivityResults;
using Dapper;
using System.Data;
using MozartWorkflows.Services.Interfaces;

namespace MozartWorkflows.Elsa.Activities
{
    [Activity(
        Category = "File Management",
        Description = "Saves document metadata to the database."
    )]
    public class SaveDocumentMetadata : Activity
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public SaveDocumentMetadata(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
        {
            var claimIntimationNo = context.GetVariable<int>("claimIntimationNumber");
            var panNumber = context.GetVariable<string>("panCard");

            var uploadedFiles = context.GetVariable<List<UploadedFileResult>>("uploadedFiles");

            if (uploadedFiles == null || uploadedFiles.Count == 0)
            {
                Console.WriteLine("No files found to save in the database.");
                return Done();
            }

            using var connection = _dbConnectionFactory.CreateConnection();
            connection.Open();

            // Use a portable timestamp function (CURRENT_TIMESTAMP works on most databases)
            string insertQuery = @"
                INSERT INTO UploadClaimDocuments 
                (LabelName, DocumentName, DocumentType, DocumentLink, PanNumber, CreatedById, CreatedOn, ClaimIntimationNo) 
                VALUES 
                (@LabelName, @DocumentName, @FileType, @DocumentLink, @PanNumber, 1, CURRENT_TIMESTAMP, @ClaimIntimationNo)";

            foreach (var file in uploadedFiles)
            {
                await connection.ExecuteAsync(insertQuery, new
                {
                    LabelName = file.FileLabel,
                    DocumentName = file.FileName,
                    FileType = file.FileType,
                    DocumentLink = file.FileUrl,
                    PanNumber = panNumber,
                    ClaimIntimationNo = claimIntimationNo,
                });

                Console.WriteLine($"✅ Saved {file.FileName} to the database.");
            }

            return Done();
        }
    }
}
