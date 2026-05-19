using MozartWorkflows.Services.Interfaces;

namespace MozartWorkflows.Services
{
    public class EventSyncJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EventSyncJob> _logger;

        public EventSyncJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<EventSyncJob> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var enabled = _configuration.GetValue<bool>("TeamsSync:Enabled");
            if (!enabled)
            {
                _logger.LogInformation("TeamsSync is disabled. Skipping EventSyncJob.");
                return;
            }

            var userId = _configuration.GetValue<string>("TeamsSync:UserId");
            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger.LogWarning("TeamsSync:UserId is not configured. Skipping EventSyncJob.");
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ITeamsMeetingService>();
                await service.SyncEventsAsync(userId);
                _logger.LogInformation("EventSyncJob completed for user {UserId}.", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EventSyncJob failed for user {UserId}. Host will continue running.", userId);
            }
        }
    }
}
