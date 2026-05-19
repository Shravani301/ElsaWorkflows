using Elsa;
using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Design;
using Elsa.Expressions;
using Elsa.Providers.WorkflowStorage;
using Elsa.Services;
using Elsa.Services.Models;
using MySqlConnector;
using System.Data;
using System.Threading.Tasks;

namespace MozartWorkflows.Elsa.Activities
{
    [Activity(
        Category = "SQL",
        DisplayName = "Execute MySQL Query",
        Description = "Executes a SQL SELECT query on a MySQL database and returns the result as a DataSet.",
        Outcomes = new[] { OutcomeNames.Done }
    )]
    public class ExecuteMySqlQuery : Activity
    {
        [ActivityInput(
            Hint = "SQL query to execute.",
            UIHint = ActivityInputUIHints.MultiLine,
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string Query { get; set; } = default!;

        [ActivityInput(
            Hint = "Connection string to connect to MySQL.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Literal }
        )]
        public string ConnectionString { get; set; } = default!;

        [ActivityOutput(
            Hint = "The result of the query as a DataSet.",
            DisableWorkflowProviderSelection = true,
            DefaultWorkflowStorageProvider = TransientWorkflowStorageProvider.ProviderName
        )]
        public DataSet? Output { get; set; }

        protected override IActivityExecutionResult OnExecute(ActivityExecutionContext context)
        {
            var dataSet = new DataSet();

            using var connection = new MySqlConnection(ConnectionString);
            connection.Open();

            using var command = new MySqlCommand(Query, connection);
            using var adapter = new MySqlDataAdapter(command);
            adapter.Fill(dataSet);

            Output = dataSet;

            return Done();
        }
    }
}