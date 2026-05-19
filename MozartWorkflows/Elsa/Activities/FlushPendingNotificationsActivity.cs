using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using MozartWorkflows.Notifications.Utilities;

namespace MozartWorkflows.Elsa.Activities
{
    public class FlushPendingNotificationsActivity : Activity
    {
        private readonly PendingNotificationFlusher _flusher;

        public FlushPendingNotificationsActivity(PendingNotificationFlusher flusher)
        {
            _flusher = flusher;
        }

        [ActivityInput(
            Label = "User ID",
            Hint = "User ID",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string? UserId { get; set; }

        protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
        {
            await _flusher.FlushAsync(UserId ?? string.Empty);
            return Done();
        }
    }

}
