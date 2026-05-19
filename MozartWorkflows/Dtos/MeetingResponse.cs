namespace MozartWorkflows.Dtos
{
    public class MeetingResponse
    {
        public string? MeetingId { get; set; }
        public string? JoinUrl { get; set; }
        public string? Subject { get; set; }
        public DateTimeOffset? StartDateTime { get; set; }
        public DateTimeOffset? EndDateTime { get; set; }
    }
}
