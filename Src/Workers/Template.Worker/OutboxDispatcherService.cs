#nullable enable
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Messaging;
using Template.Application.Logging;
using Template.Application.Messaging.Procurement;

namespace Template.Worker;

public sealed class OutboxDispatcherService(
    IServiceScopeFactory scopes,
    IProcurementEventPublisher publisher,
    IOptions<OutboxDispatchSettings> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    private readonly string owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var nextCleanup = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
                var batch = await store.ClaimAsync(owner, now, settings.LeaseDuration, settings.BatchSize, stoppingToken);
                if (batch.Count > 0)
                    logger.LogInformation(LogEvents.OutboxBatchClaimed, "Outbox dispatcher claimed {Count} events", batch.Count);

                foreach (var lease in batch)
                    await DispatchAsync(lease, settings, stoppingToken);

                if (now >= nextCleanup)
                {
                    await store.DeleteDispatchedBeforeAsync(now - settings.DispatchedRetention, 1000, stoppingToken);
                    nextCleanup = now + settings.CleanupInterval;
                }

                if (batch.Count == 0)
                    await Task.Delay(settings.PollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(LogEvents.OutboxDispatchFailed,
                    "Outbox polling failed with {ExceptionType}; retrying", exception.GetType().Name);
                await Task.Delay(settings.PollInterval, timeProvider, stoppingToken);
            }
        }
    }

    private async Task DispatchAsync(OutboxLease lease, OutboxDispatchSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            await publisher.PublishAsync(lease.Envelope, cancellationToken);
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IOutboxStore>().MarkDispatchedAsync(
                lease.Envelope.EventId, owner, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
            logger.LogInformation(LogEvents.OutboxEventDispatched,
                "Outbox event {EventId} of type {EventType} was confirmed", lease.Envelope.EventId, lease.Envelope.EventType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var nextAttemptAtUtc = OutboxDispatchPolicy.NextAttemptAtUtc(
                now, lease.AttemptCount, settings.FirstRetryDelay, settings.MaxRetryDelay);
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IOutboxStore>().MarkFailedAsync(
                lease.Envelope.EventId, owner, now, nextAttemptAtUtc, settings.MaxAttempts,
                exception.GetType().Name, cancellationToken);
            logger.LogWarning(LogEvents.OutboxDispatchFailed,
                "Outbox event {EventId} publish failed with {ExceptionType}; attempt {Attempt}",
                lease.Envelope.EventId, exception.GetType().Name, lease.AttemptCount + 1);
        }
    }
}
