
    using Elsa.ActivityResults;
    using Elsa.Attributes;
    using Elsa.Expressions;
    using Elsa.Services;
    using Elsa.Services.Models;
    using MailKit;
    using MailKit.Net.Imap;
    using MailKit.Search;
    using MailKit.Security;
    using MimeKit;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using MozartWorkflows.Elsa.Constants;

namespace MozartWorkflows.Elsa.Activities
{
    [Action(
            Category = "Email",
            Description = "Retrieve the latest unseen email from a specified email address based on the last saved UID."
        )]
        public class RetrieveEmails : Activity
        {
            [ActivityInput(
                Label = "IMAP Server",
                Hint = "The IMAP server to connect to.",
                DefaultValue = "imap.gmail.com",
                SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
            )]
            public string ImapServer { get; set; } = "imap.gmail.com";

            [ActivityInput(
                Label = "IMAP Port",
                Hint = "The port to use for the IMAP connection.",
                DefaultValue = 993,
                SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
            )]
            public int ImapPort { get; set; } = 993;

            [ActivityInput(
                Label = "Email Address",
                Hint = "The email address to retrieve emails from.",
                SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
            )]
            public string EmailAddress { get; set; } = default!;

            [ActivityInput(
                Label = "App Password",
                Hint = "The App password for the email account.",
                SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
            )]
            public string Password { get; set; } = default!;
            [ActivityInput(
                Label = "Uid",
                Hint = "The Uid for the email .",
                SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
            )]
            public uint Uid { get; set; } = default!;

            [ActivityOutput(Hint = "The retrieved email.")]
            public EmailDto? RetrievedEmail { get; set; }

            protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
            {
                using var client = new ImapClient();
                await client.ConnectAsync(ImapServer, ImapPort, SecureSocketOptions.SslOnConnect);
                await client.AuthenticateAsync(EmailAddress, Password);

                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly);

                // Fetch all UIDs from the inbox.
                var uids = await inbox.SearchAsync(SearchQuery.All);

                if (!uids.Any())
                {
                    Console.WriteLine("No emails found in the inbox.");
                    return Done("No emails found in the inbox");
                }
                var lastUid = uids.Max().Id;
                Console.WriteLine(lastUid);
                // Get the maximum UID, which is the last UID in the inbox.
                if (lastUid < Uid)
                {
                    Console.WriteLine("No emails found in the inbox.");
                    return Done("No emails found in the inbox");
                }



                // Fetch the email with the next UID
                var message = await inbox.GetMessageAsync(new UniqueId(Uid));

                var emailDto = new EmailDto
                {
                    Uid = Uid.ToString(),
                    MessageId = message.MessageId,
                    Subject = message.Subject,
                    From = string.Join(", ", message.From.Mailboxes.Select(m => $"{m.Name} <{m.Address}>")),
                    To = string.Join(", ", message.To.Mailboxes.Select(t => $"{t.Name} <{t.Address}>")),
                    Date = message.Date.DateTime,
                    Body = message.TextBody
                };

                foreach (var attachment in message.Attachments)
                {
                    if (attachment is MimePart mimePart)
                    {
                        using var memoryStream = new MemoryStream();
                        await mimePart.Content.DecodeToAsync(memoryStream);
                        emailDto.Attachments.Add(new EmailAttachmentDto
                        {
                            FileName = mimePart.FileName,
                            Content = memoryStream.ToArray()
                        });
                    }
                    else if (attachment is MessagePart rfc822)
                    {
                        using var memoryStream = new MemoryStream();
                        await rfc822.Message.WriteToAsync(memoryStream);
                        emailDto.Attachments.Add(new EmailAttachmentDto
                        {
                            FileName = rfc822.Message.Subject,
                            Content = memoryStream.ToArray()
                        });
                    }
                }

                await client.DisconnectAsync(true);

                // Save the retrieved email
                RetrievedEmail = emailDto;

                return Done();
            }

            public class EmailDto
            {
                public string Uid { get; set; } = string.Empty;
                public string MessageId { get; set; } = string.Empty;
                public string Subject { get; set; } = string.Empty;
                public string From { get; set; } = string.Empty;
                public string To { get; set; } = string.Empty;
                public DateTime Date { get; set; }
                public string Body { get; set; } = string.Empty;
                public List<EmailAttachmentDto> Attachments { get; set; } = new List<EmailAttachmentDto>();
            }

            public class EmailAttachmentDto
            {
                public string FileName { get; set; } = string.Empty;
                public byte[] Content { get; set; } = Array.Empty<byte>();
            }
        }
    }
