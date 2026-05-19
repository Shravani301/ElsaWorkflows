using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MozartWorkflows.Dtos;
using MozartWorkflows.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MozartWorkflows.Services
{
    public class SessionService : ISessionService
    {
        private readonly IDbService _dbService;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<SessionService> _logger;
        private readonly bool _enableTracking;
        private readonly int _idleTimeoutMinutes;
        private readonly int _absoluteTimeoutHours;
        private readonly int _maxSessionsPerUser;

        // In-memory tracking for performance (optional)
        private static readonly ConcurrentDictionary<string, DateTime> _sessionActivity = new();

        public SessionService(
            IDbService dbService,
            IMemoryCache memoryCache,
            IConfiguration config,
            ILogger<SessionService> logger)
        {
            _dbService = dbService;
            _memoryCache = memoryCache;
            _logger = logger;

            _enableTracking = config.GetValue<bool>("jwt:Session:EnableTracking", true);
            _idleTimeoutMinutes = config.GetValue<int>("jwt:Session:IdleTimeoutMinutes", 30);
            _absoluteTimeoutHours = config.GetValue<int>("jwt:Session:AbsoluteTimeoutHours", 8);
            _maxSessionsPerUser = config.GetValue<int>("jwt:Session:MaxSessionsPerUser", 5);
        }

        public async Task<CreateSessionResponse> CreateSession(string userId, string? deviceInfo = null, string? ipAddress = null)
        {
            if (!_enableTracking)
            {
                // Return dummy response if disabled
                return new CreateSessionResponse
                {
                    SessionId = Guid.NewGuid().ToString(),
                    IdleExpiresAt = DateTime.UtcNow.AddMinutes(_idleTimeoutMinutes),
                    AbsoluteExpiresAt = DateTime.UtcNow.AddHours(_absoluteTimeoutHours),
                    IssuedAt = DateTime.UtcNow,
                    IdleTimeoutMinutes = _idleTimeoutMinutes,
                    AbsoluteTimeoutHours = _absoluteTimeoutHours
                };
            }

            var sessionId = Guid.NewGuid().ToString();
            var issuedAt = DateTime.UtcNow;
            var idleExpiresAt = issuedAt.AddMinutes(_idleTimeoutMinutes);
            var absoluteExpiresAt = issuedAt.AddHours(_absoluteTimeoutHours);

            try
            {
                // Store in database
                var query = @"
            INSERT INTO UserSessions (
                UserId, SessionId, IssuedAt, IdleExpiresAt, AbsoluteExpiresAt,
                LastActivity, DeviceInfo, IpAddress, IsActive
            ) VALUES (
                @UserId, @SessionId, @IssuedAt, @IdleExpiresAt, @AbsoluteExpiresAt,
                @LastActivity, @DeviceInfo, @IpAddress, 1
            )";

                var parameters = new
                {
                    UserId = userId,
                    SessionId = sessionId,
                    IssuedAt = issuedAt,
                    IdleExpiresAt = idleExpiresAt,
                    AbsoluteExpiresAt = absoluteExpiresAt,
                    LastActivity = issuedAt,
                    DeviceInfo = deviceInfo,
                    IpAddress = ipAddress
                };

                await _dbService.ExecuteAsync(query, parameters);

                // Store in memory cache for fast validation
                var cacheKey = GetCacheKey(userId, sessionId);
                _memoryCache.Set(cacheKey, new SessionInfo
                {
                    UserId = userId,
                    SessionId = sessionId,
                    IssuedAt = issuedAt,
                    IdleExpiresAt = idleExpiresAt,
                    AbsoluteExpiresAt = absoluteExpiresAt,
                    LastActivity = issuedAt
                }, absoluteExpiresAt);

                // Enforce max sessions
                await EnforceMaxSessions(userId);

                _logger.LogInformation("Session created for user {UserId}: {SessionId}", userId, sessionId);

                // Return complete session information
                return new CreateSessionResponse
                {
                    SessionId = sessionId,
                    IdleExpiresAt = idleExpiresAt,
                    AbsoluteExpiresAt = absoluteExpiresAt,
                    IssuedAt = issuedAt,
                    IdleTimeoutMinutes = _idleTimeoutMinutes,
                    AbsoluteTimeoutHours = _absoluteTimeoutHours
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create session for user {UserId}", userId);
                throw new InvalidOperationException($"Failed to create session for user {userId}.", ex);
            }
        }

        public async Task<bool> ValidateSession(string userId, string sessionId)
        {
            if (!_enableTracking)
                return true; // Bypass validation if disabled

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sessionId))
                return false;

            try
            {
                var cacheKey = GetCacheKey(userId, sessionId);

                // First, check memory cache
                if (_memoryCache.TryGetValue(cacheKey, out SessionInfo? cachedSession) && cachedSession != null)
                {
                    return ValidateSessionTime(cachedSession, userId, sessionId);
                }

                // If not in cache, check database using GetAsync instead of QueryFirstOrDefaultAsync
                var query = @"
                    SELECT UserId, SessionId, IssuedAt, IdleExpiresAt, AbsoluteExpiresAt, LastActivity, 1 as IsActive
                    FROM UserSessions 
                    WHERE UserId = @UserId AND SessionId = @SessionId AND IsActive = 1";

                var parameters = new { UserId = userId, SessionId = sessionId };
                var session = await _dbService.GetAsync<SessionInfo>(query, parameters);

                if (session == null)
                {
                    _logger.LogWarning("Session not found or inactive: {SessionId} for user {UserId}",
                        sessionId, userId);
                    return false;
                }

                // Validate times
                return ValidateSessionTime(session, userId, sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating session {SessionId} for user {UserId}",
                    sessionId, userId);
                return false;
            }
        }

        private bool ValidateSessionTime(SessionInfo session, string userId, string sessionId)
        {
            var now = DateTime.UtcNow;

            // Check absolute expiry
            if (now > session.AbsoluteExpiresAt)
            {
                _logger.LogInformation("Session absolute expiry reached: {SessionId}", sessionId);
                MarkSessionInactive(userId, sessionId).ConfigureAwait(false);
                return false;
            }

            // Check idle timeout
            if (now > session.IdleExpiresAt)
            {
                _logger.LogInformation("Session idle timeout reached: {SessionId}", sessionId);
                MarkSessionInactive(userId, sessionId).ConfigureAwait(false);
                return false;
            }

            return true;
        }

        public async Task UpdateSessionActivity(string userId, string sessionId)
        {
            if (!_enableTracking)
                return;

            try
            {
                var newIdleExpiresAt = DateTime.UtcNow.AddMinutes(_idleTimeoutMinutes);

                // Update database
                var query = @"
                    UPDATE UserSessions 
                    SET LastActivity = @LastActivity, IdleExpiresAt = @IdleExpiresAt
                    WHERE UserId = @UserId AND SessionId = @SessionId AND IsActive = 1";

                var parameters = new
                {
                    UserId = userId,
                    SessionId = sessionId,
                    LastActivity = DateTime.UtcNow,
                    IdleExpiresAt = newIdleExpiresAt
                };

                await _dbService.ExecuteAsync(query, parameters);

                // Update cache
                var cacheKey = GetCacheKey(userId, sessionId);
                if (_memoryCache.TryGetValue(cacheKey, out SessionInfo? session) && session != null)
                {
                    session.LastActivity = DateTime.UtcNow;
                    session.IdleExpiresAt = newIdleExpiresAt;
                    _memoryCache.Set(cacheKey, session, session.AbsoluteExpiresAt);
                }

                // Update in-memory tracker
                _sessionActivity[GetActivityKey(userId, sessionId)] = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update session activity for {SessionId}", sessionId);
            }
        }

        public async Task RevokeSession(string userId, string sessionId)
        {
            if (!_enableTracking)
                return;

            await MarkSessionInactive(userId, sessionId);
            _logger.LogInformation("Session revoked: {SessionId} for user {UserId}", sessionId, userId);
        }

        public async Task RevokeAllUserSessions(string userId)
        {
            if (!_enableTracking)
                return;

            try
            {
                var query = "UPDATE UserSessions SET IsActive = 0 WHERE UserId = @UserId AND IsActive = 1";
                await _dbService.ExecuteAsync(query, new { UserId = userId });

                // Clear from cache
                ClearUserSessionsFromCache(userId);

                _logger.LogInformation("All sessions revoked for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to revoke all sessions for user {UserId}", userId);
                throw new InvalidOperationException($"Failed to revoke all sessions for user {userId}.", ex);
            }
        }

        public async Task<List<UserSession>> GetActiveSessions(string userId)
        {
            var query = @"
                SELECT SessionId, IssuedAt, IdleExpiresAt, AbsoluteExpiresAt, 
                       LastActivity, DeviceInfo, IpAddress
                FROM UserSessions 
                WHERE UserId = @UserId AND IsActive = 1
                ORDER BY LastActivity DESC";

            var sessions = await _dbService.GetAllAsync<UserSession>(query, new { UserId = userId });
            return sessions.ToList();
        }

        public async Task CleanupExpiredSessions()
        {
            try
            {
                var query = @"
                    UPDATE UserSessions 
                    SET IsActive = 0 
                    WHERE (IsActive = 1) AND 
                          (IdleExpiresAt <= @CurrentTime OR AbsoluteExpiresAt <= @CurrentTime)";

                var cleaned = await _dbService.EditData(query, new { CurrentTime = DateTime.UtcNow });

                if (cleaned > 0)
                {
                    _logger.LogDebug("Cleaned up {Count} expired sessions", cleaned);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup expired sessions");
            }
        }

        private async Task EnforceMaxSessions(string userId)
        {
            try
            {
                var query = @"
                    WITH RankedSessions AS (
                        SELECT SessionId, 
                               ROW_NUMBER() OVER (ORDER BY LastActivity DESC) as RowNum
                        FROM UserSessions 
                        WHERE UserId = @UserId AND IsActive = 1
                    )
                    UPDATE UserSessions 
                    SET IsActive = 0 
                    WHERE SessionId IN (
                        SELECT SessionId FROM RankedSessions WHERE RowNum > @MaxSessions
                    )";

                var revoked = await _dbService.EditData(query, new
                {
                    UserId = userId,
                    MaxSessions = _maxSessionsPerUser
                });

                if (revoked > 0)
                {
                    _logger.LogDebug("Enforced max sessions, revoked {Count} for user {UserId}",
                        revoked, userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enforce max sessions for user {UserId}", userId);
            }
        }

        private async Task MarkSessionInactive(string userId, string sessionId)
        {
            try
            {
                var query = @"
                    UPDATE UserSessions 
                    SET IsActive = 0, RevokedAt = @RevokedAt
                    WHERE UserId = @UserId AND SessionId = @SessionId";

                await _dbService.ExecuteAsync(query, new
                {
                    UserId = userId,
                    SessionId = sessionId,
                    RevokedAt = DateTime.UtcNow
                });

                // Remove from cache
                _memoryCache.Remove(GetCacheKey(userId, sessionId));
                _sessionActivity.TryRemove(GetActivityKey(userId, sessionId), out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark session inactive: {SessionId}", sessionId);
            }
        }


        private void ClearUserSessionsFromCache(string userId)
        {
            // This is simplified - in production, you'd track all cache keys for the user
            // Using a pattern-based cache invalidation approach
            // For IMemoryCache, you need to track keys manually or use a more sophisticated cache
            _logger.LogDebug("Clearing sessions from cache for user {UserId}", userId);
        }

        private static string GetCacheKey(string userId, string sessionId) => $"session:{userId}:{sessionId}";
        private static string GetActivityKey(string userId, string sessionId) => $"{userId}|{sessionId}";

        private sealed class SessionInfo
        {
            public string UserId { get; set; } = null!;
            public string SessionId { get; set; } = null!;
            public DateTime IssuedAt { get; set; }
            public DateTime IdleExpiresAt { get; set; }
            public DateTime AbsoluteExpiresAt { get; set; }
            public DateTime LastActivity { get; set; }
            public bool IsActive { get; set; } = true;
        }
    }

    public sealed class UserSession
    {
        public string SessionId { get; set; } = null!;
        public DateTime IssuedAt { get; set; }
        public DateTime IdleExpiresAt { get; set; }
        public DateTime AbsoluteExpiresAt { get; set; }
        public DateTime LastActivity { get; set; }
        public string DeviceInfo { get; set; } = null!;
        public string IpAddress { get; set; } = null!;
        public TimeSpan IdleTimeRemaining => IdleExpiresAt - DateTime.UtcNow;
        public TimeSpan AbsoluteTimeRemaining => AbsoluteExpiresAt - DateTime.UtcNow;
    }
}
