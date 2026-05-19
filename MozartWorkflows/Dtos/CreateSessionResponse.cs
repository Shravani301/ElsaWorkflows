namespace MozartWorkflows.Dtos
{
    public class CreateSessionResponse
    {
        public string SessionId { get; set; } = null!;
        public DateTime IdleExpiresAt { get; set; }
        public DateTime AbsoluteExpiresAt { get; set; }
        public DateTime IssuedAt { get; set; }
        public int IdleTimeoutMinutes { get; set; }
        public int AbsoluteTimeoutHours { get; set; }
    }
}
