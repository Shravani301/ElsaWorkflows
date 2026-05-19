namespace MozartWorkflows.Dtos
{
    public class RefreshTokenDto
    {
        public string UserId { get; set; } = null!;
        public string RefreshToken { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public DateTime IssuedAt { get; set; }
        public bool Revoked { get; set; }
    }

}
