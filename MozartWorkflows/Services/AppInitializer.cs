using MozartWorkflows.Services;
using MozartWorkflows.Services.Interfaces;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MozartWorkflows.Services
{
    public class AppInitializer : IAppInitializer
    {
        private readonly ILogger<AppInitializer> _logger;
        private readonly IRuleService _ruleService;
        private readonly IConfiguration _configuration;
        private readonly IDbService _dbService;

        public AppInitializer(ILogger<AppInitializer> logger, IRuleService ruleService, IConfiguration configuration, IDbService dbService)
        {
            _ruleService = ruleService;
            _logger = logger;
            _configuration = configuration;
            _dbService = dbService;
        }

        public async Task InitializeAsync()
        {
            try
            {
                Console.WriteLine("Initializing application...");

                await _ruleService.UpdateRulesInRuleEngine();
                await LoadTables();

                Console.WriteLine("Application initialized successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while initializing the application.");
            }
        }

        public Task LoadTables()
        {
            try
            {
                var preloadTables = _configuration.GetSection("preloadTables").Get<string[]>();
                if (preloadTables == null) return Task.CompletedTask;

                foreach (var table in preloadTables)
                {
                    CacheService.Set($"TABLE_{table}", BuildJsonArray(table));
                }

                _logger.LogInformation("Preloading of specified tables is complete.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during table preloading.");
            }

            return Task.CompletedTask;
        }

        private JsonArray BuildJsonArray(string table)
        {
            string query = $"SELECT * FROM {table}";
            List<object> data = _dbService.QueryList<object>(query);
            JsonArray jsonArray = new JsonArray();

            foreach (var item in data.OfType<IDictionary<string, object>>())
            {
                jsonArray.Add(BuildJsonObject(item));
            }

            return jsonArray;
        }

        private static JsonObject BuildJsonObject(IDictionary<string, object> dictionary)
        {
            var jsonObject = new JsonObject();

            foreach (var kvp in dictionary)
            {
                jsonObject[kvp.Key] = kvp.Value == null ? null : JsonValue.Create(kvp.Value);
            }

            return jsonObject;
        }

    }
}
