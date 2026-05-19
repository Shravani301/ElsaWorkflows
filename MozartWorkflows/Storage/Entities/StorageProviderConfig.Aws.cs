namespace MozartWorkflows.Storage.Entities;

public partial class StorageProviderConfig
{
    // ---------- AWS ----------
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string? Region { get; set; }
    public string? BucketName { get; set; }
}
