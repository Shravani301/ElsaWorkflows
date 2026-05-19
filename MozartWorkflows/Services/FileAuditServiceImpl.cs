using MozartWorkflows.Models;
using MozartWorkflows.Services.Interfaces;

namespace MozartWorkflows.Services
{
    public class FileAuditServiceImpl : IAuditService
    {
        private readonly ILogger<FileAuditServiceImpl> _logger;

        public FileAuditServiceImpl(ILogger<FileAuditServiceImpl> logger)
        {
            _logger = logger;
        }

        public Task BulkInsertWorkflowAuditsAsync(IEnumerable<WorkflowExecutionAudit> audits)
        {
            foreach (var audit in audits)
            {
                _logger.LogInformation("WorkflowAudit Inserted: {@Audit}", audit);
            }

            return Task.CompletedTask;
        }

        public Task BulkUpdateWorkflowAuditsAsync(IEnumerable<WorkflowExecutionAudit> audits)
        {
            foreach (var audit in audits)
            {
                _logger.LogInformation("WorkflowAudit Updated: {@Audit}", audit);
            }

            return Task.CompletedTask;
        }

        public Task BulkInsertRuleAuditsAsync(IEnumerable<RuleExecutionAudit> audits)
        {
            foreach (var audit in audits)
            {
                if (!audit.IsSuccess)
                    _logger.LogError("RuleError: {@Audit}", audit);
                else
                    _logger.LogInformation("RuleAudit: {@Audit}", audit);
            }

            return Task.CompletedTask;
        }
    }
}
