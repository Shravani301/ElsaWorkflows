using MozartWorkflows.Dtos;

namespace MozartWorkflows.Services.Interfaces
{
    public interface ISessionService
    {
        Task<CreateSessionResponse> CreateSession(string userId, string? deviceInfo = null, string? ipAddress = null);
        Task<bool> ValidateSession(string userId, string sessionId);
        Task UpdateSessionActivity(string userId, string sessionId);
        Task RevokeSession(string userId, string sessionId);
        Task RevokeAllUserSessions(string userId);
        Task<List<UserSession>> GetActiveSessions(string userId);
        Task CleanupExpiredSessions();
    }
}
