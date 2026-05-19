namespace MozartWorkflows.Models;

public class DashboardUser
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    // PasswordHash and Salt deliberately omitted from API responses
    public bool Active { get; set; } = true;
    public bool IsAdmin { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
