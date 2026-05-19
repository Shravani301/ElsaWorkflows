using Microsoft.Extensions.Options;
using MozartWorkflows.Services.Interfaces;

namespace MozartWorkflows.Models
{
    public class WorkflowAuditWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<BatchConfig> _batchConfig;

        public WorkflowAuditWorker(IServiceScopeFactory scopeFactory, IOptionsMonitor<BatchConfig> batchConfig)
        {
            _scopeFactory = scopeFactory;
            _batchConfig = batchConfig;
        }

#pragma warning disable S3776
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            List<WorkflowExecutionAudit> insertBatch = new();
            List<WorkflowExecutionAudit> updateBatch = new();

            var lastFlush = DateTime.UtcNow;

            while (!stoppingToken.IsCancellationRequested)
            {
                var config = _batchConfig.CurrentValue;

                // Wait for data OR timeout (100 ms)
                var waitTask = WorkflowAuditQueue.Queue.Reader.WaitToReadAsync(stoppingToken).AsTask();
                var timeoutTask = Task.Delay(100, stoppingToken);

                var completed = await Task.WhenAny(waitTask, timeoutTask);

                if (completed == waitTask && waitTask.Result)
                {
                    // Queue has data → read all available items
                    while (WorkflowAuditQueue.Queue.Reader.TryRead(out var payload))
                    {
                        if (payload.IsUpdate)
                            updateBatch.Add(payload.Audit);
                        else
                            insertBatch.Add(payload.Audit);

                        // Size-based flush
                        if (insertBatch.Count >= config.BatchSize ||
                            updateBatch.Count >= config.BatchSize)
                        {
                            await FlushBatchesAsync(insertBatch, updateBatch);
                            lastFlush = DateTime.UtcNow;
                        }
                    }
                }


                if ((insertBatch.Count > 0 || updateBatch.Count > 0) &&
                    (DateTime.UtcNow - lastFlush).TotalMilliseconds >= config.MaxWaitMs)
                {
                    await FlushBatchesAsync(insertBatch, updateBatch);
                    lastFlush = DateTime.UtcNow;
                }
            }
        }
#pragma warning restore S3776


        private async Task FlushBatchesAsync(
        List<WorkflowExecutionAudit> insertBatch,
        List<WorkflowExecutionAudit> updateBatch)
        {
            if (insertBatch.Count == 0 && updateBatch.Count == 0)
                return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

                // Insert batch
                if (insertBatch.Count > 0)
                {
                    await auditService.BulkInsertWorkflowAuditsAsync(insertBatch);
                }

                // Update batch
                if (updateBatch.Count > 0)
                {
                    await auditService.BulkUpdateWorkflowAuditsAsync(updateBatch);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorkflowAuditWorker:FlushBatches] ERROR: {ex}");
            }
            finally
            {
                insertBatch.Clear();
                updateBatch.Clear();
            }
        }

    }
}
