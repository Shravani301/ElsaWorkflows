using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MozartWorkflows;
using NLog;
using System.Data;
using System.Security.Claims;
using MozartWorkflows.Services;
using MozartWorkflows.Services.Interfaces;
using MozartWorkflows.Dtos;

namespace MozartWorkflows.Pages
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly ISessionService _sessionService;
        private readonly IUserManagementService _userManagementService;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public LoginModel(
            IConfiguration config,
            IDbConnectionFactory dbConnectionFactory,
            ISessionService sessionService,
            IUserManagementService userManagementService)
        {
            _dbConnectionFactory = dbConnectionFactory;
            _sessionService = sessionService;
            _userManagementService = userManagementService;
        }

        [BindProperty]
        public string? Username { get; set; }

        [BindProperty]
        public string? Password { get; set; }

        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            ErrorMessage = TempData["ErrorMessage"] as string;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            _logger.Info("🚀 Login form submitted");
            _logger.Info($"Username/Email: {Username}");

            var ipAddress = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                            ?? HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

            LoginUserInfo? loginUser = null;
            string? failureReason = null;

            try
            {
                await _userManagementService.EnsureSchemaAsync();

                using var conn = _dbConnectionFactory.CreateConnection();
                conn.Open();

                var prefix = SqlQueries.ParameterPrefix(conn);
                _logger.Debug($"🔍 Detected DB Type: {conn.GetType().Name}");
                _logger.Debug($"🔍 Using parameter prefix: {prefix}");

                loginUser = LoadLoginUser(conn, prefix);

                if (loginUser?.StoredHash != null)
                {
                    bool isValid = PasswordHasher.ValidatePassword(Password ?? "", loginUser.Salt, loginUser.StoredHash);

                    if (isValid)
                    {
                        _logger.Info($"✅ Login successful. User: {Username}, IP: {ipAddress}, Agent: {userAgent}");

                        string? sessionId = await CreateSessionIdAsync(loginUser.UserId, userAgent, ipAddress);

                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.Name, loginUser.DbUsername ?? Username ?? string.Empty),
                            new Claim(ClaimTypes.NameIdentifier, loginUser.UserId.ToString()),
                            new Claim("userId", loginUser.UserId.ToString()),
                            new Claim("IsAdmin", loginUser.IsAdmin ? "true" : "false")
                        };

                        if (!string.IsNullOrEmpty(sessionId))
                            claims.Add(new Claim("sessionId", sessionId));

                        var identity = new ClaimsIdentity(claims, "Cookies");
                        var principal = new ClaimsPrincipal(identity);

                        await HttpContext.SignInAsync("Cookies", principal, new AuthenticationProperties
                        {
                            IsPersistent = false,   // session cookie — cleared when browser closes
                            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                        });

                        await InsertLoginAudit(conn, loginUser.UserId, Username, ipAddress, userAgent, true, null);
                        return Redirect("/");
                    }
                    else
                    {
                        failureReason = "Invalid password";
                        _logger.Warn("❌ Password mismatch.");
                    }
                }
                else
                {
                    failureReason = "User not found";
                }

                await InsertLoginAudit(conn, loginUser?.UserId, Username, ipAddress, userAgent, false, failureReason);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"❌ Login error: {ex.Message}");
                failureReason = "Server error";
            }

            TempData["ErrorMessage"] = failureReason ?? "Login failed";
            return RedirectToPage();
        }

        private LoginUserInfo? LoadLoginUser(IDbConnection conn, string prefix)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SqlQueries.GetLoginQuery(conn);

            var inputParam = cmd.CreateParameter();
            inputParam.ParameterName = $"{prefix}Input";
            inputParam.Value = Username;
            cmd.Parameters.Add(inputParam);

            _logger.Debug("🔎 Executing SQL Command:");
            _logger.Debug(cmd.CommandText);
            foreach (IDbDataParameter p in cmd.Parameters)
            {
                _logger.Debug($"➡️ Param: {p.ParameterName} = {p.Value}");
            }

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                _logger.Warn("⚠️ No matching user found.");
                return null;
            }

            _logger.Info("✅ User record found.");
            return new LoginUserInfo(
                Convert.ToInt32(reader["Id"]),
                reader["Username"]?.ToString(),
                reader["PasswordHash"]?.ToString(),
                reader["Salt"]?.ToString() ?? "",
                reader["IsAdmin"] != DBNull.Value && Convert.ToBoolean(reader["IsAdmin"]));
        }

        private async Task<string?> CreateSessionIdAsync(int userId, string? userAgent, string? ipAddress)
        {
            try
            {
                var sessionResponse = await _sessionService.CreateSession(userId.ToString(), userAgent, ipAddress);
                return sessionResponse?.SessionId;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "⚠️ Session creation failed — login continues without session tracking.");
                return null;
            }
        }

        private static async Task InsertLoginAudit(IDbConnection conn, int? userId, string? input, string? ip, string? agent, bool isSuccess, string? reason)
        {
            var prefix = SqlQueries.ParameterPrefix(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SqlQueries.GetLoginAuditInsertQuery(conn);

            _logger.Info("📝 Inserting login audit record...");
            _logger.Debug(cmd.CommandText);

            var p1 = cmd.CreateParameter(); p1.ParameterName = $"{prefix}UserId"; p1.Value = (object?)userId ?? DBNull.Value;
            var p2 = cmd.CreateParameter(); p2.ParameterName = $"{prefix}Input"; p2.Value = input;
            var p3 = cmd.CreateParameter(); p3.ParameterName = $"{prefix}IP"; p3.Value = ip ?? (object)DBNull.Value;
            var p4 = cmd.CreateParameter(); p4.ParameterName = $"{prefix}Agent"; p4.Value = agent ?? (object)DBNull.Value;
            var p5 = cmd.CreateParameter(); p5.ParameterName = $"{prefix}Success"; p5.Value = isSuccess;
            var p6 = cmd.CreateParameter(); p6.ParameterName = $"{prefix}Reason"; p6.Value = (object?)reason ?? DBNull.Value;

            cmd.Parameters.Add(p1); cmd.Parameters.Add(p2); cmd.Parameters.Add(p3);
            cmd.Parameters.Add(p4); cmd.Parameters.Add(p5); cmd.Parameters.Add(p6);

            foreach (IDbDataParameter p in cmd.Parameters)
            {
                _logger.Debug($"➡️ Audit Param: {p.ParameterName} = {p.Value}");
            }

            await Task.Run(() => cmd.ExecuteNonQuery());

            _logger.Info($"✅ Audit record inserted for user: {input}, success: {isSuccess}");
        }

        private sealed record LoginUserInfo(
            int UserId,
            string? DbUsername,
            string? StoredHash,
            string Salt,
            bool IsAdmin);
    }
}
