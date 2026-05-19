namespace MozartWorkflows.Dtos
{
    public class AuthTokens
    {
        public string AccessToken { get; set; } = null!;
        public string? RefreshToken { get; set; }
        public DateTime AccessTokenExpiresAt { get; set; }
        public int AccessTokenExpiresInMinutes { get; set; }
        public string SessionId { get; set; } = null!;
        public int SessionExpiresInMinutes { get; set; }
        public DateTime SessionExpiresAt { get; set; }

        // Optional additional session properties
        public DateTime SessionAbsoluteExpiresAt { get; set; }
        public DateTime SessionIssuedAt { get; set; }
        public int SessionIdleTimeoutMinutes { get; set; }
        public int SessionAbsoluteTimeoutHours { get; set; }
    }
}