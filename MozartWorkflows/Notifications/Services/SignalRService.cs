using Microsoft.AspNetCore.SignalR;
using MozartWorkflows.Notifications.Hubs;
using MozartWorkflows.Notifications.Interfaces;

namespace MozartWorkflows.Notifications.Services
{
    public class SignalRService : ISignalRService
    {
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IOnlineUserTracker _tracker;

        public SignalRService(IHubContext<NotificationHub> hubContext, IOnlineUserTracker tracker)
        {
            _hubContext = hubContext;
            _tracker = tracker;
        }

        public bool IsUserOnline(string userId)
        {
            return _tracker.IsOnline(userId);
        }

        public async Task SendToUserAsync(string userId, string message)
        {
            await _hubContext.Clients.Group(userId).SendAsync("ReceiveNotification", message);
        }

        public async Task SendEventToUserAsync(string userId, string eventName, string message)
        {
            await _hubContext.Clients.Group(userId).SendAsync(eventName, message);
        }

        public async Task BroadcastMessageAsync(string eventName, string message)
        {
            await _hubContext.Clients.All.SendAsync(eventName, message);
        }
    }

}
