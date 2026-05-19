using MozartWorkflows.Models;

namespace MozartWorkflows.Services.Interfaces;

public interface IUserManagementService
{
    /// <summary>Ensures dashboard auth tables exist and seeds the first admin user if needed.</summary>
    Task EnsureSchemaAsync();

    Task<DashboardUser?> GetByIdAsync(int id);

    Task<IEnumerable<DashboardUser>> GetAllAsync();

    /// <summary>Creates a new user with default password "Password123".</summary>
    Task<DashboardUser> CreateAsync(string username, string email, bool isAdmin);

    /// <summary>Returns false if currentPassword is wrong.</summary>
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);

    /// <summary>Resets password to "Password123".</summary>
    Task ResetPasswordAsync(int userId);

    Task SetActiveAsync(int userId, bool active);

    Task SetAdminAsync(int userId, bool isAdmin);

    Task DeleteAsync(int userId);

    /// <summary>
    /// Resets password to "Password123" by username (used for self-service forgot-password).
    /// Returns false if no active user with that username exists.
    /// </summary>
    Task<bool> ResetPasswordByUsernameAsync(string username);
}
