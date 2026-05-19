using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using Newtonsoft.Json.Linq;
using System.Data;

namespace MozartWorkflows.Elsa.Activities;

[Action(
   Category = "MyActivity",
   Description = "Creating for Converting Dataset to String."
)]
public class ConvertDataset : Activity
{
    [ActivityInput(Hint = "The name of the variable to store the value into.")]
    public string VariableName { get; set; } = default!;

    [ActivityInput(
        Label = "DataSet",
        Hint = "The Dataset Value for Conversion",
        SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
    )]
    public new DataSet? Data { get; set; } = default!;

    protected override ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
    {
        if (Data == null) return new ValueTask<IActivityExecutionResult>(Done());
        var dataTable = Data.Tables[0];
        var jsonArray = new List<JObject>();
        foreach (DataRow row in dataTable.Rows)
        {
            var jsonObject = new JObject();
            foreach (DataColumn column in dataTable.Columns)
            {
                jsonObject[column.ColumnName] = JToken.FromObject(row[column]);
            }
            jsonArray.Add(jsonObject);
        }
        context.SetVariable(VariableName, jsonArray);
        return new ValueTask<IActivityExecutionResult>(Done());
    }
}
