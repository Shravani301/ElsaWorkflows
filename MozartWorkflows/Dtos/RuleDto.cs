namespace MozartWorkflows.Dtos
{
    public class RuleDto
    {
        public int Id { get; set; }
        public string? WorkflowJson { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int TotalCount { get; set; }
    }
}
