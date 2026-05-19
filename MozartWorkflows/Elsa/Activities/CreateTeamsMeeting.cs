using Azure.Identity;
using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace MozartWorkflows.Elsa.Activities 
{ 

    [Action(
        Category = "Microsoft Graph",
        Description = "Creates a Microsoft Teams online meeting using Microsoft Graph API."
    )]
    public class CreateTeamsMeeting : Activity
    {
        private static readonly string[] GraphScopes = { "https://graph.microsoft.com/.default" };

        [ActivityInput(
            Label = "Tenant ID",
            Hint = "The Azure AD Tenant ID.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string TenantId { get; set; } = default!;
        [ActivityInput(
            Label = "Client ID",
            Hint = "The Azure AD Application (Client) ID.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string ClientId { get; set; } = default!;
        [ActivityInput(
            Label = "Client Secret",
            Hint = "The Azure AD Application Secret.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string ClientSecret { get; set; } = default!;
        [ActivityInput(
            Label = "Organizer User ID",
            Hint = "The Object ID or User Principal Name (Email) of the user organizing the meeting.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string UserId { get; set; } = default!;
        [ActivityInput(
            Label = "Subject",
            Hint = "The subject/title of the Teams meeting.",
            DefaultValue = "Teams Meeting",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public string Subject { get; set; } = "Teams Meeting";
        [ActivityInput(
            Label = "Start Time (UTC)",
            Hint = "The start date and time of the meeting in UTC.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public DateTimeOffset? StartTime { get; set; }
        [ActivityInput(
            Label = "End Time (UTC)",
            Hint = "The end date and time of the meeting in UTC.",
            SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid }
        )]
        public DateTimeOffset? EndTime { get; set; }
        [ActivityOutput(Hint = "The created Teams meeting details including the Join URL.")]
        public OnlineMeetingDetails? MeetingDetails { get; set; }
        protected override async ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
        {
            try
            {
                // 1. Authentication Setup (Client Credentials Flow)
                // Azure.Identity handles generating and caching the access token automatically.
                var options = new TokenCredentialOptions
                {
                    AuthorityHost = AzureAuthorityHosts.AzurePublicCloud
                };
                var clientSecretCredential = new ClientSecretCredential(
                    TenantId, ClientId, ClientSecret, options);
                // Initialize Graph Client
                var graphClient = new GraphServiceClient(clientSecretCredential, GraphScopes);
                // 2. Set Meeting Times (Fallbacks if not provided)
                var meetingStart = StartTime ?? DateTimeOffset.UtcNow.AddMinutes(5);
                var meetingEnd = EndTime ?? meetingStart.AddMinutes(30);
                // 3. Prepare the Request Body
                var requestBody = new OnlineMeeting
                {
                    StartDateTime = meetingStart,
                    EndDateTime = meetingEnd,
                    Subject = Subject,
                    // Optional: allow everyone to bypass the waiting lobby
                  //  AutoAdmittedUsers = OnlineMeetingAutoAdmittedUsers.EveryoneInCompany
                };
                // 4. Call Microsoft Graph API to create the meeting
                // POST /users/{userId}/onlineMeetings
                var onlineMeeting = await graphClient.Users[UserId].OnlineMeetings.PostAsync(requestBody);
                if (onlineMeeting != null)
                {
                    // Map the response to our output model
                    MeetingDetails = new OnlineMeetingDetails
                    {
                        MeetingId = onlineMeeting.Id,
                        JoinUrl = onlineMeeting.JoinWebUrl,
                        Subject = onlineMeeting.Subject,
                        StartDateTime = onlineMeeting.StartDateTime,
                        EndDateTime = onlineMeeting.EndDateTime
                    };
                }
                // Complete the activity successfully
                return Done();
            }
            catch (ODataError odataError)
            {
                // Graph API specific errors
                Console.WriteLine($"Graph API Error: {odataError.Error?.Code} - {odataError.Error?.Message}");
                return Fault(odataError);
            }
            catch (Exception ex)
            {
                // General errors
                Console.WriteLine($"Error creating Teams meeting: {ex.Message}");
                return Fault(ex);
            }
        }
    }
    // A simple DTO to hold the meeting output details
    public class OnlineMeetingDetails
    {
        public string? MeetingId { get; set; }
        public string? JoinUrl { get; set; }
        public string? Subject { get; set; }
        public DateTimeOffset? StartDateTime { get; set; }
        public DateTimeOffset? EndDateTime { get; set; }
    }
}

