using Microsoft.Extensions.Options;
using MozartWorkflows.Models;
using MozartWorkflows.Services.Interfaces;

namespace MozartWorkflows.Services
{
    public class AuditWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<BatchConfig> _batchConfig;

        public AuditWorker(IServiceScopeFactory scopeFactory, IOptionsMonitor<BatchConfig> batchConfig)
        {
            _scopeFactory = scopeFactory;
            _batchConfig = batchConfig;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var config = _batchConfig.CurrentValue;
            var batch = new List<RuleExecutionAudit>(config.BatchSize);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    config = _batchConfig.CurrentValue;

                    var readTask = AuditQueue.Queue.Reader.ReadAsync(stoppingToken).AsTask();
                    var delayTask = Task.Delay(config.MaxWaitMs, stoppingToken);

                    var completed = await Task.WhenAny(readTask, delayTask);

                    if (completed == delayTask)
                    {
                        if (batch.Count > 0)
                            await FlushBatchAsync(batch);

                        continue;
                    }

                    // ✅ readTask.Result is RuleExecutionAudit (ONLY if AuditQueue is Channel<RuleExecutionAudit>)
                    batch.Add(readTask.Result);

                    while (batch.Count < config.BatchSize && AuditQueue.Queue.Reader.TryRead(out var audit))
                        batch.Add(audit);

                    if (batch.Count >= config.BatchSize)
                        await FlushBatchAsync(batch);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // graceful shutdown
            }
            finally
            {
                if (batch.Count > 0)
                    await FlushBatchAsync(batch);
            }
        }

        private async Task FlushBatchAsync(List<RuleExecutionAudit> batch)
        {
            if (batch.Count == 0) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
                await auditService.BulkInsertRuleAuditsAsync(batch);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuditWorker:FlushBatch] ERROR: {ex}");
                throw;
            }
            finally
            {
                batch.Clear();
            }
        }
    }
}
