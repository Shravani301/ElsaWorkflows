using Azure.Identity;
using Microsoft.Graph.Models;
using Microsoft.Graph;
using MozartWorkflows.Dtos;
using MozartWorkflows.Services.Interfaces;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace MozartWorkflows.Services
{
    public class TeamsMeetingService : ITeamsMeetingService
    {
        private const string GraphDefaultScope = "https://graph.microsoft.com/.default";
        private const string IndiaStandardTimeZone = "India Standard Time";
        private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss";
        private static readonly string[] GraphScopes = { GraphDefaultScope };

        private readonly IMeetingRepository _meetingRepository;
        private readonly AzureAdSettings _azureAdSettings;

        public TeamsMeetingService(IMeetingRepository meetingRepository, IOptions<AzureAdSettings> azureAdOptions)
        {
            _meetingRepository = meetingRepository;
            _azureAdSettings = azureAdOptions.Value;
        }

        public async Task<MeetingResponse> CreateMeetingAsync(CreateMeetingRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_azureAdSettings.TenantId) ||
                    string.IsNullOrWhiteSpace(_azureAdSettings.ClientId) ||
                    string.IsNullOrWhiteSpace(_azureAdSettings.ClientSecret) ||
                    string.IsNullOrWhiteSpace(request.UserId))
                {
                    throw new InvalidOperationException("Missing required configuration values");
                }

                if (request.StartTime == null || request.EndTime == null)
                {
                    throw new ArgumentException("StartTime and EndTime are required");
                }

                var credential = new ClientSecretCredential(
                          _azureAdSettings.TenantId,
                          _azureAdSettings.ClientId,
                          _azureAdSettings.ClientSecret);

                var graphClient = new GraphServiceClient(
                    credential,
                    GraphScopes);

                var attendees = request.Attendees?.Select(a => new Attendee
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = a.Email,
                        Name = a.Email
                    },
                    Type = a.IsOptional ? AttendeeType.Optional : AttendeeType.Required
                }).ToList() ?? new List<Attendee>();

                var start = request.StartTime.Value.UtcDateTime;
                var end = request.EndTime.Value.UtcDateTime;

                Console.WriteLine($"[DEBUG] Organizer: {request.UserId}");
                Console.WriteLine($"[DEBUG] Start: {start}, End: {end}");

                var calendarEvent = new Event
                {
                    Subject = request.Subject ?? "Teams Meeting",

                    Start = new DateTimeTimeZone
                    {
                        DateTime = request.StartTime.Value.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
                        TimeZone = IndiaStandardTimeZone
                    },

                    End = new DateTimeTimeZone
                    {
                        DateTime = request.EndTime.Value.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
                        TimeZone = IndiaStandardTimeZone
                    },

                    Attendees = attendees,

                    IsOnlineMeeting = true,
                    OnlineMeetingProvider = OnlineMeetingProviderType.TeamsForBusiness
                };

                var result = await graphClient
                    .Users[request.UserId]
                    .Events
                    .PostAsync(calendarEvent);

                var user = await graphClient.Users[request.UserId].GetAsync();
                Console.WriteLine($"User: {user?.DisplayName ?? "unknown"}, Id: {user?.Id ?? "unknown"}");

                if (result == null)
                    throw new InvalidOperationException("Meeting creation failed");

                Console.WriteLine($"[SUCCESS] Event created: {result.Id}");

                var dbMeeting = new Meeting
                {
                    MeetingId = result.Id!,
                    Subject = result.Subject!,
                    JoinUrl = result.OnlineMeeting?.JoinUrl,
                    StartDateTime = start,
                    EndDateTime = end,
                    CreatedByUserId = request.UserId!,
                    Attendees = request.Attendees ?? new List<AttendeeRequest>()
                };

                await _meetingRepository.SaveMeetingAsync(dbMeeting);

                return new MeetingResponse
                {
                    MeetingId = result.Id!,
                    Subject = result.Subject!,
                    JoinUrl = result.OnlineMeeting?.JoinUrl,
                    StartDateTime = result.Start?.DateTime != null
                        ? DateTimeOffset.Parse(result.Start.DateTime, CultureInfo.InvariantCulture)
                        : null,
                    EndDateTime = result.End?.DateTime != null
                        ? DateTimeOffset.Parse(result.End.DateTime, CultureInfo.InvariantCulture)
                        : null
                };
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)
            {
                var code = ex.Error?.Code;
                var message = ex.Error?.Message;

                Console.WriteLine($"[GRAPH ERROR] Code: {code}");
                Console.WriteLine($"[GRAPH ERROR] Message: {message}");

                throw new InvalidOperationException($"Graph Error: {code} - {message}");
            }
            catch (AuthenticationFailedException ex)
            {
                Console.WriteLine($"[AUTH ERROR] {ex.Message}");
                throw new InvalidOperationException($"Authentication Failed: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GENERAL ERROR] {ex.Message}");
                throw;
            }
        }

        public async Task<MeetingResponse> UpdateMeetingAsync(string meetingId, CreateMeetingRequest request)
        {
            try
            {
                if (request.StartTime == null || request.EndTime == null)
                    throw new ArgumentException("StartTime and EndTime are required");

                var credential = new ClientSecretCredential(
                    _azureAdSettings.TenantId,
                    _azureAdSettings.ClientId,
                    _azureAdSettings.ClientSecret);

                var graphClient = new GraphServiceClient(
                    credential,
                    GraphScopes);

                var attendees = request.Attendees?.Select(a => new Attendee
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = a.Email,
                        Name = a.Name
                    },
                    Type = a.IsOptional ? AttendeeType.Optional : AttendeeType.Required
                }).ToList();

                var updatedEvent = new Event
                {
                    Subject = request.Subject,

                    Start = new DateTimeTimeZone
                    {
                        DateTime = request.StartTime.Value.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
                        TimeZone = IndiaStandardTimeZone
                    },

                    End = new DateTimeTimeZone
                    {
                        DateTime = request.EndTime.Value.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
                        TimeZone = IndiaStandardTimeZone
                    },

                    Attendees = attendees
                };

                await graphClient
                    .Users[request.UserId]
                    .Events[meetingId]
                    .PatchAsync(updatedEvent);

                var dbMeeting = new Meeting
                {
                    MeetingId = meetingId,
                    Subject = request.Subject ?? string.Empty,
                    StartDateTime = request.StartTime,
                    EndDateTime = request.EndTime,
                    CreatedByUserId = request.UserId!
                };

                await _meetingRepository.UpdateMeetingAsync(dbMeeting);

                return new MeetingResponse
                {
                    MeetingId = meetingId,
                    Subject = request.Subject ?? string.Empty,
                    StartDateTime = request.StartTime,
                    EndDateTime = request.EndTime
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPDATE ERROR] {ex.Message}");
                throw;
            }
        }

        public async Task<bool> CancelMeetingAsync(string meetingId, string userId)
        {
            try
            {
                var credential = new ClientSecretCredential(
                    _azureAdSettings.TenantId,
                    _azureAdSettings.ClientId,
                    _azureAdSettings.ClientSecret);

                var graphClient = new GraphServiceClient(
                    credential,
                    GraphScopes);

                await graphClient
                    .Users[userId]
                    .Events[meetingId]
                    .DeleteAsync();

                await _meetingRepository.DeleteMeetingAsync(meetingId);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DELETE ERROR] {ex.Message}");
                throw;
            }
        }

        public async Task ProcessWebhookEvent(string userId, string eventId, string changeType)
        {
            try
            {
                var credential = new ClientSecretCredential(
                    _azureAdSettings.TenantId,
                    _azureAdSettings.ClientId,
                    _azureAdSettings.ClientSecret);

                var graphClient = new GraphServiceClient(
                    credential,
                    GraphScopes);

                if (changeType == "deleted")
                {
                    await _meetingRepository.DeleteMeetingAsync(eventId);
                    return;
                }

                var ev = await graphClient
                    .Users[userId]
                    .Events[eventId]
                    .GetAsync();

                if (ev == null)
                    return;

                var attendees = ev.Attendees?.Select(a => new AttendeeRequest
                {
                    Email = a.EmailAddress?.Address ?? string.Empty
                }).ToList() ?? new List<AttendeeRequest>();

                var meeting = new Meeting
                {
                    MeetingId = ev.Id!,
                    Subject = ev.Subject!,
                    JoinUrl = ev.OnlineMeeting?.JoinUrl,
                    StartDateTime = ev.Start?.DateTime != null
                        ? DateTimeOffset.Parse(ev.Start.DateTime, CultureInfo.InvariantCulture)
                        : null,
                    EndDateTime = ev.End?.DateTime != null
                        ? DateTimeOffset.Parse(ev.End.DateTime, CultureInfo.InvariantCulture)
                        : null,
                    CreatedByUserId = userId,
                    Attendees = attendees
                };

                await _meetingRepository.SaveMeetingAsync(meeting);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WEBHOOK ERROR] {ex.Message}");
                throw;
            }
        }

        public async Task<string?> CreateSubscriptionAsync(string userId)
        {
            try
            {
                var notificationUrl = _azureAdSettings.WebhookNotificationUrl;

                if (string.IsNullOrWhiteSpace(notificationUrl))
                    throw new InvalidOperationException("Webhook URL is missing in appsettings");

                var credential = new ClientSecretCredential(
                    _azureAdSettings.TenantId,
                    _azureAdSettings.ClientId,
                    _azureAdSettings.ClientSecret);

                var graphClient = new GraphServiceClient(
                    credential,
                    GraphScopes);

                var subscription = new Subscription
                {
                    ChangeType = "created,updated,deleted",
                    Resource = $"users/{userId}/events",
                    NotificationUrl = notificationUrl,
                    ExpirationDateTime = DateTimeOffset.UtcNow.AddHours(2),
                    ClientState = "my-secret-state"
                };

                var result = await graphClient.Subscriptions.PostAsync(subscription);

                Console.WriteLine($"[SUBSCRIPTION CREATED] Id: {result?.Id}");

                return result?.Id;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SUBSCRIPTION ERROR] {ex.Message}");
                throw;
            }
        }

        public async Task SyncEventsAsync(string userId)
        {
            var credential = new ClientSecretCredential(
                _azureAdSettings.TenantId,
                _azureAdSettings.ClientId,
                _azureAdSettings.ClientSecret);

            var graphClient = new GraphServiceClient(credential);

            var events = await graphClient
                .Users[userId]
                .Events
                .GetAsync();

            var indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById(IndiaStandardTimeZone);

            foreach (var ev in events?.Value ?? [])
            {
                DateTimeOffset? startIST = null;
                DateTimeOffset? endIST = null;

                if (!string.IsNullOrEmpty(ev.Start?.DateTime))
                {
                    var start = DateTimeOffset.Parse(ev.Start.DateTime, CultureInfo.InvariantCulture);
                    startIST = TimeZoneInfo.ConvertTime(start, indiaTimeZone);
                }

                if (!string.IsNullOrEmpty(ev.End?.DateTime))
                {
                    var end = DateTimeOffset.Parse(ev.End.DateTime, CultureInfo.InvariantCulture);
                    endIST = TimeZoneInfo.ConvertTime(end, indiaTimeZone);
                }

                var meeting = new Meeting
                {
                    MeetingId = ev.Id!,
                    Subject = ev.Subject!,
                    JoinUrl = ev.OnlineMeeting?.JoinUrl,
                    StartDateTime = startIST,
                    EndDateTime = endIST,
                    CreatedByUserId = userId,
                    Attendees = ev.Attendees?.Select(a => new AttendeeRequest
                    {
                        Email = a.EmailAddress?.Address ?? string.Empty
                    }).ToList() ?? new List<AttendeeRequest>()
                };

                await _meetingRepository.SaveMeetingAsync(meeting);
            }
        }
    }
}
