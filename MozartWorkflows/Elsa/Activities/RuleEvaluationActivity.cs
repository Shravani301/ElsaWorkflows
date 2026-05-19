using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;
using Elsa;
using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using Microsoft.Extensions.Logging;
using MozartWorkflows.Models;
using MozartWorkflows.Services.Interfaces;
using Newtonsoft.Json;

namespace MozartWorkflows.Elsa.Activities
{

[Action(Category = "RulesEngineActivity", Description = "Evaluates business rules using cached form data and global defaults.")]
public sealed class RuleEvaluationActivity(IRuleDataService data, ILogger<RuleEvaluationActivity> logger) : Activity
{
    private readonly IRuleDataService _data = data;
    private readonly ILogger<RuleEvaluationActivity> _logger = logger;

    [ActivityInput(Label = "CaseId", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid })]
    public string CaseId { get; set; } = default!;

    [ActivityInput(Label = "ApplicationId", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid })]
    public int ApplicationId { get; set; }

    [ActivityInput(Label = "WorkflowName", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid })]
    public string WorkflowName { get; set; } = default!;

    [ActivityOutput]
    public string RuleEvaluationResult { get; private set; } = default!;

#pragma warning disable S3776
    protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
    {
        try
        {
            // 1) Engine (cached in service) + owning RuleSetJsonId (used only for fallback defaults).
            var (engine, ruleSetJsonId) = await _data.GetRulesEngineAsync(ApplicationId, WorkflowName);

            // 2) Kick both reads: form data + GLOBAL defaults (fast path).
            //    NOTE: Global defaults call is argument-less per your new interface.
            var formTask = _data.GetFlattenedCaseDataAsync(CaseId);
            var globalsTask = _data.GetDefaultParamsAsync(); // global defaults

            var flattenedCaseData = await formTask;
            if (flattenedCaseData.Count == 0)
                throw new InvalidOperationException($"Flattened CaseData is empty for CaseId {CaseId}.");

            var defaultValues = await globalsTask;

            // 3) Fallback to per-ruleset defaults only if global defaults are empty.
            if (defaultValues.Count == 0)
            {
                _logger.LogDebug("Global defaults are empty; falling back to per-ruleSet defaults. RuleSetJsonId={RuleSetJsonId}", ruleSetJsonId);
                defaultValues = await _data.GetDefaultParamsAsync(ruleSetJsonId);
            }

            // 4) Prepare globals for RulesEngine (Expando keeps dynamic access lightweight).
            var globals = new ExpandoObject() as IDictionary<string, object?>;
            globals["formParams"] = flattenedCaseData.ToExpando();
            globals["defaultParams"] = defaultValues.ToExpando();

            _logger.LogDebug("CaseId={CaseId}: formParams={FormCount}, defaultParams={DefCount}", CaseId, flattenedCaseData.Count, defaultValues.Count);
            _logger.LogInformation("Default Values:{DefaultParams}", defaultValues);
            _logger.LogInformation("Flattened CaseData:{FormParams}", flattenedCaseData);

            // 5) Execute rules for this workflow.
            var results = await engine.ExecuteAllRulesAsync(WorkflowName, globals);

            var passedRuleNames = new List<string>();
            var passedEvents = new List<string>();
            var ruleSummaries = new List<object>();

            decimal totalRiskScore = 0M;
            decimal totalDiscount = 0M;
            decimal totalLoadFactor = 1M;

            foreach (var r in results)
            {
                if (r.IsSuccess)
                {
                    passedRuleNames.Add(r.Rule.RuleName);
                    if (!string.IsNullOrWhiteSpace(r.Rule.SuccessEvent))
                        passedEvents.Add(r.Rule.SuccessEvent);
                }

                // Pull outputs (if any) with helper; take first for summary text.
                var outputs = RuleOutputHelper.GetOutputs(r);
                var summaryOutput = outputs.FirstOrDefault();

                ruleSummaries.Add(new
                {
                    r.Rule.RuleName,
                    Expression = r.Rule.Expression ?? "[Composite Rule]",
                    r.IsSuccess,
                    SuccessEvent = r.IsSuccess ? r.Rule.SuccessEvent : r.Rule.ErrorMessage,
                    Output = summaryOutput != null
                        ? JsonConvert.SerializeObject(summaryOutput, Formatting.None)
                        : (r.ActionResult?.Exception?.Message ?? r.ExceptionMessage ?? "No output")
                });

                if (!r.IsSuccess) continue;

                // Aggregate numerical parts from each output (risk, discount, load).
                foreach (var output in outputs)
                {
                    var (riskInc, discInc, loadInc) = RuleOutputHelper.ExtractParts(output);
                    totalRiskScore += riskInc;
                    totalDiscount += discInc;
                    totalLoadFactor *= (loadInc == 0M ? 1M : loadInc);
                }


            }

            // 6) Final result object (compact, deterministic).
            var evaluationResult = new
            {
                Status = passedRuleNames.Count > 0
                    ? $"Rules Passed: {string.Join(", ", passedRuleNames)}"
                    : "No rules passed.",
                Flags = passedEvents,
                AggregatedResults = new
                {
                    TotalRiskScore = totalRiskScore,
                    TotalDiscount = totalDiscount,
                    TotalLoadFactor = totalLoadFactor
                },
                RuleSummary = ruleSummaries
            };

            RuleEvaluationResult = JsonConvert.SerializeObject(evaluationResult, Formatting.Indented);
            context.SetVariable("RuleEvaluationResult", RuleEvaluationResult);

            return Outcomes("Done");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating rules. CaseId={CaseId}, Workflow={WorkflowName}", CaseId, WorkflowName);
            return Outcomes("Faulted");
        }
    }
#pragma warning restore S3776
}

internal static class ExpandoExtensions
{
    public static IDictionary<string, object?> ToExpando(this IReadOnlyDictionary<string, object?> dict)
    {
        var expando = new ExpandoObject() as IDictionary<string, object?>;
        foreach (var kv in dict)
            expando[kv.Key] = kv.Value;
        return expando;
    }
}

} // namespace MozartWorkflows.Elsa.Activities
