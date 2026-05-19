using Microsoft.Extensions.Caching.Memory;
using MozartWorkflows;
using System.Data;
using MozartWorkflows.Services.Interfaces;

namespace MozartWorkflows.Services
{
    public class ConfigManager
    {
        private readonly IMemoryCache _cache;
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public ConfigManager(IMemoryCache cache, IDbConnectionFactory dbConnectionFactory)
        {
            _cache = cache;
            _dbConnectionFactory = dbConnectionFactory;
        }

        public string GetConfigurationItem(string configurationName)
        {
            // ✅ First check in cache
            if (_cache.TryGetValue(configurationName, out string? cachedValue) && cachedValue is not null)
                return cachedValue;

            try
            {
                using var connection = _dbConnectionFactory.CreateConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = SqlQueries.SqlServer.GetConfigurationItem;

                var param = command.CreateParameter();
                param.ParameterName = "@Name";
                param.Value = configurationName;
                command.Parameters.Add(param);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    var jsonValue = reader.GetString(0);

                    if (!string.IsNullOrWhiteSpace(jsonValue))
                    {
                        _cache.Set(configurationName, jsonValue, TimeSpan.FromMinutes(15)); // cache duration
                        Console.BackgroundColor = ConsoleColor.Blue;
                        Console.WriteLine(jsonValue);
                        Console.ResetColor();
                        return jsonValue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error fetching configuration: {ex.Message}");
            }

            // If not found, return empty or fallback default
            return "";
        }
    }
}
