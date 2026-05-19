using MozartWorkflows.Storage.Abstractions;
using MozartWorkflows.Storage.Providers;
using MozartWorkflows.Storage.Repository;

namespace MozartWorkflows.Storage.Factory;

public interface IStorageServiceFactory
{
    IStorageService Get(long? applicationId = null, string? providerName = null);
}

public sealed class StorageServiceFactory : IStorageServiceFactory
{
    private readonly IStorageProviderRepository _repo;
    public StorageServiceFactory(IStorageProviderRepository repo) => _repo = repo;

    public IStorageService Get(long? applicationId = null, string? providerName = null)
    {
        // 1. Load active providers for the app (blocks the current thread once)
        var candidates = _repo
            .GetActiveAsync(applicationId, providerName)
            .GetAwaiter()                   // ⇦ convert Task<List<..>> → List<..>
            .GetResult();

        if (!candidates.Any())
            throw new InvalidOperationException(
                applicationId == null
                    ? "No active storage provider configured."
                    : $"No active storage provider configured for ApplicationId={applicationId}.");

        // 2. Pick default > oldest-active to keep the choice deterministic
        var cfg = candidates
            .OrderByDescending(p => p.IsDefault) // prefer the row marked IsDefault=1
            .ThenBy(p => p.Id)                   // otherwise take the oldest row
            .First();

        // 3. Materialise the concrete storage service
        return cfg.ProviderType.ToUpperInvariant() switch
        {
            "AWS" => new AwsS3StorageService(cfg),
            "AZURE" => new AzureBlobStorageService(cfg),
            "GCS" => new GcsStorageService(cfg),
            "OMNIDOCS" => new OmniDocsStorageService(cfg),
            _ => throw new NotSupportedException(
                     $"Provider type '{cfg.ProviderType}' not supported.")
        };
    }
}
