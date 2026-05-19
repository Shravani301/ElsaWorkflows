namespace MozartWorkflows.Dtos
{
    public class Meeting
    {
        public int Id { get; set; }

        public string MeetingId { get; set; } = null!;
        public string Subject { get; set; } = null!;
        public string? JoinUrl { get; set; }

        public DateTimeOffset? StartDateTime { get; set; }
        public DateTimeOffset? EndDateTime { get; set; }

        public string CreatedByUserId { get; set; } = null!;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public List<AttendeeRequest> Attendees { get; set; } = new();
    }

}
