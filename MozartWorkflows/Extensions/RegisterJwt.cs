using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using MozartWorkflows.Services.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace MozartWorkflows.Extensions
{
    public static class RegisterJwt
    {
        private const string MixedScheme = "Mixed";

        public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration config)
        {
            var secretKey = config.GetValue<string>("jwt:Key");
            var clockSkewMinutes = config.GetValue<int>("jwt:ClockSkewMinutes", 2);
            var enableSessionTracking = config.GetValue<bool>("jwt:Session:EnableTracking", true);

            if (string.IsNullOrEmpty(secretKey))
                throw new InvalidOperationException("JWT Key is not configured");

            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            services.AddAuthentication(options =>
            {
                options.DefaultScheme = MixedScheme;
                options.DefaultChallengeScheme = MixedScheme;
            })
            .AddCookie("Cookies", ConfigureCookieOptions)
            .AddJwtBearer("Bearer", options => ConfigureJwtOptions(options, secretKey, clockSkewMinutes, enableSessionTracking))
            .AddPolicyScheme(MixedScheme, MixedScheme, options =>
            {
                options.ForwardDefaultSelector = context => IsApiRequest(context.Request.Path) ? "Bearer" : "Cookies";
            });

            return services;
        }

        private static void ConfigureCookieOptions(CookieAuthenticationOptions options)
        {
            options.LoginPath = "/elsa-login";
            options.AccessDeniedPath = "/access-denied";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Events = new CookieAuthenticationEvents
            {
                OnValidatePrincipal = ValidateCookiePrincipalAsync
            };
        }

        private static void ConfigureJwtOptions(JwtBearerOptions options, string secretKey, int clockSkewMinutes, bool enableSessionTracking)
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ClockSkew = TimeSpan.FromMinutes(clockSkewMinutes),
                NameClaimType = "userId",
                RoleClaimType = ClaimTypes.Role
            };

            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context => ValidateTokenSessionAsync(context, enableSessionTracking)
            };
        }

        private static Task ValidateCookiePrincipalAsync(CookieValidatePrincipalContext context)
        {
            var userId = context.Principal?.FindFirst("userId")?.Value
                      ?? context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
                return Task.CompletedTask;

            try
            {
                var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                if (cache.TryGetValue($"loggedout:{userId}", out _))
                    context.RejectPrincipal();
            }
            catch (Exception)
            {
                // Fail open so auth keeps working even if cache resolution is unavailable.
            }

            return Task.CompletedTask;
        }

        private static async Task ValidateTokenSessionAsync(TokenValidatedContext context, bool enableSessionTracking)
        {
            if (!enableSessionTracking)
                return;

            var userId = context.Principal?.FindFirst("userId")?.Value;
            var sessionId = context.Principal?.FindFirst("sessionId")?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sessionId))
                return;

            var sessionService = context.HttpContext.RequestServices.GetRequiredService<ISessionService>();

            if (!await sessionService.ValidateSession(userId, sessionId))
            {
                context.Fail("Session expired or invalid");
                return;
            }

            await sessionService.UpdateSessionActivity(userId, sessionId);
        }

        private static bool IsApiRequest(PathString requestPath) =>
            requestPath.StartsWithSegments("/api") || requestPath.StartsWithSegments("/elsa-api");
    }
}
