namespace MozartWorkflows.Storage.Entities;

public partial class StorageProviderConfig
{
    // ---------- Azure --------
    public string? ConnectionString { get; set; }
    public string? ContainerName { get; set; }
}
