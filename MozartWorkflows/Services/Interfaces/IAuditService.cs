using MozartWorkflows.Models;

namespace MozartWorkflows.Services.Interfaces
{
    public interface IAuditService
    {


        public Task BulkInsertWorkflowAuditsAsync(IEnumerable<WorkflowExecutionAudit> audits);

        public Task BulkUpdateWorkflowAuditsAsync(IEnumerable<WorkflowExecutionAudit> audits);

        public Task BulkInsertRuleAuditsAsync(IEnumerable<RuleExecutionAudit> audits);


    }
}
