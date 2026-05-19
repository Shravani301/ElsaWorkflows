namespace MozartWorkflows.Notifications.Interfaces
{
    public interface IOnlineUserTracker
    {
        void MarkOnline(string userId);
        void MarkOffline(string userId);
        bool IsOnline(string userId);
    }
}
