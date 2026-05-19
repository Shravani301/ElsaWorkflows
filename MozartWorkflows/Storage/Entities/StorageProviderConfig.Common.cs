namespace MozartWorkflows.Storage.Entities;

public partial class StorageProviderConfig
{
    public long Id { get; set; }
    public string ProviderType { get; set; } = default!;
    public string Name { get; set; } = default!;

    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // New columns you added
    public long? ApplicationId { get; set; }
    public long? CloudProviderId { get; set; }
}
