namespace MozartWorkflows.Models
{
    public class RuleExecutionAudit
    {
        public int RuleAuditId { get; set; }
        public string WorkflowAuditId { get; set; } = string.Empty;
        public string? WorkflowName { get; set; }
        public string? RuleName { get; set; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime EndTime { get; set; } = DateTime.UtcNow;
        public bool IsSuccess { get; set; } = true;
        public string? ExceptionMessage { get; set; }
        public string? GlobalVariableValue { get; set; }
        public long SequenceNo { get; set; }

        public double DurationMs =>
            (EndTime - StartTime).TotalMilliseconds;
    }

}
