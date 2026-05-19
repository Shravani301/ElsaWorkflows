using Microsoft.Extensions.Caching.Memory;
using System.Text.Json.Nodes;

namespace MozartWorkflows.Services
{

    public static class CacheService
    {
        private static readonly MemoryCache _cache = new(new MemoryCacheOptions());

        public static JsonNode Set(string key, JsonNode value)
        {
            _cache.Set(key, value);
            return value;
        }

        public static object Set(string key, object value)
        {
            _cache.Set(key, value);
            return value;
        }

        public static JsonNode? Get(string key)
        {
            _cache.TryGetValue(key, out JsonNode? value);
            return value;
        }

        public static object? GetAny(string key)
        {
            _cache.TryGetValue(key, out object? value);
            return value;
        }

        // ======== DECIMAL-SPECIFIC METHODS ==========
        public static decimal SetDecimal(string key, decimal value)
        {
            var rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
            _cache.Set(key, rounded);
            return rounded;
        }


        public static decimal GetDecimal(string key)
        {
            if (_cache.TryGetValue(key, out decimal value))
            {
                return value;
            }

            // fallback: try as object if stored differently
            if (_cache.TryGetValue(key, out object? obj) && obj is not null)
            {
                try
                {
                    return Convert.ToDecimal(obj);
                }
                catch
                {
                    Console.WriteLine($"[CacheService] WARN: Failed to convert {obj} to decimal.");
                }
            }

            return 0M;
        }
        public static JsonNode? Remove(string key)
        {
            JsonNode? value = Get(key);
            _cache.Remove(key);
            return value;
        }

        public static bool Exists(string key)
        {
            return _cache.TryGetValue(key, out _);
        }

        public static string GenerateRandomKey()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static void Clear()
        {
            _cache.Compact(1.0);
        }
    }
}
