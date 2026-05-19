using Elsa;

using Elsa.ActivityResults;

using Elsa.Attributes;

using Elsa.Design;

using Elsa.Expressions;

using Elsa.Providers.WorkflowStorage;

using Elsa.Services;

using Elsa.Services.Models;

using MySqlConnector;

using System.Threading.Tasks;

namespace MozartWorkflows.Elsa.Activities

{

    [Activity(

        Category = "SQL",

        DisplayName = "Execute MySQL Command",

        Description = "Executes a non-query SQL command (INSERT, UPDATE, DELETE) using MySQL.",

        Outcomes = new[] { OutcomeNames.Done }

    )]

    public class ExecuteMySqlCommand : Activity

    {

        [ActivityInput(

            Hint = "SQL command to execute (INSERT, UPDATE, DELETE).",

            UIHint = ActivityInputUIHints.MultiLine,

            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }

        )]

        public string Command { get; set; } = default!;

        [ActivityInput(

            Hint = "The MySQL connection string. You can use getConfig('ConnectionStrings:...').",

            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Literal }

        )]

        public string? ConnectionString { get; set; }

        [ActivityOutput(

            Hint = "Number of rows affected by the SQL command.",

            DefaultWorkflowStorageProvider = TransientWorkflowStorageProvider.ProviderName

        )]

        public int? Output { get; set; }

        protected override IActivityExecutionResult OnExecute(ActivityExecutionContext context)

        {

            using var connection = new MySqlConnection(ConnectionString);

            connection.Open();

            using var command = new MySqlCommand(Command, connection);

            Output = command.ExecuteNonQuery();

            return Done();

        }

    }

}

