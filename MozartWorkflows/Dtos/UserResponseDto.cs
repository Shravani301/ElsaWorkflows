namespace MozartWorkflows.Dtos
{
    public class UserResponseDto
    {
        public string UserId { get; set; } = null!;
        public string UserName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Role { get; set; } = null!;
        public int ApplicationId { get; set; }
        public string ApplicationName { get; set; } = null!;
    }
}
