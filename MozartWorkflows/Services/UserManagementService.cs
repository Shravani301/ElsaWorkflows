using Dapper;
using MozartWorkflows;
using MozartWorkflows.Models;
using MozartWorkflows.Services.Interfaces;

namespace MozartWorkflows.Services;

public class UserManagementService : IUserManagementService
{
    private readonly IDbConnectionFactory _connectionFactory;

    private const string DefaultPassword = "Password123";
    private const string DefaultAdminUsername = "admin";
    private const string DefaultAdminEmail = "admin@mozart.local";

    public UserManagementService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task EnsureSchemaAsync()
    {
        var salt = PasswordHasher.CreateSaltKey(5);
        var hash = PasswordHasher.CreatePasswordHash(DefaultPassword, salt);

        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(SqlQueries.SqlServer.EnsureDashboardAuthTables);
        await conn.ExecuteAsync(SqlQueries.SqlServer.EnsureDefaultAdminUser, new
        {
            UserId = Guid.NewGuid().ToString(),
            Username = DefaultAdminUsername,
            Email = DefaultAdminEmail,
            PasswordHash = hash,
            Salt = salt
        });
    }

    public async Task<DashboardUser?> GetByIdAsync(int id)
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<DashboardUser>(SqlQueries.SqlServer.GetUserById, new { Id = id });
    }

    public async Task<IEnumerable<DashboardUser>> GetAllAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<DashboardUser>(SqlQueries.SqlServer.GetAllUsers);
    }

    public async Task<DashboardUser> CreateAsync(string username, string email, bool isAdmin)
    {
        var salt = PasswordHasher.CreateSaltKey(5);
        var hash = PasswordHasher.CreatePasswordHash(DefaultPassword, salt);
        var userId = Guid.NewGuid().ToString();

        using var conn = _connectionFactory.CreateConnection();
        var created = await conn.QueryFirstAsync<DashboardUser>(SqlQueries.SqlServer.CreateUser, new
        {
            UserId = userId,
            Username = username,
            Email = email,
            PasswordHash = hash,
            Salt = salt,
            IsAdmin = isAdmin
        });
        return created;
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        using var conn = _connectionFactory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<(string PasswordHash, string Salt)>(
            SqlQueries.SqlServer.GetUserPasswordHash, new { Id = userId });

        if (row == default) return false;

        bool valid = PasswordHasher.ValidatePassword(currentPassword, row.Salt, row.PasswordHash);
        if (!valid) return false;

        var newSalt = PasswordHasher.CreateSaltKey(5);
        var newHash = PasswordHasher.CreatePasswordHash(newPassword, newSalt);

        await conn.ExecuteAsync(SqlQueries.SqlServer.UpdateUserPassword, new { PasswordHash = newHash, Salt = newSalt, Id = userId });
        return true;
    }

    public async Task ResetPasswordAsync(int userId)
    {
        var newSalt = PasswordHasher.CreateSaltKey(5);
        var newHash = PasswordHasher.CreatePasswordHash(DefaultPassword, newSalt);

        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(SqlQueries.SqlServer.UpdateUserPassword, new { PasswordHash = newHash, Salt = newSalt, Id = userId });
    }

    public async Task SetActiveAsync(int userId, bool active)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(SqlQueries.SqlServer.SetUserActive, new { Active = active, Id = userId });
    }

    public async Task SetAdminAsync(int userId, bool isAdmin)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(SqlQueries.SqlServer.SetUserAdmin, new { IsAdmin = isAdmin, Id = userId });
    }

    public async Task DeleteAsync(int userId)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(SqlQueries.SqlServer.DeleteUser, new { Id = userId });
    }

    public async Task<bool> ResetPasswordByUsernameAsync(string username)
    {
        var newSalt = PasswordHasher.CreateSaltKey(5);
        var newHash = PasswordHasher.CreatePasswordHash(DefaultPassword, newSalt);

        using var conn = _connectionFactory.CreateConnection();
        int rows = await conn.ExecuteAsync(SqlQueries.SqlServer.UpdateUserPasswordByUsername, new { PasswordHash = newHash, Salt = newSalt, Username = username });
        return rows > 0;
    }
}
