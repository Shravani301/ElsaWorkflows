namespace MozartWorkflows.Notifications.Models
{
    public class PendingNotification
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string? UserId { get; set; }
        public string? Message { get; set; }
        public string? Subject { get; set; }
        public string? Mode { get; set; }
        public bool IsSent { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

}
