using Didanihaa.Infrastructure;

namespace Didanihaa.Control;

public sealed class ScanWorker(CatalogRepository repository, LibraryScanner scanner, ILogger<ScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        repository.RecoverInterruptedScans();
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = repository.Jobs().LastOrDefault(job => job.Status == "queued");
            if (job is not null && repository.ClaimScan(job.Id))
            {
                try { await scanner.RunAsync(job, stoppingToken); }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Scan {JobId} failed", job.Id);
                    repository.UpdateScan(job.Id, 0, 1, "failed", "خطای داخلی اسکن؛ گزارش برنامه را بررسی کنید.");
                }
            }
            else
            {
                try { await Task.Delay(800, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
