using MozartWorkflows.Dtos;

namespace MozartWorkflows.Services.Interfaces
{
    public interface IMeetingRepository
    {
        Task SaveMeetingAsync(Meeting meeting);
        Task UpdateMeetingAsync(Meeting meeting);
        Task DeleteMeetingAsync(string meetingId);
    }
}
