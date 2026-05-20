namespace MyInvois.Service.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;

/// <summary>
/// Background scheduler that fires ProcessDateRangeBatch() once per day at the configured time.
/// Uses IServiceScopeFactory so each run gets a fresh DI scope (scoped services like
/// IInvoiceProcessor, IAuditLogger are resolved fresh per invocation).
/// </summary>
public sealed class DailyBatchHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BatchSchedulerSettings _settings;
    private readonly ILogger<DailyBatchHostedService> _logger;

    public DailyBatchHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<BatchSchedulerSettings> settings,
        ILogger<DailyBatchHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings     = settings.Value;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("[DailyBatch] Scheduler disabled via BatchScheduler:Enabled=false.");
            return;
        }

        _logger.LogInformation(
            "[DailyBatch] Scheduler started. Daily run at {Hour:D2}:{Minute:D2} local time, lookback {Days} day(s).",
            _settings.DailyRunHour, _settings.DailyRunMinute, _settings.LookbackDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun(_settings.DailyRunHour, _settings.DailyRunMinute);

            _logger.LogInformation("[DailyBatch] Next run in {Minutes:F0} minutes ({At:HH:mm} local).",
                delay.TotalMinutes, DateTime.Now.Add(delay));

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            await RunBatchAsync(stoppingToken);
        }

        _logger.LogInformation("[DailyBatch] Scheduler stopped.");
    }

    internal async Task RunBatchAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DailyBatch] Firing daily batch.");

        using var scope = _scopeFactory.CreateScope();
        var processor   = scope.ServiceProvider.GetRequiredService<IInvoiceProcessor>();

        try
        {
            var today    = DateTime.Today;
            var fromDate = today.AddDays(-_settings.LookbackDays);
            var toDate   = today.AddSeconds(-1);

            var result = await processor.ProcessDateRangeBatch(fromDate, toDate, stoppingToken);

            _logger.LogInformation(
                "[DailyBatch] Complete. BatchId={BatchId} Total={Total} Success={Success} Failed={Failed} Skipped={Skipped} Duration={Duration}s",
                result.BatchId, result.TotalInvoices, result.SuccessCount,
                result.FailedCount, result.SkippedCount, result.DurationSeconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[DailyBatch] Run cancelled mid-flight.");
        }
        catch (Exception ex)
        {
            // Log and continue — a failed run must not crash the host process.
            _logger.LogError(ex, "[DailyBatch] Unhandled exception during daily batch run.");
        }
    }

    /// <summary>
    /// Calculates the delay until the next configured run time (always forward in time).
    /// If the target time today has already passed, returns the delay to tomorrow's run.
    /// </summary>
    public static TimeSpan TimeUntilNextRun(int hour, int minute)
    {
        var now    = DateTime.Now;
        var target = now.Date.AddHours(hour).AddMinutes(minute);

        if (target <= now)
            target = target.AddDays(1);

        return target - now;
    }
}
