using MozartWorkflows.Models;
using MozartWorkflows.Services.Interfaces;
using RulesEngine.Actions;
using RulesEngine.ExpressionBuilders;
using RulesEngine.Models;
using System.Diagnostics;
using System.Text.Json;

namespace MozartWorkflows.Services
{
    public class AuditLogAction : ActionBase
    {
        public override ValueTask<object> Run(ActionContext context, RuleParameter[] ruleParameters)
        {

            RuleExecutionAudit ruleExecutionAudit = BuildAudit(new AuditPayload(context, ruleParameters));

            AuditQueue.TryQueueAudit(ruleExecutionAudit);

            return ValueTask.FromResult<object>(null!);
        }

        public static RuleExecutionAudit BuildAudit(AuditPayload payload)
        {
            var ctx = payload.Context;
            var parameters = payload.Parameters;

            ctx.TryGetContext("isSuccess", out bool isSuccess);
            ctx.TryGetContext("workflowName", out string workflowName);
            ctx.TryGetContext("ruleName", out string ruleName);
            ctx.TryGetContext("sequenceNo", out int sequenceNo);
            ctx.TryGetContext("errorMessage", out string errorMsg);


            var audit = new RuleExecutionAudit
            {
                WorkflowAuditId = GetWorkflowAuditId(parameters),
                WorkflowName = workflowName ?? "UnknownWorkflow",
                RuleName = ruleName ?? "UnknownRule",
                SequenceNo = sequenceNo,
                StartTime = payload.TimeStamp,
                IsSuccess = isSuccess,
                GlobalVariableValue = "0.00",
                EndTime = DateTime.UtcNow
            };

            if (isSuccess)
            {
                try
                {
                    audit.GlobalVariableValue = ResolveOutputValue(ctx, parameters);
                }
                catch (Exception ex)
                {
                    audit.GlobalVariableValue = "0.00";
                    audit.IsSuccess = false;
                    audit.ExceptionMessage = ex.Message;
                }
            }
            else
            {
                audit.ExceptionMessage = errorMsg ?? "Internal rule failure due ro invalid logic";
            }

            return audit;
        }

        private static string GetWorkflowAuditId(RuleParameter[] parameters)
        {
            return parameters
                .FirstOrDefault(p => p.Name.Equals("workflowAuditId", StringComparison.OrdinalIgnoreCase))
                ?.Value?.ToString() ?? string.Empty;
        }


        private static string ResolveOutputValue(ActionContext context, RuleParameter[] parameters)
        {
            if (!context.TryGetContext("output", out string outputName) || string.IsNullOrWhiteSpace(outputName))
                return "0.00";

            var param = parameters.FirstOrDefault(p =>
                p.Name.Equals(outputName.Trim(), StringComparison.OrdinalIgnoreCase));

            return param != null
                ? (ConvertToDecimal(param.Value) ?? 0m).ToString("F2")
                : "0.00";
        }


        private static decimal? ConvertToDecimal(object? obj)
        {
            if (obj is null)
                return null;

            return obj switch
            {
                decimal d => d,
                double db => (decimal)db,
                float f => (decimal)f,
                int i => i,
                long l => l,
                JsonElement je when je.ValueKind == JsonValueKind.Number =>
                    je.TryGetDecimal(out var dec) ? dec : null,
                JsonElement je when je.ValueKind == JsonValueKind.String =>
                    decimal.TryParse(je.GetString(), out var dec) ? dec : null,
                _ => decimal.TryParse(obj.ToString(), out var dec) ? dec : null
            };
        }
    }
}
