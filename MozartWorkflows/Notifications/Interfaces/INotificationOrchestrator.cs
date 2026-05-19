namespace MozartWorkflows.Notifications.Interfaces
{
    public interface INotificationOrchestrator
    {
        Task SendNotificationAsync(string userId, string userEmail, string messageTemplate, IDictionary<string, string> variables, string mode, string subjectTemplate);
    }
}
