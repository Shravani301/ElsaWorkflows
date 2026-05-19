namespace MozartWorkflows.Dtos
{
    public class AzureAdSettings
    {
        public string TenantId { get; set; } = default!;
        public string ClientId { get; set; } = default!;
        public string ClientSecret { get; set; } = default!;
        public string WebhookNotificationUrl { get; set; } = default!;
    }
}
