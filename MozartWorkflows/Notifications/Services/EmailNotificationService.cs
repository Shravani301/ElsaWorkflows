using Microsoft.Extensions.Options;
using MozartWorkflows.Notifications.Interfaces;
using MozartWorkflows.Notifications.Models;
using System.Net;
using System.Net.Mail;

namespace MozartWorkflows.Notifications.Services
{
    public class EmailNotificationService : IEmailNotificationService
    {
        private readonly SmtpSettings _settings;

        public EmailNotificationService(IOptions<SmtpSettings> options)
        {
            _settings = options.Value;
        }

        public async Task SendAsync(string userId, string userEmail, string message, string subject)
        {
            if (string.IsNullOrWhiteSpace(userEmail)) return;

            var mail = new MailMessage
            {
                From = new MailAddress(_settings.Email ?? string.Empty, _settings.DisplayName),
                Subject = subject ?? "Monocept Notification",
                Body = message,
                IsBodyHtml = false
            };

            mail.To.Add(userEmail);

            using var smtp = new SmtpClient(_settings.Host, _settings.Port)
            {
                Credentials = new NetworkCredential(_settings.Email, _settings.Password),
                EnableSsl = true
            };

            await smtp.SendMailAsync(mail);
        }
    }

}
