using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Elsa.Activities.Http.Services;
using Elsa.Activities.Sql.Extensions;
using Elsa.Persistence.EntityFramework.Core.Extensions;
using Elsa.Persistence.EntityFramework.SqlServer;
using Elsa.Persistence.EntityFramework.PostgreSql;
using Elsa.Persistence.EntityFramework.MySql;
using Elsa.Persistence.EntityFramework.Oracle;
using Elsa.Retention.Extensions;
using NodaTime;
using Elsa.Runtime;
using MozartWorkflows.Services;
using Elsa;
using MozartWorkflows.Elsa.Activities;

namespace MozartWorkflows.Extensions
{
    public static class RegisterElsa
    {
        public static IServiceCollection AddElsa(this IServiceCollection service, IConfiguration config)
        {
            var connectionString = config.GetConnectionString("connectionString");
            var elsaBaseUrl = config.GetValue<string>("Elsa:Server:BaseUrl");
            var provider = config["DatabaseProvider"] ?? "SqlServer";

            if (connectionString != null && elsaBaseUrl != null)
            {
                service.AddElsa(elsa =>
                {
                    // Set persistence based on configured DB provider
                    elsa.UseEntityFrameworkPersistence(ef =>
                    {
                        switch (provider)
                        {
                            case "SqlServer":
                                ef.UseSqlServer(connectionString);
                                break;
                            case "PostgreSql":
                                ef.UsePostgreSql(connectionString);
                                break;
                            case "MySql":
                                ef.UseMySql(connectionString);
                                break;
                            case "Oracle":
                                ef.UseOracle(connectionString);
                                break;
                            default:
                                throw new NotSupportedException($"Database provider '{provider}' is not supported.");
                        }
                    }, autoRunMigrations: true);

                    elsa.AddConsoleActivities();

                    // ? CRITICAL: Configure HTTP Activities with custom handler
                    elsa.AddHttpActivities(httpOptions =>
                    {
                        httpOptions.BaseUrl = new Uri(elsaBaseUrl);

                        // This tells Elsa to use JWT for API endpoints
                        httpOptions.HttpEndpointAuthorizationHandlerFactory =
                            ActivatorUtilities.GetServiceOrCreateInstance<JwtForApiAuthorizationHandler>;
                    });

                    elsa.AddQuartzTemporalActivities();
                    elsa.AddJavaScriptActivities();
                    elsa.AddSqlServerActivities();
                    elsa.AddWorkflowsFrom<Program>();
                    elsa.AddActivitiesFrom<Program>();
                    elsa.AddActivity<GenerateOtpActivity>();
                });
            }

            service.AddStartupTask<UpdateRouteTableWithBookmarks>();
            service.AddElsaApiEndpoints();

            service.AddRetentionServices(options =>
            {
                options.SweepInterval = Duration.FromMinutes(config.GetValue<int>("Elsa:RetentionServices:SweepInterval"));
                options.TimeToLive = Duration.FromMinutes(config.GetValue<int>("Elsa:RetentionServices:TimeToLive"));
            });

            service.AddNotificationHandlersFrom<Program>();
            return service;
        }
    }
}