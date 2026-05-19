using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using MozartWorkflows;
using MozartWorkflows.Services;
using MozartWorkflows.Services.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace MozartWorkflows.Controllers;

/// <summary>
/// Public authentication endpoints that do NOT require a logged-in user.
/// </summary>
[ApiController]
[Route("api/auth")]
#pragma warning disable S6960
public class AuthController : ControllerBase
{
    private readonly IUserManagementService _userMgmt;
    private readonly IDbConnectionFactory _dbFactory;
    private readonly IConfiguration _config;

    public AuthController(
        IUserManagementService userMgmt,
        IDbConnectionFactory dbFactory,
        IConfiguration config)
    {
        _userMgmt = userMgmt;
        _dbFactory = dbFactory;
        _config = config;
    }

    /// <summary>
    /// JWT login for the Elsa dashboard UI.
    /// Returns a signed JWT AND sets an HttpOnly cookie so Elsa Studio's /elsa-api/*
    /// calls (which carry no Authorization header) can still be attributed to the
    /// logged-in user via the cookie fallback in RequestUserContext middleware.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] DashboardLoginRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Username and password are required." });

        try
        {
            await _userMgmt.EnsureSchemaAsync();

            using var conn = _dbFactory.CreateConnection();
            conn.Open();

            var prefix = SqlQueries.ParameterPrefix(conn);
            var sql    = SqlQueries.GetLoginQuery(conn);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var p = cmd.CreateParameter(); p.ParameterName = $"{prefix}Input"; p.Value = req.Username.Trim();
            cmd.Parameters.Add(p);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return Unauthorized(new { message = "Invalid username or password." });

            var userId   = Convert.ToInt32(reader["Id"]);
            var username = reader["Username"]?.ToString() ?? req.Username;
            var hash     = reader["PasswordHash"]?.ToString() ?? "";
            var salt     = reader["Salt"]?.ToString() ?? "";
            var isAdmin  = reader["IsAdmin"] != DBNull.Value && Convert.ToBoolean(reader["IsAdmin"]);
            reader.Close();

            if (!PasswordHasher.ValidatePassword(req.Password, salt, hash))
                return Unauthorized(new { message = "Invalid username or password." });

            var secretKey    = _config.GetValue<string>("jwt:Key")!;
            var expiryHours  = _config.GetValue<int>("jwt:DashboardExpiry", 8);
            var signingKey   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

            var claims = new List<Claim>
            {
                new Claim("userId",                             userId.ToString()),
                new Claim(ClaimTypes.NameIdentifier,            userId.ToString()),
                new Claim(ClaimTypes.Name,                      username),
                new Claim("username",                           username),
                new Claim("IsAdmin",                            isAdmin ? "true" : "false"),
                new Claim("isAdmin",                            isAdmin ? "true" : "false"),
            };

            var jwtToken = new JwtSecurityToken(
                claims:             claims,
                expires:            DateTime.UtcNow.AddHours(expiryHours),
                signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

            // Set an HttpOnly cookie with the same identity so the browser includes it
            // automatically on /elsa-api/* requests made by Elsa Studio (which does not
            // forward the localStorage JWT). The middleware cookie fallback then populates
            // RequestUserContext.Username, letting WorkflowChangeAudit record the real user.
            var cookiePrincipal = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(claims, "Cookies"));
            await HttpContext.SignInAsync("Cookies", cookiePrincipal, new AuthenticationProperties
            {
                IsPersistent  = false,
                ExpiresUtc    = DateTimeOffset.UtcNow.AddHours(expiryHours)
            });

            return Ok(new
            {
                token     = new JwtSecurityTokenHandler().WriteToken(jwtToken),
                username,
                userId,
                isAdmin,
                expiresAt = DateTime.UtcNow.AddHours(expiryHours)
            });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Login failed. Please try again." });
        }
    }

    /// <summary>
    /// Self-service forgot-password: resets the user's password to the system default.
    /// Always returns a generic message regardless of whether the username was found,
    /// to avoid leaking which usernames exist.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Username))
            return BadRequest(new { message = "Username is required." });

        // Intentionally ignore the boolean return — do not leak whether the user exists
        await _userMgmt.EnsureSchemaAsync();
        await _userMgmt.ResetPasswordByUsernameAsync(req.Username.Trim());

        return Ok(new
        {
            message = "If that username exists and is active, the password has been reset to the default (Password123). " +
                      "Please log in and change your password immediately."
        });
    }
}
#pragma warning restore S6960

public record DashboardLoginRequest(string Username, string Password);
public record ForgotPasswordRequest(string Username);
