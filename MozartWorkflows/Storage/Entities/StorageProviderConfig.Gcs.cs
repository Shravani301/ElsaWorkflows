namespace MozartWorkflows.Storage.Entities;

public partial class StorageProviderConfig
{
    // ---------- GCS ----------
    public string? GcpJsonKey { get; set; }
    public string? GcpBucketName { get; set; }
}
