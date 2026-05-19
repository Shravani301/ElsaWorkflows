using MozartWorkflows.Models;
using System.Threading.Channels;

namespace MozartWorkflows.Services
{
    public static class AuditQueue
    {
        public static readonly Channel<RuleExecutionAudit> Queue =
    Channel.CreateUnbounded<RuleExecutionAudit>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        public static void TryQueueAudit(RuleExecutionAudit payload)
        {
            Queue.Writer.TryWrite(payload);
        }
    }
}
