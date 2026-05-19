namespace MozartWorkflows.Notifications.Interfaces
{
    public interface INotificationUserService
    {
        Task<string> GetEmailForUser(string userId);
    }
}
