using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;
using MozartWorkflows.Dtos;
using MozartWorkflows.Elsa.Constants;
using MozartWorkflows.Extensions;
using MozartWorkflows.Models;
using MozartWorkflows.Notifications.Hubs;
using MozartWorkflows.Notifications.Interfaces;
using MozartWorkflows.Notifications.Models;
using MozartWorkflows.Notifications.Repository;
using MozartWorkflows.Notifications.Services;
using MozartWorkflows.Notifications.Utilities;
using MozartWorkflows.PhoneCall.Models;
using MozartWorkflows.Services;
using MozartWorkflows.Services.Interfaces;
using MozartWorkflows.Storage.Abstractions;
using MozartWorkflows.Storage.Factory;
using MozartWorkflows.Storage.Repository;
using NLog;
using NLog.Web;
using QuestPDF.Infrastructure;
using RulesEngine.ExpressionBuilders;
using RulesEngine.Models;
using System.Security.Claims;

namespace MozartWorkflows
{
    public class Program
    {
        private Program() { }

        public static void Main(string[] args)
        {
            var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
            logger.Debug("Initialized main");

            try
            {
                var builder = WebApplication.CreateBuilder(args);
                var config = builder.Configuration;

                QuestPDF.Settings.License = LicenseType.Community;

                // SignalR
                builder.Services.AddSignalR();

                builder.Logging.ClearProviders();
                builder.Host.UseNLog();

                builder.Services.AddCors();
                builder.Services.AddControllers();
                
                // ✅ Add Elsa with JWT authentication
                builder.Services.AddElsa(config);

                // ✅ Add JWT Authentication (moved to extension method)
                builder.Services.AddJwtAuthentication(config);

                // Register other services
                RegisterServices(builder, config);

                // Add Swagger
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();

                builder.Services.AddRazorPages();

                var app = builder.Build();

                // Get logger from services for middleware
                var appLogger = app.Services.GetRequiredService<ILogger<Program>>();
              
                ConfigureMiddleware(app, appLogger);

                app.Run();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Fatal error during startup.");
                throw new InvalidOperationException("Fatal error during startup.", ex);
            }
            finally
            {
                LogManager.Shutdown();
            }
        }

        private static void RegisterServices(WebApplicationBuilder builder, ConfigurationManager config)
        {
            // Authorization
            builder.Services.AddSingleton<IAuthorizationPolicyProvider, DynamicApplicationPolicyProvider>();

            // Configuration
            builder.Services.Configure<EmailSettings>(config.GetSection("EmailSettings"));
            builder.Services.Configure<SmtpSettings>(config.GetSection("EmailSettings"));
            builder.Services.Configure<TwilioSettings>(config.GetSection("Twilio"));
            builder.Services.Configure<AzureAdSettings>(config.GetSection("AzureAd"));
           
            // Singleton services
            builder.Services.AddSingleton<AzureOcrService>();

            // Scoped services
            builder.Services.AddScoped<IDbConnectionFactory, DbConnectionFactory>();
            builder.Services.AddScoped<IUserService, UserService>();
            builder.Services.AddScoped<IStorageProviderRepository, StorageProviderRepository>();
            builder.Services.AddScoped<IStorageServiceFactory, StorageServiceFactory>();
            builder.Services.AddScoped<IStorageService>(sp =>
                sp.GetRequiredService<IStorageServiceFactory>().Get());
            builder.Services.AddScoped<IEmailNotificationService, EmailNotificationService>();
            builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
            builder.Services.AddScoped<INotificationOrchestrator, NotificationOrchestrator>();
            builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
            builder.Services.AddScoped<ISignalRService, SignalRService>();
            builder.Services.AddSingleton<IOnlineUserTracker, InMemoryUserTracker>();
            builder.Services.AddScoped<PendingNotificationFlusher>();
            builder.Services.AddScoped<IRulesRepository, DbRulesRepository>();
            builder.Services.AddScoped<IRuleDataService, RuleDataService>();
            builder.Services.AddScoped<IRuleService, RuleServiceImpl>();
            builder.Services.AddScoped<IDbService, DapperDbService>();
            builder.Services.AddScoped<ITeamsMeetingService, TeamsMeetingService>();
            builder.Services.AddScoped<IMeetingRepository, MeetingRepository>();
            

            // Audit logging
            var logMode = config.GetValue<string>("AuditLogging:Mode");
            builder.Services.AddScoped<IAuditService>(sp =>
                logMode == "Database"
                    ? sp.GetRequiredService<AuditServiceImpl>()
                    : sp.GetRequiredService<FileAuditServiceImpl>());

            builder.Services.AddScoped<AuditServiceImpl>();
            builder.Services.AddScoped<FileAuditServiceImpl>();
            builder.Services.AddHostedService<AuditWorker>();
            builder.Services.AddHostedService<WorkflowAuditWorker>();
            builder.Services.AddHostedService<EventSyncJob>();


            builder.Services.Configure<BatchConfig>(
                builder.Configuration.GetSection("BatchConfig"));
         

            // App initialization
            builder.Services.AddScoped<IAppInitializer, AppInitializer>();
            builder.Services.AddMemoryCache();

            // Register session service
            builder.Services.AddScoped<ISessionService, SessionService>();
            builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
            builder.Services.AddScoped<ConfigManager>();

            // ── Workflow Change Audit ─────────────────────────────────────────────
            builder.Services.AddScoped<RequestUserContext>();
            builder.Services.AddScoped<IWorkflowChangeAuditService, WorkflowChangeAuditService>();

            // ── User Management ───────────────────────────────────────────────────
            builder.Services.AddScoped<PasswordHasher>();
            builder.Services.AddScoped<IUserManagementService, UserManagementService>();

            // Rules Engine
            builder.Services.AddSingleton<RuleExpressionParser>(sp =>
            {
                return new RuleExpressionParser(new ReSettings
                {
                    CustomTypes = new Type[]
                    {
                        typeof(WorkflowExecutionService),
                        typeof(Utils),
                        typeof(CacheService)
                    }
                });
            });

            builder.Services.WithJavaScriptOptions(options => options.EnableConfigurationAccess = true);
        }

#pragma warning disable S3776
        private static void ConfigureMiddleware(WebApplication app, ILogger<Program> logger)
        {
            RegisterStartupInitialization(app);

            // CORS
#pragma warning disable S5122
            app.UseCors(policy => policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());
#pragma warning restore S5122

            // Swagger for development
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // Standard middleware
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();

            // Request logging
            app.Use(async (context, next) =>
            {
                logger.LogInformation("Incoming request: {Method} {Path}", context.Request.Method, context.Request.Path);
                await next();
            });

            // Authentication/Authorization
            app.UseAuthentication();

            // Populate RequestUserContext so Elsa notification handlers can record
            // who made the change even when /elsa-api/* routes skip Cookie auth.
            app.Use(async (context, next) =>
            {
                await PopulateRequestUserContextAsync(context);
                await next();
            });

            app.UseAuthorization();

            // Endpoints
            app.MapControllers();
            app.MapRazorPages();
            app.UseHttpActivities(); // Elsa

            // Shared logout handler — works for both the fetch-based POST from doLogout()
            // and any direct GET navigation (e.g. typed URL, link, fallback).
            async Task LogoutHandler(HttpContext context)
            {
                try
                {
                    logger.LogInformation("Logout requested via {Method}", context.Request.Method);

                    // Resolve userId from cookie claims (stored as the ElsaDashboardUsers.Id int)
                    var userId = context.User.FindFirst("userId")?.Value
                              ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                    if (!string.IsNullOrEmpty(userId) && int.TryParse(userId, out int userIdInt))
                    {
                        // 1. Stamp the most-recent successful login row in ElsaLoginAudit
                        //    with IsLogout=1 and LogoutTime=now so the audit trail is complete.
                        try
                        {
                            var dbFactory = context.RequestServices
                                .GetRequiredService<MozartWorkflows.Services.Interfaces.IDbConnectionFactory>();
                            using var conn = dbFactory.CreateConnection();

                            await conn.ExecuteAsync(SqlQueries.SqlServer.UpdateLogoutAudit, new { UserId = userIdInt });
                            logger.LogInformation("LogoutAudit updated for userId={UserId}", userIdInt);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Could not update ElsaLoginAudit for userId={UserId}", userIdInt);
                        }

                        // 2. Revoke server-side session so any stale cookie is immediately rejected
                        //    by OnValidatePrincipal on the next request.
                        try
                        {
                            var sessionSvc = context.RequestServices
                                .GetRequiredService<MozartWorkflows.Services.Interfaces.ISessionService>();
                            await sessionSvc.RevokeAllUserSessions(userId);
                            logger.LogInformation("Sessions revoked for userId={UserId}", userId);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Could not revoke sessions — continuing logout");
                        }

                        // 3. Set MemoryCache flag so OnValidatePrincipal rejects this userId's
                        //    cookie on any subsequent request — blocks "new tab after logout" instantly.
                        try
                        {
                            var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
                            cache.Set($"loggedout:{userId}", true, TimeSpan.FromHours(8));
                            logger.LogInformation("MemoryCache logout flag set for userId={UserId}", userId);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Could not set logout cache flag for userId={UserId}", userId);
                        }
                    }

                    // Expire the HttpOnly auth cookie via Set-Cookie response header.
                    await context.SignOutAsync("Cookies");

                    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                    context.Response.Headers.Pragma = "no-cache";

                    // For fetch calls (doLogout), the client navigates itself after receiving
                    // the response. For direct GET navigation (fallback), return redirect HTML.
                    // Note: same-origin fetch sends Sec-Fetch-Mode: same-origin, not cors.
                    bool isFetch = IsFetchLogoutRequest(context.Request);
                    if (isFetch)
                    {
                        context.Response.StatusCode = 200;
                        await context.Response.WriteAsync("ok");
                    }
                    else
                    {
                        context.Response.ContentType = "text/html; charset=utf-8";
                        await context.Response.WriteAsync(@"<!DOCTYPE html>
<html><head><title>Logging out...</title></head><body>
<script>
try{localStorage.clear();}catch(e){}
try{sessionStorage.clear();}catch(e){}
window.location.replace('/elsa-login');
</script>
<noscript><meta http-equiv=""refresh"" content=""0;url=/elsa-login""></noscript>
</body></html>");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Logout error occurred.");
                    context.Response.Redirect("/elsa-login?error=logout_failed");
                }
            }

            app.MapPost("/logout", LogoutHandler);
            app.MapGet("/logout",  LogoutHandler);

            
            app.MapWhen(context =>
            !context.Request.Path.StartsWithSegments("/api") &&
            !context.Request.Path.StartsWithSegments("/elsa-api") &&
            !context.Request.Path.StartsWithSegments("/swagger") &&
            !context.Request.Path.StartsWithSegments("/_content"),
            appBuilder =>
            {
                appBuilder.UseRouting();

                
                appBuilder.UseAuthentication();
                appBuilder.UseAuthorization();

                appBuilder.UseEndpoints(endpoints =>
                {
                    endpoints.MapFallbackToPage("/_Host");
                });
            });

           
            app.MapHub<NotificationHub>("/notificationHub");
        }
#pragma warning restore S3776

        private static void RegisterStartupInitialization(WebApplication app)
        {
            var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStarted.Register(() =>
            {
                using var scope = app.Services.CreateScope();
                var appInitializer = scope.ServiceProvider.GetRequiredService<IAppInitializer>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                var auditSvc = scope.ServiceProvider.GetRequiredService<IWorkflowChangeAuditService>();
                var userMgmtSvc = scope.ServiceProvider.GetRequiredService<IUserManagementService>();

                appInitializer.InitializeAsync().GetAwaiter().GetResult();
                auditSvc.EnsureTableExistsAsync().GetAwaiter().GetResult();
                userMgmtSvc.EnsureSchemaAsync().GetAwaiter().GetResult();
                logger.LogInformation("Startup initialization completed for application tables, workflow audit, and dashboard user schema.");
            });
        }

        private static async Task PopulateRequestUserContextAsync(HttpContext context)
        {
            var userCtx = context.RequestServices.GetRequiredService<RequestUserContext>();
            userCtx.IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

            if (context.User.Identity?.IsAuthenticated == true)
            {
                PopulateFromPrincipal(userCtx, context.User);
                return;
            }

            var cookieResult = await context.AuthenticateAsync("Cookies");
            if (cookieResult.Succeeded && cookieResult.Principal is not null)
                PopulateFromCookiePrincipal(userCtx, cookieResult.Principal);
        }

        private static void PopulateFromPrincipal(RequestUserContext userCtx, ClaimsPrincipal principal)
        {
            userCtx.Username =
                principal.FindFirst("username")?.Value
                ?? principal.FindFirst("name")?.Value
                ?? principal.FindFirst(ClaimTypes.Name)?.Value
                ?? principal.Identity?.Name
                ?? "unknown";
            userCtx.UserId =
                principal.FindFirst("userId")?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private static void PopulateFromCookiePrincipal(RequestUserContext userCtx, ClaimsPrincipal principal)
        {
            userCtx.Username =
                principal.FindFirst(ClaimTypes.Name)?.Value
                ?? principal.Identity?.Name
                ?? "unknown";
            userCtx.UserId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private static bool IsFetchLogoutRequest(HttpRequest request)
        {
            var fetchMode = request.Headers["Sec-Fetch-Mode"].ToString();
            return request.Method == "POST"
                && (fetchMode == "same-origin"
                    || fetchMode == "cors"
                    || request.Headers.XRequestedWith == "XMLHttpRequest");
        }
    }
}
