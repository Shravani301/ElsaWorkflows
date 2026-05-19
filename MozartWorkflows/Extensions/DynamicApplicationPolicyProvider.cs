using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MozartWorkflows.Services.Interfaces;
using System.Data;
using System.Security.Claims;
using System.Text.Json;

namespace MozartWorkflows.Extensions
{
    public class DynamicApplicationPolicyProvider : IAuthorizationPolicyProvider
    {
        private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;
        private readonly IServiceProvider _serviceProvider;

        public DynamicApplicationPolicyProvider(
            IOptions<AuthorizationOptions> options,
            IServiceProvider serviceProvider)
        {
            _fallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
            _serviceProvider = serviceProvider;
        }

        public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            Console.WriteLine($"Policy requested: {policyName}");

            List<string>? roleList = null;

            if (policyName.TrimStart().StartsWith("\"App\""))
            {
                try
                {
                    string json = "{" + policyName + "}";
                    var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                    if (dict != null && dict.TryGetValue("App", out var parsedRoles))
                    {
                        roleList = parsedRoles;
                    }
                    else
                    {
                        Console.WriteLine("No 'App' key found in JSON policy string.");
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to parse JSON policy string: {ex.Message}");
                    return null;
                }
            }
            else if (policyName.StartsWith("App:"))
            {
                string roleSegment = policyName.Substring(4).Trim();
                try
                {
                    roleList = JsonSerializer.Deserialize<List<string>>(roleSegment);
                }
                catch (JsonException)
                {
                    roleList = roleSegment.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(r => r.Trim())
                        .ToList();
                }
            }

            if (roleList == null)
            {
                Console.WriteLine("Falling back to default policy provider.");
                return await _fallbackPolicyProvider.GetPolicyAsync(policyName);
            }

            return await BuildPolicyFromRoleListAsync(roleList);
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
            _fallbackPolicyProvider.GetDefaultPolicyAsync();

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
            _fallbackPolicyProvider.GetFallbackPolicyAsync();

        private async Task<AuthorizationPolicy?> BuildPolicyFromRoleListAsync(List<string> roleList)
        {
            if (roleList.Count == 0)
            {
                Console.WriteLine("No valid roles specified in policy.");
                return null;
            }

            Console.WriteLine("Parsed roles: " + string.Join(", ", roleList));
            var validRoles = new List<(string RoleName, int ApplicationId)>();

            foreach (var role in roleList)
            {
                var cleanedRole = role.Replace(" ", "");
                var record = await GetUserRoleByPolicyNameAsync(cleanedRole);

                if (record != null)
                    validRoles.Add(record.Value);
            }

            if (!validRoles.Any())
            {
                Console.WriteLine("No matching roles found in DB.");
                return null;
            }

            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                {
                    var roleClaim = context.User.FindFirst(ClaimTypes.Role)?.Value;
                    var appIdClaim = context.User.FindFirst("applicationId")?.Value;

                    Console.WriteLine($"Token role: {roleClaim}, applicationId: {appIdClaim}");

                    if (string.IsNullOrEmpty(roleClaim) || string.IsNullOrEmpty(appIdClaim))
                        return false;

                    if (!int.TryParse(appIdClaim, out var parsedAppId))
                        return false;

                    return validRoles.Any(r =>
                        string.Equals(r.RoleName, roleClaim, StringComparison.OrdinalIgnoreCase) &&
                        r.ApplicationId == parsedAppId);
                })
                .Build();
        }

        private Task<(string RoleName, int ApplicationId)?> GetUserRoleByPolicyNameAsync(string policyRoleName)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

                using var conn = dbFactory.CreateConnection();
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = SqlQueries.SqlServer.GetUserRoleByPolicyName;

                var param = cmd.CreateParameter();
                param.ParameterName = "@PolicyName";
                param.Value = policyRoleName;
                cmd.Parameters.Add(param);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return Task.FromResult<(string RoleName, int ApplicationId)?>(
                        (reader.GetString(reader.GetOrdinal("Name")),
                        reader.GetInt32(reader.GetOrdinal("ApplicationId"))));
                }

                return Task.FromResult<(string RoleName, int ApplicationId)?>(null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB ERROR] Role lookup failed: {ex.Message}");
                return Task.FromResult<(string RoleName, int ApplicationId)?>(null);
            }
        }
    }
}
