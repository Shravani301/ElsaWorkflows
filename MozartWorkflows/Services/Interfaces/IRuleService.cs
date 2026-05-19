using MozartWorkflows.Dtos;

namespace MozartWorkflows.Services.Interfaces
{
    public interface IRuleService
    {
        Task<(IEnumerable<RuleDtoForController> Data, int TotalCount, int TotalPages)> GetAllRulesAsync(int page, int size);
        Task<RuleDtoForController?> GetRuleByIdAsync(int id);
        Task<RuleDtoForController?> GetRuleByCodeAsync(string ruleCode);
        Task<RuleDtoForController> AddRuleAsync(RuleDtoForController rule);
        Task<RuleDtoForController> UpdateRuleAsync(RuleDtoForController rule);
        Task DeleteRuleAsync(int id);
        Task UpdateRulesInRuleEngine();
        Task<IEnumerable<string>> GetAllWorkflowNamesAsync();

    }
}
