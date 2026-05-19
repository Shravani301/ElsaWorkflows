using System.Threading.Channels;

namespace MozartWorkflows.Models
{
    public static class WorkflowAuditQueue
    {
        public static readonly Channel<WorkflowAuditPayload> Queue =
            Channel.CreateUnbounded<WorkflowAuditPayload>(
                new UnboundedChannelOptions
                {
                    SingleReader = false,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
    }
}
