#nullable enable
using MediatR;
using Microsoft.Extensions.Options;
using Template.Application.Common.Results;
using Template.Application.Features.Requisitions;
using Template.Application.Features.Sourcing;
using Template.Application.Logging;

namespace Template.Worker;

/// <summary>Scheduled sweep options of the Worker (<c>ScheduledSweepSettings</c>); no secret.</summary>
public sealed class ScheduledSweepSettings
{
    public const string SectionName = nameof(ScheduledSweepSettings);

    public bool Enabled { get; set; } = true;

    /// <summary>Delay between sweeps; it bounds how late an RFQ closes after its deadline.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    public int BatchSize { get; set; } = 100;
}

public sealed class ScheduledSweepSettingsValidator : IValidateOptions<ScheduledSweepSettings>
{
    public ValidateOptionsResult Validate(string? name, ScheduledSweepSettings options) =>
        options.Interval >= TimeSpan.FromSeconds(5) && options.Interval <= TimeSpan.FromDays(1) && options.BatchSize is >= 1 and <= 500
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("ScheduledSweepSettings requires a 5 s-1 day interval and a 1-500 batch.");
}

/// <summary>
/// Phase 33 recurring triggers (D33-1, ADR-0003 "Worker + database timers" for simple sweeps): SRC-012 closes RFQs whose
/// deadline passed and REQ-009 expires requisitions past needed-by + grace, releasing their reservation. Both run the
/// existing idempotent Application use cases, which retry optimistic conflicts, so concurrent Worker instances are
/// safe. Each sweep uses its own scope (one correlation ID); a failure is logged by type and retried next interval.
/// </summary>
public sealed class ScheduledSweepService(
    IServiceScopeFactory scopes,
    IOptions<ScheduledSweepSettings> options,
    TimeProvider timeProvider,
    ILogger<ScheduledSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(settings.Interval, timeProvider, stoppingToken);
                await RunOnceAsync(settings.BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Runs both sweeps once; exposed for tests. One sweep failing does not skip the other.</summary>
    public async Task RunOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        await SweepAsync("rfq-close", sender => sender.Send(new CloseDueRfqsCommand(batchSize), cancellationToken), cancellationToken);
        await SweepAsync("requisition-expiry", sender => sender.Send(new ExpireDueRequisitionsCommand(batchSize), cancellationToken), cancellationToken);
    }

    private async Task SweepAsync(
        string sweep,
        Func<ISender, Task<Result<int>>> run,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await run(scope.ServiceProvider.GetRequiredService<ISender>());
            if (result.IsSuccess)
            {
                WorkerMetrics.SweepItems.Add(result.Value, new KeyValuePair<string, object?>("sweep", sweep));
                if (result.Value > 0)
                    logger.LogInformation(LogEvents.ScheduledSweepCompleted, "Scheduled sweep {Sweep} processed {Count} records", sweep, result.Value);
            }
            else
            {
                WorkerMetrics.SweepFailures.Add(1, new KeyValuePair<string, object?>("sweep", sweep));
                logger.LogWarning(LogEvents.ScheduledSweepFailed, "Scheduled sweep {Sweep} returned {ErrorCode}", sweep, result.Error!.Code);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            WorkerMetrics.SweepFailures.Add(1, new KeyValuePair<string, object?>("sweep", sweep));
            logger.LogWarning(LogEvents.ScheduledSweepFailed, "Scheduled sweep {Sweep} failed with {ExceptionType}; retrying", sweep, exception.GetType().Name);
        }
    }
}
