using System.Text.Json;
using Elsa.Events;
using MediatR;
using MozartWorkflows.Notifications.Interfaces;

namespace MozartWorkflows.Elsa.NotificationHandlers
{
    public class ActivityNotificationHandler : INotificationHandler<ActivityExecuted>
    {
        private readonly ISignalRService _signalRService;
        public ActivityNotificationHandler(ISignalRService signalRService)
        {
            _signalRService = signalRService;
        }
        public async Task Handle(ActivityExecuted notification, CancellationToken cancellationToken)
        {
            var context = notification.ActivityExecutionContext;
            var workflowBlueprint = context.WorkflowExecutionContext.WorkflowBlueprint;
            var activityBlueprint = context.ActivityBlueprint;
            var payload = new
            {
                WorkflowInstanceId = context.WorkflowInstance.Id,
                WorkflowName = workflowBlueprint.Name ?? "Unknown Workflow",
                ActivityId = context.ActivityId,
                ActivityName = activityBlueprint.Name ?? activityBlueprint.Type,
                ActivityType = activityBlueprint.Type,
                Status = "Executed",
                Timestamp = DateTime.UtcNow
            };
            var jsonPayload = JsonSerializer.Serialize(payload);
            await _signalRService.BroadcastMessageAsync("WorkflowActivityTracked", jsonPayload);
        }
    }
}
