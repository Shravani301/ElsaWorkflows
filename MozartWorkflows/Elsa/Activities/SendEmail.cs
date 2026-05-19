using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Design;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MozartWorkflows.Models;
using MozartWorkflows.Elsa.Constants;
using Microsoft.Extensions.Options;

namespace MozartWorkflows.Elsa.Activities
{
    [Action(
        Category = "Email",
        Description = "Send an email with optional attachments."
    )]
    public class SendEmail : Activity
    {
        public SendEmail(IOptions<EmailSettings> options)
        {
            var emailSettings = options.Value;
            Port = emailSettings.Port;
            Host = emailSettings.Host ?? string.Empty;
            FromEmail = emailSettings.Email ?? string.Empty;
            AppPassword = emailSettings.Password ?? string.Empty;
        }
        [ActivityInput(
            Label = "Port",
            Hint = "Port",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public int Port { get; set; }
        [ActivityInput(
            Label = "Host",
            Hint = "Host",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string Host { get; set; }
        [ActivityInput(
            Label = "From Email Address",
            Hint = "The email address of the sender.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string FromEmail { get; set; }

        [ActivityInput(
            Label = "App Password",
            Hint = "App Password of the sender.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string AppPassword { get; set; }

        [ActivityInput(
            Label = "To Email Address",
            Hint = "The email address of the recipient.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]

        public string ToEmail { get; set; } = default!;

        [ActivityInput(
            Label = "Subject",
            Hint = "The subject of the email.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string Subject { get; set; } = default!;

        [ActivityInput(
            Label = "Body",
            Hint = "The body of the email.",
            UIHint = ActivityInputUIHints.MultiLine,
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string Body { get; set; } = default!;

        [ActivityInput(
            Label = "Attachment",
            Hint = "Optional attachment as a base64 string.",
            UIHint = ActivityInputUIHints.Json,
            SupportedSyntaxes = new[] { SyntaxNames.Json, SyntaxNames.JavaScript }
        )]
        public List<UplodedFile>? Attachments { get; set; }

        [ActivityOutput] public string? Output { get; set; }

        protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
        {
            try
            {
                var email = new MimeMessage();
                email.Sender = MailboxAddress.Parse(FromEmail);
                email.To.Add(MailboxAddress.Parse(ToEmail));
                email.Subject = Subject;

                var builder = new BodyBuilder
                {
                    HtmlBody = Body
                };

                if (Attachments != null)
                {
                    foreach (var attachment in Attachments)
                    {
                        var fileBytes = Convert.FromBase64String(attachment.Base64String ?? string.Empty);
                        builder.Attachments.Add(attachment.FileName, fileBytes, ContentType.Parse("application/octet-stream"));
                    }
                }

                email.Body = builder.ToMessageBody();

                using var smtp = new SmtpClient();
                await smtp.ConnectAsync(Host, Port, SecureSocketOptions.StartTls, context.CancellationToken);
                await smtp.AuthenticateAsync(FromEmail, AppPassword, context.CancellationToken);
                await smtp.SendAsync(email);
                await smtp.DisconnectAsync(true, context.CancellationToken);

                Output = "Email sent successfully.";
                return Done();
            }
            catch (Exception ex)
            {
                Output = $"Failed to send email: {ex.Message}";
                return Outcomes("Failed");
            }
        }
    }
}
