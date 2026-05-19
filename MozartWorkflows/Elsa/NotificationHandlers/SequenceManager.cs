using Elsa.Scripting.JavaScript.Messages;
using MediatR;
using Microsoft.Extensions.Configuration;
using MozartWorkflows.Dtos;
using MozartWorkflows.Services;
using MozartWorkflows.Services.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace MozartWorkflows.Elsa.NotificationHandlers
{
    public class SequenceManager : INotificationHandler<EvaluatingJavaScriptExpression>
    {
        private readonly IConfiguration _configuration;
        private readonly IJwtTokenService _token;
        private readonly PasswordHasher _passwordHasher;
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public SequenceManager(IConfiguration configuration, IJwtTokenService token, IDbConnectionFactory dbConnectionFactory)
        {
            _configuration = configuration;
            _passwordHasher = new PasswordHasher(configuration);
            _token = token;
            _dbConnectionFactory = dbConnectionFactory;
        }

        public Task Handle(EvaluatingJavaScriptExpression notification, CancellationToken cancellationToken)
        {
            var engine = notification.Engine;
            engine.SetValue("GetSequence", (Func<int>)(GetNextSequence));
            engine.SetValue("GetJwtToken", (Func<string, string, int, string, AuthTokens>)(GetJwtToken));
            engine.SetValue("GetRefreshJwtToken", (Func<string, AuthTokens>)(GetRefreshJwtToken));
            engine.SetValue("VerifyPassword", (Func<string, string, bool>)(VerifyPassword));
            engine.SetValue("HashPassword", (Func<string, string>)(HashPassword));
            engine.SetValue("CreatePasswordHash", (Func<string, string, string>)(CreatePasswordHash));
            engine.SetValue("ValidatePassword", (Func<string, string, string, bool>)(ValidatePassword));
            engine.SetValue("CreateResetToken", (Func<string, string, string>)((userId, email) =>
            {
                return _token.CreateResetToken(userId, email);
            }));
            engine.SetValue("ValidateResetToken", (Func<string, object>)(token =>
            {
                if (_token.ValidateResetToken(token, out var userId, out var email))
                {
                    return new { success = true, userId, email };
                }
                return new { success = false };
            }));
            engine.SetValue("CreateSaltKey", (Func<int, string>)((size) => {
                return PasswordHasher.CreateSaltKey(size);
            }));

            return Task.CompletedTask;
        }

        // ✅ Refactored: Provider-specific SQL
        private int GetNextSequence()
        {
            var provider = _configuration["DatabaseProvider"] ?? "SqlServer";
            var sqlQuery = provider switch
            {
                "SqlServer" => "SELECT NEXT VALUE FOR CaseID",
                "PostgreSql" => "SELECT nextval('\"CaseID\"')",
                "Oracle" => "SELECT CaseID.NEXTVAL FROM DUAL",
                "MySql" => @"INSERT INTO CaseIDSequenceTable () VALUES(); SELECT LAST_INSERT_ID();",
                _ => throw new NotSupportedException($"Provider '{provider}' is not supported for sequence generation.")
            };

            using var connection = _dbConnectionFactory.CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = sqlQuery;

            // ✅ MySQL requires handling multiple result sets
            if (provider == "MySql")
            {
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    _ = reader.FieldCount;
                reader.NextResult(); // go to SELECT LAST_INSERT_ID()
                reader.Read();
                return Convert.ToInt32(reader[0]);
            }
            else
            {
                var result = command.ExecuteScalar();
                return Convert.ToInt32(result);
            }
        }

        // ✅ Updated method to handle both AccessToken and RefreshToken
        private AuthTokens GetJwtToken(string userId, string role, int applicationId, string applicationName)
        {
            // Create the token using the updated CreateToken method
            var token = _token.CreateToken(userId, role, applicationId, applicationName);

            // Return the AuthTokens object that contains both AccessToken and RefreshToken
            return token;  // Returning the AuthTokens object directly
        }
        private AuthTokens GetRefreshJwtToken(string refreshToken)
        {
            // Create the token using the updated CreateToken method
            var token = _token.RefreshToken(refreshToken);

            // Return the AuthTokens object that contains both AccessToken and RefreshToken
            return token;  // Returning the AuthTokens object directly
        }

        private bool VerifyPassword(string password, string base64Hash) =>
            _passwordHasher.VerifyPassword(password, base64Hash);

        private string HashPassword(string password) =>
            _passwordHasher.HashPassword(password);

        private static string CreatePasswordHash(string password, string salt) =>
            PasswordHasher.CreatePasswordHash(password, salt);

        private static bool ValidatePassword(string password, string salt, string hashed) =>
            PasswordHasher.ValidatePassword(password, salt, hashed);
    }

}
