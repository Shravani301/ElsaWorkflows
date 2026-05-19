using MozartWorkflows.Notifications.Interfaces;

namespace MozartWorkflows.Notifications.Services
{
    public class NotificationOrchestrator : INotificationOrchestrator
    {
        private readonly IEmailNotificationService _emailService;
        private readonly IPushNotificationService _pushService;

        public NotificationOrchestrator(IEmailNotificationService emailService, IPushNotificationService pushService)
        {
            _emailService = emailService;
            _pushService = pushService;
        }

        public async Task SendNotificationAsync(string userId, string userEmail, string messageTemplate, IDictionary<string, string> variables, string mode, string subjectTemplate)
        {
            var message = ApplyTemplate(messageTemplate, variables);
            var subject = string.IsNullOrEmpty(subjectTemplate) ? null : ApplyTemplate(subjectTemplate, variables);

            if (mode.Equals("Push", StringComparison.OrdinalIgnoreCase))
            {
                await _pushService.SendAsync(userId, message);
            }
            else if (mode.Equals("Email", StringComparison.OrdinalIgnoreCase))
            {
                await _emailService.SendAsync(userId, userEmail, message, subject ?? string.Empty);
            }
            else
            {
                throw new ArgumentException($"Unsupported notification mode: {mode}");
            }
        }

        private static string ApplyTemplate(string template, IDictionary<string, string> variables)
        {
            foreach (var kvp in variables)
            {
                template = template.Replace("{{" + kvp.Key + "}}", kvp.Value);
            }
            return template;
        }
    }

}
