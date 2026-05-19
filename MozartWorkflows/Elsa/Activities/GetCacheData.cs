using Elsa.ActivityResults;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Services;
using Elsa.Services.Models;
using Microsoft.Extensions.Caching.Memory;

namespace MozartWorkflows.Elsa.Activities;

[Activity(
    Category = "MyActivity",
    Description = "Get Data From Cache."
)]
public class GetCacheData : Activity
{
    private readonly IMemoryCache _cache;
    public GetCacheData(IMemoryCache cache)
    {
        _cache = cache;
    }
    [ActivityInput(Hint = "Cache key", SupportedSyntaxes = new[] { SyntaxNames.JavaScript, SyntaxNames.Liquid })]
    public string CacheKey { get; set; } = default!;
    [ActivityOutput] public Object? Output { get; set; }
    protected override ValueTask<IActivityExecutionResult> OnExecuteAsync(ActivityExecutionContext context)
    {
        if (_cache.TryGetValue(CacheKey, out var cachedData))
        {
            Output = cachedData;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Data Get From Cache.");
            Console.ResetColor();
        }
        return new ValueTask<IActivityExecutionResult>(Done());
    }
}

