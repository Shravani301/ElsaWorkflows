using Microsoft.AspNetCore.SignalR;

namespace MozartWorkflows.Notifications.Hubs
{
    public class NotificationHub : Hub
    {
        public async Task RegisterUser(string userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }
    }

}
