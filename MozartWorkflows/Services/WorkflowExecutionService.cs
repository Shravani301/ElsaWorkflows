using MozartWorkflows.Dtos;
using MozartWorkflows.Models;
using MozartWorkflows.Services.Interfaces;
using RulesEngine.Actions;
using RulesEngine.ExpressionBuilders;
using RulesEngine.Models;
using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MozartWorkflows.Services
{
    public class WorkflowExecutionService
    {
        private static readonly JsonSerializerOptions JsonOutputSerializerOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        private static RulesEngine.RulesEngine _rulesEngine = null!;

        protected WorkflowExecutionService() { }

        static WorkflowExecutionService()
        {
            InitializeRuleEngine();
        }

        private static void InitializeRuleEngine()
        {
            RuleExpressionParser ruleExpressionParser = new RuleExpressionParser(
                new ReSettings
                {
                    CustomTypes = new Type[]
                    {
                        typeof(WorkflowExecutionService),
                        typeof(MozartWorkflows.Services.Utils),
                        typeof(CacheService)
                    }
                }
            );

            ReSettings reSettings = new ReSettings
            {
                IgnoreException = false,
                EnableExceptionAsErrorMessage = true,
                CustomActions = new Dictionary<string, Func<ActionBase>>
                {
                    { "JsonOutput", () => new JsonOutputAction(ruleExpressionParser) },
                    { "AuditLog", () => new AuditLogAction() }
                },
                CustomTypes = new Type[]
                {
                    typeof(WorkflowExecutionService),
                    typeof(MozartWorkflows.Services.Utils),
                    typeof(CacheService)
                }
            };

            _rulesEngine = new RulesEngine.RulesEngine(reSettings);
        }

        public static string ExecuteWorkflow(string workflowName, object inputData)
        {
            return ExecuteWorkflowAsync(workflowName, inputData).GetAwaiter().GetResult();
        }

        public static async Task<string> ExecuteWorkflowAsync(string workflowName, object inputData)
        {
            string globalVarId = CacheService.GenerateRandomKey();
            string? ruleFailureDetails = null;
            string workflowAuditId = GenerateWorkflowAuditId();
            DateTime startTime = DateTime.UtcNow;

            try
            {
                ExpandoObject workflowData = ConvertInput(inputData);

                var parameters = new[]
                {
                    new RuleParameter("workflowData", workflowData),
                    new RuleParameter("globalVariable", globalVarId),
                    new RuleParameter("workflowAuditId", workflowAuditId)
                };

                var ruleResults = await _rulesEngine.ExecuteAllRulesAsync(workflowName, parameters);

                foreach (var result in ruleResults)
                {
                    if (!result.IsSuccess || !string.IsNullOrEmpty(result.ExceptionMessage))
                    {
                        ruleFailureDetails = result.ExceptionMessage ?? "Unknown rule error";

                        if (!string.IsNullOrEmpty(result.Rule?.RuleName))
                            ruleFailureDetails = $"Rule '{result.Rule.RuleName}' failed: {ruleFailureDetails}";

                        throw new InvalidOperationException(ruleFailureDetails);
                    }
                }

                string jsonOutput = JsonSerializer.Serialize(
                    ruleResults[ruleResults.Count - 1].ActionResult.Output,
                    JsonOutputSerializerOptions);

                await WorkflowAuditQueue.Queue.Writer.WriteAsync(
                    new WorkflowAuditPayload
                    {
                        Audit = new WorkflowExecutionAudit
                        {
                            WorkflowAuditId = workflowAuditId,
                            WorkflowName = workflowName,
                            StartTime = startTime,
                            EndTime = DateTime.UtcNow,
                            IsSuccess = true,
                            ExceptionMessage = null
                        },
                        IsUpdate = false
                    }
                );

                return jsonOutput;
            }
            catch (Exception ex)
            {
                string finalError = ruleFailureDetails ?? ex.Message;

                await WorkflowAuditQueue.Queue.Writer.WriteAsync(
                    new WorkflowAuditPayload
                    {
                        Audit = new WorkflowExecutionAudit
                        {
                            WorkflowAuditId = workflowAuditId,
                            WorkflowName = workflowName,
                            StartTime = startTime,
                            EndTime = DateTime.UtcNow,
                            IsSuccess = false,
                            ExceptionMessage = finalError
                        },
                        IsUpdate = false
                    }
                );

                throw;
            }
        }

        private static ExpandoObject ConvertInput(object inputData)
        {
            if (inputData == null) return new ExpandoObject();

            try
            {
                if (inputData is JsonNode jsonNode)
                    return JsonToExpando.Convert(jsonNode);

                if (inputData is string jsonString && JsonNode.Parse(jsonString) is JsonNode node)
                    return JsonToExpando.Convert(node);

                var json = JsonSerializer.Serialize(inputData);
                var parsed = JsonNode.Parse(json);

                return JsonToExpando.Convert(parsed);
            }
            catch
            {
                return new ExpandoObject();
            }
        }

        private static string GenerateWorkflowAuditId()
        {
            return $"{Guid.NewGuid():N}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        }

        public static void updateRuleEngineWorkflows(List<RuleDtoForController> rules)
        {
            foreach (var workflowJson in rules.Select(rule => rule.WorkflowJson).Where(workflowJson => workflowJson != null))
            {
                _rulesEngine.AddOrUpdateWorkflow(workflowJson);
            }
        }
    }
}
