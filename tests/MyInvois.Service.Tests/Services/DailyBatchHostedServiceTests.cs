using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MyInvois.Service.Configuration;
using MyInvois.Service.Models;
using MyInvois.Service.Services;

namespace MyInvois.Service.Tests.Services;

[Trait("Category", "Unit")]
public class DailyBatchHostedServiceTests
{
    // ── TimeUntilNextRun logic ────────────────────────────────────────────────

    [Fact]
    public void TimeUntilNextRun_TargetInFuture_ReturnsPositiveDelay()
    {
        // Pick a target that is definitely in the future from any test run time:
        // add 23 hours to ensure it's always forward.
        var now    = DateTime.Now;
        var target = now.AddHours(23);

        var delay = DailyBatchHostedService.TimeUntilNextRun(target.Hour, target.Minute);

        delay.Should().BePositive();
        delay.Should().BeLessThanOrEqualTo(TimeSpan.FromHours(24));
    }

    [Fact]
    public void TimeUntilNextRun_TargetAlreadyPassed_ReturnsDelayToTomorrow()
    {
        // Use midnight (00:00) — if we're past midnight (always true after 00:01),
        // the next run should be tomorrow's midnight, i.e. ~ 24h away.
        var delay = DailyBatchHostedService.TimeUntilNextRun(0, 0);

        // Should be between 0 and 24 hours; and since now > 00:00 today,
        // it must be pointing to tomorrow (> 0).
        delay.Should().BePositive();
        delay.Should().BeLessThanOrEqualTo(TimeSpan.FromHours(24));
    }

    [Fact]
    public void TimeUntilNextRun_NeverReturnsNegativeOrZero()
    {
        // Test a range of hours to confirm the method never returns ≤ 0.
        for (var h = 0; h < 24; h++)
        {
            var delay = DailyBatchHostedService.TimeUntilNextRun(h, 0);
            delay.Should().BePositive(because: $"delay for hour {h} must always be forward in time");
        }
    }

    // ── Scheduler disabled ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ExitsImmediatelyWithoutCallingProcessor()
    {
        var processorMock = new Mock<IInvoiceProcessor>();
        var svc = BuildService(enabled: false, processorMock);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await svc.StartAsync(cts.Token);
        await svc.StopAsync(CancellationToken.None);

        processorMock.Verify(
            p => p.ProcessDateRangeBatch(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "processor must not be called when scheduler is disabled");
    }

    // ── Scheduler enabled, immediate first-run via short delay ───────────────

    [Fact]
    public async Task RunBatchAsync_CallsProcessorWithYesterdayDateRange()
    {
        // Directly invoke RunBatchAsync (internal, visible via InternalsVisibleTo)
        // to verify the correct date range is passed — avoids a slow timing loop.
        var processorMock = new Mock<IInvoiceProcessor>();
        processorMock
            .Setup(p => p.ProcessDateRangeBatch(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchResult { CompletedAt = DateTime.UtcNow });

        var svc = BuildService(enabled: true, processorMock, lookbackDays: 1);

        await svc.RunBatchAsync(CancellationToken.None);

        var expectedFrom = DateTime.Today.AddDays(-1);
        var expectedTo   = DateTime.Today.AddSeconds(-1);

        processorMock.Verify(p => p.ProcessDateRangeBatch(
            It.Is<DateTime>(d => d.Date == expectedFrom.Date),
            It.Is<DateTime>(d => d.Date == expectedTo.Date),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessorThrows_DoesNotCrashHost()
    {
        var processorMock = new Mock<IInvoiceProcessor>();
        processorMock
            .Setup(p => p.ProcessDateRangeBatch(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB2 unreachable"));

        var fireAt = DateTime.Now.AddSeconds(1);
        var svc    = BuildService(enabled: true, processorMock, hour: fireAt.Hour, minute: fireAt.Minute);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // The hosted service must NOT propagate the exception out of StartAsync / StopAsync.
        var act = async () =>
        {
            await svc.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
            await svc.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync("unhandled exceptions inside a BackgroundService must be swallowed and logged");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DailyBatchHostedService BuildService(
        bool enabled,
        Mock<IInvoiceProcessor> processorMock,
        int hour         = 2,
        int minute       = 0,
        int lookbackDays = 1)
    {
        var services = new ServiceCollection();
        services.AddScoped<IInvoiceProcessor>(_ => processorMock.Object);
        var provider = services.BuildServiceProvider();

        var settings = Options.Create(new BatchSchedulerSettings
        {
            Enabled        = enabled,
            DailyRunHour   = hour,
            DailyRunMinute = minute,
            LookbackDays   = lookbackDays,
        });

        return new DailyBatchHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            settings,
            new Mock<ILogger<DailyBatchHostedService>>().Object);
    }
}
