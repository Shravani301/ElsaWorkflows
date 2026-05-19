namespace MozartWorkflows.Services.Interfaces
{
    public interface IRuleDataService
    {
        Task<(RulesEngine.RulesEngine Engine, int RuleSetJsonId)> GetRulesEngineAsync(int applicationId, string workflowName);
        Task<IReadOnlyDictionary<string, object?>> GetFlattenedCaseDataAsync(string caseId);

        // OLD per-ruleset (keep if you still need it anywhere)
        Task<IReadOnlyDictionary<string, object?>> GetDefaultParamsAsync(int ruleSetJsonId);

        // NEW: global defaults
        Task<IReadOnlyDictionary<string, object?>> GetDefaultParamsAsync();

        void InvalidateCase(string caseId);
        void InvalidateWorkflow(string workflowName);
        void InvalidateRuleSet(int ruleSetJsonId);
        void InvalidateGlobalDefaults();
    }
}