#nullable enable
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Maintenance;
using Template.Application.Logging;

namespace Template.Worker;

/// <summary>Operational data retention options of the Worker (<c>DataRetentionSettings</c>); no secret.</summary>
public sealed class DataRetentionSettings
{
    public const string SectionName = nameof(DataRetentionSettings);

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Rows removed per batch; each batch is its own short transaction.</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>Upper bound of batches per dataset and pass, so one pass never monopolizes the database.</summary>
    public int MaxBatchesPerRun { get; set; } = 20;

    /// <summary>NFR §5: completed Inbox entries are kept 7 days.</summary>
    public TimeSpan InboxRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>NFR §5: session families are kept until their absolute expiry plus 30 days.</summary>
    public TimeSpan SessionFamilyRetention { get; set; } = TimeSpan.FromDays(30);
}

public sealed class DataRetentionSettingsValidator : IValidateOptions<DataRetentionSettings>
{
    public ValidateOptionsResult Validate(string? name, DataRetentionSettings options) =>
        options.Interval >= TimeSpan.FromMinutes(1) && options.Interval <= TimeSpan.FromDays(1)
        && options.BatchSize is >= 1 and <= 5000
        && options.MaxBatchesPerRun is >= 1 and <= 1000
        && options.InboxRetention >= TimeSpan.FromDays(1)
        && options.SessionFamilyRetention >= TimeSpan.Zero
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "DataRetentionSettings requires a 1 min-1 day interval, a 1-5000 batch, 1-1000 batches per run, "
                + "an Inbox retention of at least 1 day and a non-negative session retention.");
}

/// <summary>Rows removed by one retention pass, per dataset.</summary>
public sealed record DataRetentionResult(int IdempotencyRecords, int InboxEntries, int SessionFamilies);

/// <summary>
/// Periodic cleanup of operational records (docs/product/non-functional-requirements.md §5): expired API idempotency
/// records (24 h), completed Inbox entries (7 days) and session families past absolute expiry + 30 days. The Outbox keeps
/// its own cleanup in <see cref="OutboxDispatcherService"/>. Deletes are bounded batches selected by time only, so
/// concurrent Worker instances are safe (a row removed by another instance is simply not found). A failing dataset is
/// logged by exception type and retried at the next interval without skipping the others.
/// </summary>
public sealed class DataRetentionService(
    IServiceScopeFactory scopes,
    IOptions<DataRetentionSettings> options,
    TimeProvider timeProvider,
    ILogger<DataRetentionService> logger) : BackgroundService
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
                await RunOnceAsync(stoppingToken);
                await Task.Delay(settings.Interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Runs one pass over every dataset; exposed for tests.</summary>
    public async Task<DataRetentionResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var idempotency = await PurgeAsync("idempotency-records", settings,
            (store, batch) => store.DeleteExpiredIdempotencyRecordsAsync(now, batch, cancellationToken), cancellationToken);
        var inbox = await PurgeAsync("inbox", settings,
            (store, batch) => store.DeleteCompletedInboxBeforeAsync(now - settings.InboxRetention, batch, cancellationToken), cancellationToken);
        var sessions = await PurgeAsync("session-families", settings,
            (store, batch) => store.DeleteSessionFamiliesExpiredBeforeAsync(now - settings.SessionFamilyRetention, batch, cancellationToken),
            cancellationToken);
        return new DataRetentionResult(idempotency, inbox, sessions);
    }

    private async Task<int> PurgeAsync(
        string dataset,
        DataRetentionSettings settings,
        Func<IOperationalDataRetention, int, Task<int>> deleteBatch,
        CancellationToken cancellationToken)
    {
        var tag = new KeyValuePair<string, object?>("dataset", dataset);
        var total = 0;
        try
        {
            for (var batch = 0; batch < settings.MaxBatchesPerRun; batch++)
            {
                await using var scope = scopes.CreateAsyncScope();
                var deleted = await deleteBatch(scope.ServiceProvider.GetRequiredService<IOperationalDataRetention>(), settings.BatchSize);
                total += deleted;
                if (deleted < settings.BatchSize)
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            WorkerMetrics.RetentionFailures.Add(1, tag);
            logger.LogWarning(LogEvents.DataRetentionFailed,
                "Data retention for {Dataset} failed with {ExceptionType}; retrying", dataset, exception.GetType().Name);
        }

        if (total > 0)
        {
            WorkerMetrics.RetentionDeleted.Add(total, tag);
            logger.LogInformation(LogEvents.DataRetentionCompleted, "Data retention removed {Count} {Dataset} rows", total, dataset);
        }

        return total;
    }
}
