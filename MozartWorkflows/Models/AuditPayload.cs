using RulesEngine.Models;
using RulesEngine.Actions;

namespace MozartWorkflows.Models
{
    public class AuditPayload
    {
        public ActionContext Context { get; }
        public RuleParameter[] Parameters { get; }

        public DateTime TimeStamp { get; } = DateTime.UtcNow;

        public AuditPayload(ActionContext context, RuleParameter[] parameters)
        {
            Context = context;
            Parameters = parameters;
        }
    }
}
