using MozartWorkflows.Notifications.Interfaces;

namespace MozartWorkflows.Notifications.Utilities
{
    public class PendingNotificationFlusher
    {
        private readonly INotificationRepository _repo;
        private readonly ISignalRService _signalR;

        public PendingNotificationFlusher(INotificationRepository repo, ISignalRService signalR)
        {
            _repo = repo;
            _signalR = signalR;
        }

        public async Task FlushAsync(string userId)
        {
            var pending = await _repo.GetUnsentPushNotificationsAsync(userId);

            foreach (var notification in pending)
            {
                try
                {
                    await _signalR.SendToUserAsync(userId, notification.Message ?? string.Empty);
                    await _repo.MarkAsSentAsync(notification.Id);
                }
                catch
                {
                    // Optionally log exception
                }
            }
        }
    }

}
