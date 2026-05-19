using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services.Models;
using Elsa.Services;
using MozartWorkflows.Notifications.Interfaces;

namespace MozartWorkflows.Elsa.Activities
{
    public class SendNotificationActivity : Activity
    {
        private readonly INotificationOrchestrator _orchestrator;

        public SendNotificationActivity(INotificationOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        [ActivityInput(
            Label = "User ID",
            Hint = "User ID",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string? UserId { get; set; }

        [ActivityInput(
            Label = "User Email",
            Hint = "User Email",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string? UserEmail { get; set; }

        [ActivityInput(
            Label = "Message Template",
            Hint = "Message Template",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string? Template { get; set; }

        [ActivityInput(
            Label = "Subject Template",
            Hint = "Subject Template",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string? Subject { get; set; }

        [ActivityInput(
            Label = "Template Variables",
            Hint = "Template Variables",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid, SyntaxNames.Json }
        )]
        public IDictionary<string, string>? Variables { get; set; }

        [ActivityInput(
            Label = "Notification Mode",
            Hint = "Notification Mode",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid, SyntaxNames.Json }
        )]
        public string? NotificationMode { get; set; }

        protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
        {
            await _orchestrator.SendNotificationAsync(
                UserId ?? string.Empty, UserEmail ?? string.Empty,
                Template ?? string.Empty, Variables ?? new Dictionary<string, string>(),
                NotificationMode ?? string.Empty, Subject ?? string.Empty);
            return Done();
        }
    }
}
