namespace MozartWorkflows.Dtos
{
    public class CreateMeetingRequest
    {
        public string UserId { get; set; } = default!; 

        public string Subject { get; set; } = "Teams Meeting";

        public DateTimeOffset? StartTime { get; set; }
        public DateTimeOffset? EndTime { get; set; }

        public List<AttendeeRequest> Attendees { get; set; } = new();
    }
    public class AttendeeRequest
    {
        public string Email { get; set; } = default!;
        public string? Name { get; set; } = default;
        public bool IsOptional { get; set; } = false;
    }
}
