using System.Data;
using Dapper;
using MozartWorkflows.Dtos;
using MozartWorkflows.Services.Interfaces;
using System.Text.Json;   
using MozartWorkflows.Notifications.Interfaces;

namespace MozartWorkflows.Services
{
    public class MeetingRepository:IMeetingRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly ISignalRService _signalRService;

        public MeetingRepository(IDbConnectionFactory dbConnectionFactory, ISignalRService signalRService)
        {
            _dbConnectionFactory = dbConnectionFactory;
            _signalRService = signalRService;
        }

        private async Task NotifyMeetingUsersAsync(Meeting meeting, string eventName, IDbConnection? connection = null)
        {
            var targetUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(meeting.CreatedByUserId))
                targetUsers.Add(meeting.CreatedByUserId);

            if (meeting.Attendees != null)
                targetUsers.UnionWith(
                    meeting.Attendees
                        .Select(attendee => attendee.Email)
                        .Where(email => !string.IsNullOrWhiteSpace(email))!);

            if (targetUsers.Count == 0 && connection != null && !string.IsNullOrEmpty(meeting.MeetingId))
            {
                var creator = await connection.QueryFirstOrDefaultAsync<string>("SELECT CreatedByUserId FROM TeamMeeting WHERE MeetingId = @MeetingId", new { MeetingId = meeting.MeetingId });
                if (!string.IsNullOrEmpty(creator)) targetUsers.Add(creator);
            }

            var meetingJson = JsonSerializer.Serialize(meeting);
            foreach (var userEmail in targetUsers)
            {
                await _signalRService.SendEventToUserAsync(userEmail, eventName, meetingJson);
            }
        }

        public async Task SaveMeetingAsync(Meeting meeting)
        {
            using var connection = _dbConnectionFactory.CreateConnection();

            if (string.IsNullOrWhiteSpace(meeting.MeetingId))
                throw new ArgumentException("MeetingId is required", nameof(meeting));

            var attendeesList = (meeting.Attendees ?? new List<AttendeeRequest>())
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Email))
                .Select(a => new
                {
                    name = a.Name,
                    email = a.Email
                })
                .ToList();

            var attendeesJson = JsonSerializer.Serialize(attendeesList);

            var parameters = new DynamicParameters();
            parameters.Add("@MeetingId", meeting.MeetingId);
            parameters.Add("@Subject", meeting.Subject);
            parameters.Add("@JoinUrl", meeting.JoinUrl);
            parameters.Add("@StartDateTime", meeting.StartDateTime);
            parameters.Add("@EndDateTime", meeting.EndDateTime);
            parameters.Add("@CreatedByUserId", meeting.CreatedByUserId);
            parameters.Add("@Attendees", attendeesJson, DbType.String);

            await connection.ExecuteAsync(
                "sp_SaveMeeting",
                parameters,
                commandType: CommandType.StoredProcedure);

            await NotifyMeetingUsersAsync(meeting, "MeetingAdded");
        }
        public async Task UpdateMeetingAsync(Meeting meeting)
        {
            using var connection = _dbConnectionFactory.CreateConnection();

            var parameters = new DynamicParameters();
            parameters.Add("@MeetingId", meeting.MeetingId);
            parameters.Add("@Subject", meeting.Subject);
            parameters.Add("@StartDateTime", meeting.StartDateTime);
            parameters.Add("@EndDateTime", meeting.EndDateTime);

            await connection.ExecuteAsync(
                "usp_UpdateMeeting",
                parameters,
                commandType: CommandType.StoredProcedure
            );

            await NotifyMeetingUsersAsync(meeting, "MeetingUpdated");
        }

        public async Task DeleteMeetingAsync(string meetingId)
        {
            using var connection = _dbConnectionFactory.CreateConnection();

            await NotifyMeetingUsersAsync(
                new Meeting { MeetingId = meetingId },
                "MeetingDeleted",
                connection);

            var parameters = new DynamicParameters();
            parameters.Add("@MeetingId", meetingId, DbType.String);

            await connection.ExecuteAsync(
                "sp_DeleteMeeting",
                parameters,
                commandType: CommandType.StoredProcedure);
        }
    }
}
