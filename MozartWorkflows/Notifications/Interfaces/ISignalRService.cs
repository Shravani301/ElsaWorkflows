namespace MozartWorkflows.Notifications.Interfaces
{
    public interface ISignalRService
    {
        bool IsUserOnline(string userId);
        Task SendToUserAsync(string userId, string message);
        Task SendEventToUserAsync(string userId, string eventName, string message);
        Task BroadcastMessageAsync(string eventName, string message);
    }
}
