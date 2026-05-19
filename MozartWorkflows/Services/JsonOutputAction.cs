using RulesEngine.Actions;
using RulesEngine.ExpressionBuilders;
using RulesEngine.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MozartWorkflows.Services
{
    public class JsonOutputAction : ActionBase
    {
        private readonly RuleExpressionParser _parser;

        public JsonOutputAction(RuleExpressionParser parser)
        {
            _parser = parser;
        }

        public override ValueTask<object> Run(ActionContext context, RuleParameter[] ruleParameters)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");

            var element = context.GetContext<JsonElement>("Value");

            if (element.ValueKind != JsonValueKind.Object)
            {
                sw.Stop();
                Console.WriteLine($"[JsonOutputAction] [{timestamp}] SKIPPED - Not object, Time: {sw.Elapsed.TotalMilliseconds:F4} ms");
                return new ValueTask<object>(null!);
            }

            var result = new Dictionary<string, object>();

            foreach (var prop in element.EnumerateObject())
            {
                if (!prop.Value.TryGetProperty("Expression", out var exprNode))
                    continue;

                var expr = exprNode.GetString();
                if (expr == null)
                    continue;

                object value = EvaluateFast(expr, ruleParameters);
                result[prop.Name] = value;
            }

            sw.Stop();

            return new ValueTask<object>((object)result);
        }

        // --------------------------------------------------------------------
        // FAST EXPRESSION EVALUATOR
        // --------------------------------------------------------------------
        private object EvaluateFast(string expr, RuleParameter[] parameters)
        {
            // PATTERN: CacheService.GetAny(xxxKey)
            if (expr.StartsWith("CacheService.GetAny("))
            {
                string keyName = ExtractVariable(expr);
                string keyValue = GetParamValue(parameters, keyName);

                return CacheService.GetAny(keyValue)!;
            }

            // PATTERN: CacheService.GetDecimal(xxxKey)
            if (expr.StartsWith("CacheService.GetDecimal("))
            {
                string keyName = ExtractVariable(expr);
                string keyValue = GetParamValue(parameters, keyName);

                return CacheService.GetDecimal(keyValue);
            }

            // FALLBACK (rare case only)
            return _parser.Evaluate<object>(expr, parameters);
        }

        // Extract variable name between parentheses in expressions like:
        // CacheService.GetAny(flagsKey)
        private static string ExtractVariable(string expr)
        {
            int start = expr.IndexOf('(') + 1;
            int end = expr.IndexOf(')');
            return expr.Substring(start, end - start).Trim();
        }

        // Get actual value of parameter (dynamic key like "ABCD_FLAGS_123")
        private static string GetParamValue(RuleParameter[] parameters, string name)
        {
            return parameters.FirstOrDefault(p => p.Name == name)?.Value?.ToString() ?? string.Empty;
        }
    }
}
