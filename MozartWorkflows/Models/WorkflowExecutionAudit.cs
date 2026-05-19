namespace MozartWorkflows.Models
{
    public class WorkflowExecutionAudit
    {
        public string WorkflowAuditId { get; set; } = string.Empty;
        public string WorkflowName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public bool IsSuccess { get; set; }
        public string? ExceptionMessage { get; set; }

        public double DurationMs =>
            EndTime.HasValue ? (EndTime.Value - StartTime).TotalMilliseconds : 0;
    }
}
