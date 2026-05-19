using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MozartWorkflows.Elsa.Constants;
using Microsoft.Extensions.Options;
namespace MozartWorkflows.Elsa.Activities
{
    [Action(
        Category = "Email",
        Description = "Send an email with optional attachments."
    )]
    public class SendEmailOtp : Activity
    {
        private readonly string? _host;
        private readonly int _port;
        private readonly string? _fromEmail;
        private readonly string? _appPassword;

        public SendEmailOtp(IOptions<EmailSettings> options, IConfiguration configuration)
        {
            _ = options.Value;
            _host = configuration["EmailSettings:Host"];
            _port = int.Parse(configuration["EmailSettings:Port"] ?? "0"); // <-- parse to int
            _fromEmail = configuration["EmailSettings:Email"];
            _appPassword = configuration["EmailSettings:Password"];
        }

        [ActivityInput(
            Label = "To Email Address",
            Hint = "The email address of the recipient.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string ToEmail { get; set; } = default!;

        public string Subject { get; set; } = "Mozart Login";

        [ActivityOutput] public string? Output { get; set; }

        protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
        {
            try
            {
                var otp = context.GetVariable<string>("generatedOTP");
                Console.WriteLine($"Generated OTP: {otp}");
                var body = $"Your OTP is {otp}. Please do not share it with anyone.";

                var email = new MimeMessage();
                email.Sender = MailboxAddress.Parse(_fromEmail);
                email.To.Add(MailboxAddress.Parse(ToEmail));
                email.Subject = Subject;

                var builder = new BodyBuilder { HtmlBody = body };
                email.Body = builder.ToMessageBody();

                using var smtp = new SmtpClient();
                await smtp.ConnectAsync(_host, _port, SecureSocketOptions.StartTls, context.CancellationToken);
                await smtp.AuthenticateAsync(_fromEmail, _appPassword, context.CancellationToken);
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
