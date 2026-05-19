using System.Text.Json.Nodes;

namespace MozartWorkflows.Models
{
    public class WorkflowInput
    {
        public string WorkflowName { get; set; } = null!;
        public JsonNode InputJson { get; set; } = null!;

        public WorkflowInput(string workflowName, JsonNode inputJson)
        {
            WorkflowName = workflowName;
            InputJson = inputJson;
        }

        public WorkflowInput() { }
    }
}
