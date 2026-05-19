namespace MozartWorkflows.Services.Interfaces
{
    public interface IRulesRepository
    {
        Task<(string rulesJson, int ruleSetJsonId)?> GetRulesJsonAsync(int applicationId, string workflowName);
        Task<string?> GetCaseDataJsonAsync(string caseId);
        Task<string?> GetDefaultValuesAsync(int ruleSetJsonId);  // Added method for default values
        Task<string?> GetAllDefaultValuesAsync();
    }
}
