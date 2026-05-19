using MozartWorkflows.Notifications.Models;

namespace MozartWorkflows.Notifications.Interfaces
{
    public interface INotificationRepository
    {
        Task SavePendingNotificationAsync(string userId, string message, string subject, string mode);
        Task<IEnumerable<PendingNotification>> GetUnsentPushNotificationsAsync(string userId);
        Task MarkAsSentAsync(Guid notificationId);
    }
}
