using System.Text;


namespace OcelotAPI.Gateway;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    public RequestLoggingMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context)
    {
        // --- request -------------------------------------------------------
        var requestBody = await ReadRequestBodyAsync(context.Request);
        Console.WriteLine($"🚀 {context.Request.Method} {context.Request.Path}");
        Console.WriteLine($"   ⬇︎ {requestBody}");

        // swap response body with a memory stream so we can read it
        var originalBody = context.Response.Body;
        await using var mem = new MemoryStream();
        context.Response.Body = mem;

        var start = DateTime.UtcNow;
        await _next(context);              //  🔻 gateway / downstream
        var end = DateTime.UtcNow;

        // --- response ------------------------------------------------------
        mem.Position = 0;
        var responseBody = await new StreamReader(mem).ReadToEndAsync();
        mem.Position = 0;
        await mem.CopyToAsync(originalBody);

        Console.WriteLine($"   ⬆︎ {context.Response.StatusCode}");
        Console.WriteLine($"   ⬆︎ {responseBody}");

        await PersistAsync(context, requestBody, responseBody, start, end);
    }

    private static async Task<string> ReadRequestBodyAsync(HttpRequest req)
    {
        req.EnableBuffering();
        using var r = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: true);
        var body = await r.ReadToEndAsync();
        req.Body.Position = 0;
        return string.IsNullOrWhiteSpace(body) ? "{}" : body;
    }

    private static Task PersistAsync(
        HttpContext ctx, string reqBody, string respBody,
        DateTime start, DateTime end)
    {
        var repo = ctx.RequestServices.GetRequiredService<LogRepository>();
        return repo.LogRequestAsync(new RequestLogEntry(
            Path: ctx.Request.Path,
            Method: ctx.Request.Method,
            RequestBody: reqBody,
            ResponseBody: respBody,
            StatusCode: ctx.Response.StatusCode,
            ElapsedTimeMs: (int)(end - start).TotalMilliseconds,
            StartTime: start,
            EndTime: end));
    }
}
