using MozartWorkflows.Extensions;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Provider.Polly;
using OcelotAPI.Gateway;
using Microsoft.OpenApi.Models;


var builder = WebApplication.CreateBuilder(args);

var httpsPort = Environment.GetEnvironmentVariable("GATEWAY_HTTPS_PORT") ?? "6001";
builder.WebHost.UseUrls($"https://*:{httpsPort}");

builder.Configuration
       .SetBasePath(Directory.GetCurrentDirectory())                // optional but explicit
       .AddJsonFile("oce.appsettings.json", optional: false, reloadOnChange: true)
       .AddJsonFile($"oce.appsettings.{builder.Environment.EnvironmentName}.json",
                    optional: true, reloadOnChange: true)          // e.g. oce.appsettings.Development.json
       .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
       .AddEnvironmentVariables();

builder.Services.AddOcelot(builder.Configuration).AddPolly();

// Add Swagger services
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Ocelot API Gateway",
        Version = "v1",
        Description = "API Gateway with Ocelot"
    });
});

builder.Services.AddJwtAuthentication(builder.Configuration);

builder.Services.AddSingleton<LogRepository>();
#pragma warning disable S5122
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});
#pragma warning restore S5122
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Ocelot API Gateway V1");
    c.RoutePrefix = string.Empty; // Set Swagger UI to be the root page, or you can change the route prefix if you prefer
});

app.UseCors("AllowAll");

app.UseAuthentication();

app.UseMiddleware<RequestLoggingMiddleware>();

await app.UseOcelot();

await app.RunAsync();
