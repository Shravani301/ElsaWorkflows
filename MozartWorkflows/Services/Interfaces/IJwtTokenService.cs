using MozartWorkflows.Dtos;

namespace MozartWorkflows.Services.Interfaces
{
    public interface IJwtTokenService
    {
        // Method to create a token, UserId is now string
        AuthTokens CreateToken(string userId, string role, int applicationId, string applicationName);

        // Method to create a reset token, UserId is still string
        string CreateResetToken(string userId, string email);

        // Method to validate a reset token, returns userId and email as strings
        bool ValidateResetToken(string token, out string? userId, out string? email);

        // Method to refresh the access token using refresh token
        AuthTokens RefreshToken(string refreshToken); // Add this if you plan to use refresh token functionality
    }
}
