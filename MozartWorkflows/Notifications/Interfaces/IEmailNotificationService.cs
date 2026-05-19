namespace MozartWorkflows.Notifications.Interfaces
{
    public interface IEmailNotificationService
    {
        Task SendAsync(string userId, string userEmail, string message, string subject);
    }

}
