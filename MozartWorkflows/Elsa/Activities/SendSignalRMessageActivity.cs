using System.Threading.Tasks;
using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using MozartWorkflows.Notifications.Interfaces;

namespace MozartWorkflows.Elsa.Activities
{
    [Action(
        Category = "Notifications",
        DisplayName = "Send SignalR Message",
        Description = "Sends a real-time SignalR message to a specific user or broadcasts to all connected clients."
    )]
    public class SendSignalRMessageActivity : Activity
    {
        private readonly ISignalRService _signalRService;

        public SendSignalRMessageActivity(ISignalRService signalRService)
        {
            _signalRService = signalRService;
        }

        [ActivityInput(
            Label = "User Email/ID",
            Hint = "The specific user to send the message to. Leave blank to broadcast to all clients.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string UserId { get; set; } = null!;

        [ActivityInput(
            Label = "Event Name",
            Hint = "The name of the event to trigger on the client (e.g., 'DashboardUpdate').",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string EventName { get; set; } = null!;

        [ActivityInput(
            Label = "Message Payload",
            Hint = "The JSON data or text message to send to the client.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid, SyntaxNames.Json }
        )]
        public string? MessagePayload { get; set; }

        protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(EventName))
            {
                return Fault("EventName is required to send a SignalR message.");
            }

            if (string.IsNullOrWhiteSpace(UserId))
            {
                
                await _signalRService.BroadcastMessageAsync(EventName, MessagePayload ?? "");
            }
            else
            {
                
                await _signalRService.SendEventToUserAsync(UserId, EventName, MessagePayload ?? "");
            }

            return Done();
        }
    }
}
