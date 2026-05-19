using MozartWorkflows.Dtos;

namespace MozartWorkflows.Services.Interfaces
{
    public interface ITeamsMeetingService
    {
        Task<MeetingResponse> CreateMeetingAsync(CreateMeetingRequest request);
        Task<MeetingResponse> UpdateMeetingAsync(string meetingId, CreateMeetingRequest request);
        Task<bool> CancelMeetingAsync(string meetingId, string userId);
        Task ProcessWebhookEvent(string userId, string eventId, string changeType);
        Task<string?> CreateSubscriptionAsync(string userId);
        Task SyncEventsAsync(string userId);



    }
}
