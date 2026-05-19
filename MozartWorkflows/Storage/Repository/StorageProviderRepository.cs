using System.Data;
using Dapper;
using MozartWorkflows.Storage.Entities;

namespace MozartWorkflows.Storage.Repository;

public interface IStorageProviderRepository
{
    Task<List<StorageProviderConfig>> GetActiveAsync(
        long? applicationId = null,
        string? providerName = null);
}

public sealed class StorageProviderRepository : IStorageProviderRepository
{
    private readonly Func<IDbConnection> _connFactory;

    public StorageProviderRepository(IConfiguration cfg)
    {
        var cs = cfg.GetConnectionString("connectionString")
                 ?? throw new InvalidOperationException("Connection string 'connectionString' missing.");
        _connFactory = () => new Microsoft.Data.SqlClient.SqlConnection(cs);
    }

    public async Task<List<StorageProviderConfig>> GetActiveAsync(
        long? applicationId = null,
        string? providerName = null)
    {
        const string sql = @"
SELECT *
FROM   StorageProviderConfig
WHERE  IsActive = 1
  AND (@Name  IS NULL OR Name          = @Name)
  AND (@AppId IS NULL OR ApplicationId = @AppId)";
        using var conn = _connFactory();
        var rows = await conn.QueryAsync<StorageProviderConfig>(
            sql, new { Name = providerName, AppId = applicationId });
        return rows.ToList();
    }
}

