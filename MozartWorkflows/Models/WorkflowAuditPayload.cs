namespace MozartWorkflows.Models
{
    public class WorkflowAuditPayload
    {

        public WorkflowExecutionAudit Audit { get; set; } = null!;
        public bool IsUpdate { get; set; }
    }
}
