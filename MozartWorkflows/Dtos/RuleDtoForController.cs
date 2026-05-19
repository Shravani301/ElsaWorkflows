using RulesEngine.Models;
namespace MozartWorkflows.Dtos
{
    public class RuleDtoForController
    {
        public int Id { get; set; }
        public Workflow? WorkflowJson { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}