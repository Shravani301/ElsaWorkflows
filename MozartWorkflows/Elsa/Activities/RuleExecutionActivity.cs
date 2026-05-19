using Elsa;
using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using Jint.Native;
using Microsoft.Extensions.Logging;
using MozartWorkflows.Services;
using MozartWorkflows.Services.Interfaces;
using Newtonsoft.Json;
using System.Dynamic;

namespace MozartWorkflows.Elsa.Activities
{
    [Action(
        Category = "RulesEngineActivity",
        Description = "Executes rule-based workflow logic using RulesEngine and returns the JSON result."
    )]
    public sealed class RuleExecutionActivity : Activity
    {
        private readonly IRuleService _ruleService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RuleExecutionActivity> _logger;

        public RuleExecutionActivity(
            IRuleService ruleService,
            IConfiguration configuration,
            ILogger<RuleExecutionActivity> logger)
        {
            _ruleService = ruleService;
            _configuration = configuration;
            _logger = logger;
        }

        // ---- Inputs ----
        [ActivityInput(Label = "Workflow Name", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid })]
        public string WorkflowName { get; set; } = default!;

        [ActivityInput(Label = "Input JSON", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid })]
        public ExpandoObject InputJson { get; set; } = new();

        // ---- Output ----
        [ActivityOutput]
        public string WorkflowResultJson { get; private set; } = default!;

        protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
        {
            _logger.LogInformation("Starting RuleExecutionActivity for workflow: {WorkflowName}", WorkflowName);

            try
            {
                // Validate Inputs (Same as Controller)
                if (string.IsNullOrWhiteSpace(WorkflowName) || InputJson == null)
                {
                    string error = "WorkflowName and InputJson are required.";
                    _logger.LogWarning("{Error}", error);

                    context.SetVariable("WorkflowResultJson", error);
                    return Outcomes("Faulted");
                }

                bool testMood = _configuration.GetValue<bool>("testMood");

                if (testMood)
                {
                    _logger.LogInformation("TEST MODE ON → Updating Rules into Rule Engine...");
                    await _ruleService.UpdateRulesInRuleEngine();
                }

                // Convert InputJson to clean JSON
                string sanitizedJson = SanitizeJson(InputJson);

                // ---- Async Execution (matches Controller) ----
                string resultJson = await WorkflowExecutionService.ExecuteWorkflowAsync(WorkflowName, sanitizedJson);

                WorkflowResultJson = resultJson;

                context.SetVariable("WorkflowResultJson", WorkflowResultJson);

                _logger.LogInformation("Workflow executed successfully: {WorkflowName}", WorkflowName);

                return Outcomes("Done");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing workflow {WorkflowName}", WorkflowName);

                context.SetVariable("WorkflowResultJson", ex.Message);
                return Outcomes("Faulted");
            }
        }

        private static string SanitizeJson(ExpandoObject inputJson)
        {
            var jsonString = JsonConvert.SerializeObject(inputJson);
            return jsonString.Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
