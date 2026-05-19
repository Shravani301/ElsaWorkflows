using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;

namespace MozartWorkflows.Elsa.Activities;

[Action(
    Category = "MyActivity",
    Description = "GetFile Details."
)]
public class GetFile : Activity
{
    [ActivityInput(
            Label = "FilePath",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
    public string FilePath { get; set; } = default!;
    [ActivityOutput] public string Output { get; set; } = default!;
    protected override ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
    {
        if(FilePath!= null)
        {
           byte[] file = System.IO.File.ReadAllBytes(FilePath);
           string base64String = Convert.ToBase64String(file);
           Output = base64String;
        }
        return new ValueTask<IActivityExecutionResult>(Done());
    }
}
