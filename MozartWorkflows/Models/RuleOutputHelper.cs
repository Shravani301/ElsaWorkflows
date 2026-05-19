using Newtonsoft.Json;
using System.Globalization;
using System.Reflection;

namespace MozartWorkflows.Models
{
    public static class RuleOutputHelper
    {
        private const string RiskKey = "riskScore";
        private const string DiscountKey = "discount";
        private const string LoadFactorKey = "loadFactor";

        public static IEnumerable<object?> GetOutputs(object ruleResult)
        {
            var actionsResultsProp = ruleResult.GetType().GetProperty("ActionsResults");
            if (actionsResultsProp?.GetValue(ruleResult) is System.Collections.IEnumerable many)
            {
                foreach (var ar in many)
                {
                    var output = ar?.GetType().GetProperty("Output")?.GetValue(ar)
                              ?? ar?.GetType().GetProperty("Result")?.GetValue(ar);
                    if (output != null) yield return output;
                }
                yield break;
            }

            var actionResultProp = ruleResult.GetType().GetProperty("ActionResult");
            var singleAr = actionResultProp?.GetValue(ruleResult);
            var singleOutput = singleAr?.GetType().GetProperty("Output")?.GetValue(singleAr)
                            ?? singleAr?.GetType().GetProperty("Result")?.GetValue(singleAr);
            if (singleOutput != null) yield return singleOutput;
        }

        public static (decimal risk, decimal discount, decimal load) ExtractParts(object? output)
        {
            if (output is null)
                return (0M, 0M, 0M);

            if (output is IDictionary<string, object?> dict)
            {
                return (
                    GetDecimal(dict, RiskKey),
                    GetDecimal(dict, DiscountKey),
                    GetDecimal(dict, LoadFactorKey)
                );
            }

            var t = output.GetType();
            if (t.IsClass && t != typeof(string))
            {
                return (
                    ToDecimal(t.GetProperty(RiskKey, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(output)),
                    ToDecimal(t.GetProperty(DiscountKey, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(output)),
                    ToDecimal(t.GetProperty(LoadFactorKey, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(output))
                );
            }

            return (0M, 0M, 0M);
        }

        private static decimal GetDecimal(IDictionary<string, object?> dict, string key)
            => dict.TryGetValue(key, out var v) ? ToDecimal(v) : 0M;

        private static decimal ToDecimal(object? v)
        {
            if (v is null) return 0M;
            return v switch
            {
                decimal d => d,
                double d => Convert.ToDecimal(d, CultureInfo.InvariantCulture),
                float f => Convert.ToDecimal(f, CultureInfo.InvariantCulture),
                int i => i,
                long l => l,
                string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) => dec,
                _ => 0M
            };
        }
    }
}