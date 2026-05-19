using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MozartWorkflows.Services.Interfaces;
using System.Security.Claims;

namespace MozartWorkflows.Controllers;

[ApiController]
[Route("api/user-mgmt")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class UserManagementController : ControllerBase
{
    private const string AdminAccessRequired = "Admin access required.";
    private readonly IUserManagementService _userMgmt;

    public UserManagementController(IUserManagementService userMgmt)
        => _userMgmt = userMgmt;

    private int CurrentUserId =>
        int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    private bool CurrentIsAdmin =>
        User.FindFirst("IsAdmin")?.Value == "true";

    public sealed record UserProfileResponse(string Username, string UserId, bool IsAdmin);

    // GET /api/user-mgmt/profile
    [HttpGet("profile")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    public IActionResult GetProfile()
    {
        var username = User.FindFirst(ClaimTypes.Name)?.Value ?? "";
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0";
        var isAdmin = User.FindFirst("IsAdmin")?.Value == "true";
        return Ok(new UserProfileResponse(username, userId, isAdmin));
    }

    // POST /api/user-mgmt/change-password
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        if (req.NewPassword != req.ConfirmPassword)
            return BadRequest(new { message = "New password and confirmation do not match." });

        if (string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(new { message = "New password cannot be empty." });

        var success = await _userMgmt.ChangePasswordAsync(CurrentUserId, req.CurrentPassword, req.NewPassword);
        if (!success)
            return BadRequest(new { message = "Current password is incorrect." });

        return Ok(new { message = "Password changed successfully." });
    }

    // GET /api/user-mgmt/users
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        if (!CurrentIsAdmin)
            return StatusCode(403, new { message = AdminAccessRequired });

        var users = await _userMgmt.GetAllAsync();
        return Ok(users);
    }

    // POST /api/user-mgmt/users
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        if (!CurrentIsAdmin)
            return StatusCode(403, new { message = AdminAccessRequired });

        if (string.IsNullOrWhiteSpace(req.Username))
            return BadRequest(new { message = "Username is required." });

        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { message = "Email is required." });

        var user = await _userMgmt.CreateAsync(req.Username, req.Email, req.IsAdmin ?? false);
        return Ok(user);
    }

    // PUT /api/user-mgmt/users/{id}/toggle-active
    [HttpPut("users/{id}/toggle-active")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        if (!CurrentIsAdmin)
            return StatusCode(403, new { message = AdminAccessRequired });

        if (id == CurrentUserId)
            return BadRequest(new { message = "You cannot toggle your own account status." });

        var user = await _userMgmt.GetByIdAsync(id);
        if (user == null) return NotFound(new { message = "User not found." });

        await _userMgmt.SetActiveAsync(id, !user.Active);
        return Ok(new { message = "User status updated.", active = !user.Active });
    }

    // PUT /api/user-mgmt/users/{id}/toggle-admin
    [HttpPut("users/{id}/toggle-admin")]
    public async Task<IActionResult> ToggleAdmin(int id)
    {
        if (!CurrentIsAdmin)
            return StatusCode(403, new { message = AdminAccessRequired });

        if (id == CurrentUserId)
            return BadRequest(new { message = "You cannot change your own admin status." });

        var user = await _userMgmt.GetByIdAsync(id);
        if (user == null) return NotFound(new { message = "User not found." });

        await _userMgmt.SetAdminAsync(id, !user.IsAdmin);
        return Ok(new { message = "Admin status updated.", isAdmin = !user.IsAdmin });
    }

    // POST /api/user-mgmt/users/{id}/reset-password
    [HttpPost("users/{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id)
    {
        if (!CurrentIsAdmin)
            return StatusCode(403, new { message = AdminAccessRequired });

        await _userMgmt.ResetPasswordAsync(id);
        return Ok(new { message = "Password reset to default." });
    }

    // DELETE /api/user-mgmt/users/{id}
    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        if (!CurrentIsAdmin)
            return StatusCode(403, new { message = AdminAccessRequired });

        if (id == CurrentUserId)
            return BadRequest(new { message = "You cannot delete your own account." });

        await _userMgmt.DeleteAsync(id);
        return Ok(new { message = "User deleted." });
    }
}

public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);
public record CreateUserRequest(string Username, string Email, bool? IsAdmin);
