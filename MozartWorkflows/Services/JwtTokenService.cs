using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MozartWorkflows.Dtos;
using MozartWorkflows.Services.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace MozartWorkflows.Services
{
    public class JwtTokenService : IJwtTokenService
    {
        private const string JwtKeyConfig = "jwt:Key";
        private const string GenerateRefreshTokenConfig = "jwt:GenerateRefreshToken";

        private readonly IConfiguration _config;
        private readonly IDbService _dbService;
        private readonly ILogger<JwtTokenService> _logger;
        private readonly ISessionService _sessionService;

        public JwtTokenService(
            IConfiguration config,
            IDbService dbService,
            ILogger<JwtTokenService> logger,
            ISessionService sessionService)
        {
            _config = config;
            _dbService = dbService;
            _logger = logger;
            _sessionService = sessionService;
        }

        /// <summary>
        /// Interface implementation - Create token with session management (synchronous)
        /// </summary>
        public AuthTokens CreateToken(string userId, string role, int applicationId, string applicationName)
        {
            // Call the async version and wait for it to complete
            return CreateTokenAsync(userId, role, applicationId, applicationName).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Async version for session-based token creation
        /// </summary>
        public async Task<AuthTokens> CreateTokenAsync(string userId, string role, int applicationId, string applicationName, string? deviceInfo = null, string? ipAddress = null)
        {
            ValidateParameters(userId, role, applicationId, applicationName);

            var accessTokenExpiryMinutes = _config.GetValue<int>("jwt:AccessTokenExpiry", 30);
            var refreshTokenExpiryDays = _config.GetValue<int>("jwt:RefreshTokenExpiry", 7);
            var generateRefreshToken = _config.GetValue<bool>(GenerateRefreshTokenConfig, false);

            // Create session and get complete session information
            var sessionResponse = await _sessionService.CreateSession(userId, deviceInfo, ipAddress);

            var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, role),
        new Claim("userId", userId),
        new Claim("applicationId", applicationId.ToString()),
        new Claim("applicationName", applicationName),
        new Claim("sessionId", sessionResponse.SessionId),
        new Claim("sessionIssuedAt", sessionResponse.IssuedAt.ToString("o")), // ISO 8601 format
        new Claim("sessionIdleExpiresAt", sessionResponse.IdleExpiresAt.ToString("o")),
        new Claim("issuedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
    };

            var secretKey = Encoding.UTF8.GetBytes(_config[JwtKeyConfig] ??
                throw new InvalidOperationException("JWT Key is missing"));
            var signingKey = new SymmetricSecurityKey(secretKey);

            var accessTokenExpiresAt = DateTime.UtcNow.AddMinutes(accessTokenExpiryMinutes);
            var accessToken = new JwtSecurityToken(
                claims: claims,
                expires: accessTokenExpiresAt,
                signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
            );

            string? refreshToken = null;
            if (generateRefreshToken)
            {
                refreshToken = GenerateSecureRefreshToken();
                await StoreRefreshTokenAsync(userId, refreshToken,
                    DateTime.UtcNow.AddDays(refreshTokenExpiryDays), sessionResponse.SessionId);
            }

            // Calculate session expiry in minutes
            var sessionExpiresInMinutes = (int)Math.Max(0,
                (sessionResponse.IdleExpiresAt - DateTime.UtcNow).TotalMinutes);

            return new AuthTokens
            {
                AccessToken = new JwtSecurityTokenHandler().WriteToken(accessToken),
                RefreshToken = refreshToken,
                AccessTokenExpiresAt = accessTokenExpiresAt,
                AccessTokenExpiresInMinutes = accessTokenExpiryMinutes,
                SessionId = sessionResponse.SessionId,
                SessionExpiresInMinutes = sessionExpiresInMinutes,
                SessionExpiresAt = sessionResponse.IdleExpiresAt,
                SessionAbsoluteExpiresAt = sessionResponse.AbsoluteExpiresAt, // Optional: if you want to return absolute expiry too
                SessionIssuedAt = sessionResponse.IssuedAt, // Optional
                SessionIdleTimeoutMinutes = sessionResponse.IdleTimeoutMinutes, // Optional
                SessionAbsoluteTimeoutHours = sessionResponse.AbsoluteTimeoutHours // Optional
            };
        }

        /// <summary>
        /// Validates token and session (async)
        /// </summary>
        public async Task<bool> ValidateTokenAndSessionAsync(string token, string userId)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userId))
                return false;

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_config[JwtKeyConfig] ??
                    throw new InvalidOperationException("JWT Key is missing"));
                var clockSkewMinutes = _config.GetValue<int>("jwt:ClockSkewMinutes", 2);

                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(clockSkewMinutes),
                    NameClaimType = "userId"
                }, out SecurityToken _);

                var sessionId = principal.FindFirst("sessionId")?.Value;
                if (string.IsNullOrEmpty(sessionId))
                {
                    _logger.LogWarning("No session ID in token for user {UserId}", userId);
                    return false;
                }

                var isValidSession = await _sessionService.ValidateSession(userId, sessionId);
                if (!isValidSession)
                {
                    _logger.LogWarning("Invalid session {SessionId} for user {UserId}", sessionId, userId);
                    return false;
                }

                await _sessionService.UpdateSessionActivity(userId, sessionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token validation failed for user {UserId}", userId);
                return false;
            }
        }

        /// <summary>
        /// Validates the refresh token and extracts user information (synchronous interface implementation)
        /// </summary>
        public bool ValidateRefreshToken(string refreshToken, out string? userId)
        {
            userId = null;

            var query = @"
                SELECT * FROM RefreshTokens 
                WHERE RefreshToken = @RefreshToken 
                AND Revoked = 0 
                AND ExpiresAt > @CurrentTime";

            var parameters = new
            {
                RefreshToken = refreshToken,
                CurrentTime = DateTime.UtcNow
            };

            var refreshTokenRecord = _dbService.QueryFirstOrDefault<RefreshTokenDto>(query, parameters);

            if (refreshTokenRecord != null)
            {
                userId = refreshTokenRecord.UserId;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Refreshes the access token using a valid refresh token (synchronous interface implementation)
        /// </summary>
        public AuthTokens RefreshToken(string refreshToken)
        {
            var generateRefreshToken = _config.GetValue<bool>(GenerateRefreshTokenConfig, false);
            if (!generateRefreshToken)
            {
                throw new InvalidOperationException("Refresh tokens are not enabled in configuration");
            }

            if (ValidateRefreshToken(refreshToken, out string? userId))
            {
                var user = GetUserDetails(userId!) ?? throw new SecurityTokenException("User not found");
                var newTokens = CreateToken(user.UserId, user.Role, user.ApplicationId, user.ApplicationName);
                RevokeRefreshToken(refreshToken);
                return newTokens;
            }

            throw new SecurityTokenException("Invalid refresh token");
        }

        /// <summary>
        /// Async version for refreshing token with session management
        /// </summary>
        public async Task<AuthTokens> RefreshTokenAsync(string refreshToken, string? deviceInfo = null,
            string? ipAddress = null)
        {
            if (string.IsNullOrEmpty(refreshToken))
                throw new ArgumentException("Refresh token is required");

            var generateRefreshToken = _config.GetValue<bool>(GenerateRefreshTokenConfig, false);
            if (!generateRefreshToken)
                throw new InvalidOperationException("Refresh tokens are disabled");

            var userId = await ValidateRefreshTokenAsync(refreshToken);
            if (string.IsNullOrEmpty(userId))
                throw new SecurityTokenException("Invalid refresh token");

            var user = GetUserDetails(userId);
            if (user == null)
                throw new SecurityTokenException("User not found");

            await RevokeSessionByRefreshTokenAsync(refreshToken);

            return await CreateTokenAsync(user.UserId, user.Role, user.ApplicationId,
                user.ApplicationName, deviceInfo, ipAddress);
        }

        /// <summary>
        /// Creates a secure reset token used only for password reset scenarios (interface implementation)
        /// </summary>
        public string CreateResetToken(string userId, string email)
        {
            var resetExpiryTime = _config.GetValue<int>("jwt:ResetExpiryTime", 10);

            var claims = new List<Claim>
            {
                new Claim("userId", userId),
                new Claim("email", email),
                new Claim("otpVerified", "true"),
                new Claim("purpose", "reset_password"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var key = Encoding.UTF8.GetBytes(_config[JwtKeyConfig] ??
                throw new InvalidOperationException("JWT Key is not configured"));
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(resetExpiryTime),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = new JwtSecurityTokenHandler().CreateToken(tokenDescriptor);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// Validates the reset token and extracts user details (interface implementation)
        /// </summary>
        public bool ValidateResetToken(string token, out string? userId, out string? email)
        {
            userId = null;
            email = null;

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_config[JwtKeyConfig] ??
                throw new InvalidOperationException("JWT Key is not configured"));

            var clockSkewMinutes = _config.GetValue<int>("jwt:ClockSkewMinutes", 2);

            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(clockSkewMinutes)
                }, out SecurityToken _);

                var purpose = principal.FindFirst("purpose")?.Value;
                var verified = principal.FindFirst("otpVerified")?.Value;

                if (purpose != "reset_password" || verified != "true")
                    return false;

                userId = principal.FindFirst("userId")?.Value;
                email = principal.FindFirst("email")?.Value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Logout - revoke session
        /// </summary>
        public async Task LogoutAsync(string userId, string sessionId)
        {
            await _sessionService.RevokeSession(userId, sessionId);
            _logger.LogInformation("User {UserId} logged out from session {SessionId}", userId, sessionId);
        }

        /// <summary>
        /// Logout from all devices
        /// </summary>
        public async Task LogoutAllAsync(string userId)
        {
            await _sessionService.RevokeAllUserSessions(userId);
            _logger.LogInformation("User {UserId} logged out from all sessions", userId);
        }

        /// <summary>
        /// Gets active sessions for user
        /// </summary>
        public async Task<List<UserSession>> GetActiveSessionsAsync(string userId)
        {
            return await _sessionService.GetActiveSessions(userId);
        }

        // Helper methods
        private async Task<string?> ValidateRefreshTokenAsync(string refreshToken)
        {
            var query = @"
                SELECT UserId, SessionId 
                FROM RefreshTokens 
                WHERE RefreshToken = @RefreshToken 
                AND Revoked = 0 AND ExpiresAt > @CurrentTime";

            var parameters = new
            {
                RefreshToken = refreshToken,
                CurrentTime = DateTime.UtcNow
            };

            var record = await _dbService.QueryFirstOrDefaultAsync<RefreshTokenDto>(query, parameters);
            return record?.UserId;
        }

        private async Task StoreRefreshTokenAsync(string userId, string refreshToken,
            DateTime expiresAt, string sessionId)
        {
            var query = @"
                INSERT INTO RefreshTokens (UserId, RefreshToken, ExpiresAt, IssuedAt, SessionId) 
                VALUES (@UserId, @RefreshToken, @ExpiresAt, @IssuedAt, @SessionId)";

            var parameters = new
            {
                UserId = userId,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt,
                IssuedAt = DateTime.UtcNow,
                SessionId = sessionId
            };

            await _dbService.ExecuteAsync(query, parameters);
        }

        private async Task RevokeSessionByRefreshTokenAsync(string refreshToken)
        {
            var query = @"
                SELECT UserId, SessionId 
                FROM RefreshTokens 
                WHERE RefreshToken = @RefreshToken";

            var record = await _dbService.QueryFirstOrDefaultAsync<dynamic>(query,
                new { RefreshToken = refreshToken });

            if (record != null)
            {
                await _sessionService.RevokeSession(record.UserId, record.SessionId);

                await _dbService.ExecuteAsync(
                    "UPDATE RefreshTokens SET Revoked = 1 WHERE RefreshToken = @RefreshToken",
                    new { RefreshToken = refreshToken });
            }
        }

        private void RevokeRefreshToken(string refreshToken)
        {
            var query = "UPDATE RefreshTokens SET Revoked = 1 WHERE RefreshToken = @RefreshToken";
            var parameters = new { RefreshToken = refreshToken };
            _dbService.Execute(query, parameters);
        }

        private static void ValidateParameters(string userId, string role, int applicationId, string applicationName)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

            if (string.IsNullOrWhiteSpace(role))
                throw new ArgumentException("Role cannot be null or empty", nameof(role));

            if (applicationId <= 0)
                throw new ArgumentException("Application ID must be greater than 0", nameof(applicationId));

            if (string.IsNullOrWhiteSpace(applicationName))
                throw new ArgumentException("Application name cannot be null or empty", nameof(applicationName));
        }

        private static string GenerateSecureRefreshToken()
        {
            var randomBytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }

        private UserResponseDto? GetUserDetails(string userId)
        {
            var query = "EXEC GetUserDetailsByUserId @UserId = @UserId";
            var parameters = new { UserId = userId };
            return _dbService.QueryFirstOrDefault<UserResponseDto>(query, parameters);
        }
    }
}