using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using Microsoft.Extensions.Caching.Memory;

namespace MozartWorkflows.Elsa.Activities
{
    [Activity(
    Category = "MyActivity",
    Description = "Set Cache With Data."
    )]
    public class SetCacheData : Activity
    {
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _config;
        public SetCacheData(IMemoryCache cache, IConfiguration config)
        {
            _cache = cache;
            _config = config;
        }
        [ActivityInput(Hint = "Cache key", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid })]
        public string CacheKey { get; set; } = default!;
        [ActivityInput(Hint = "Data Which you want to Cache", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid })]
        public Object CacheData { get; set; } = default!;
        protected override ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
        {
            if (!_cache.TryGetValue(CacheKey, out _))
            {
                var cacheEntryOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_config.GetValue<int>("Cache:ExpiryTime"))
                };
                _cache.Set(CacheKey, CacheData, cacheEntryOptions);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Cache Set With Data.");
                Console.ResetColor();
            }
            return new ValueTask<IActivityExecutionResult>(Done());
        }
    }
}
