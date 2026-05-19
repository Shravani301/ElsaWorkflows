namespace MozartWorkflows.Notifications.Interfaces
{
    public interface IPushNotificationService
    {
        Task SendAsync(string userId, string message);
        Task StoreForLaterAsync(string userId, string message);
    }
}
