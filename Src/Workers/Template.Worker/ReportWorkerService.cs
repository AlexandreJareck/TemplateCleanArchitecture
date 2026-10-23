#nullable enable
using Microsoft.Extensions.Options;
using Template.Application.Logging;
using Template.Application.Services.Reporting;

namespace Template.Worker;

/// <summary>Report execution options of the Worker (<c>ReportWorkerSettings</c>); no secret.</summary>
public sealed class ReportWorkerSettings
{
    public const string SectionName = nameof(ReportWorkerSettings);

    public bool Enabled { get; set; } = true;

    /// <summary>Delay before polling again when no job is due.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Concurrent report attempts per Worker process (1–4).</summary>
    public int MaxConcurrentJobs { get; set; } = 1;

    /// <summary>Retention pass interval (RPT-006 expiry and FIL-005 cleanup selection).</summary>
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(15);

    public int MaintenanceBatchSize { get; set; } = 200;
}

public sealed class ReportWorkerSettingsValidator : IValidateOptions<ReportWorkerSettings>
{
    public ValidateOptionsResult Validate(string? name, ReportWorkerSettings options) =>
        options.PollInterval > TimeSpan.Zero && options.PollInterval <= TimeSpan.FromMinutes(5)
        && options.MaxConcurrentJobs is >= 1 and <= 4
        && options.MaintenanceInterval >= TimeSpan.FromSeconds(10) && options.MaintenanceInterval <= TimeSpan.FromDays(1)
        && options.MaintenanceBatchSize is >= 1 and <= 1000
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("ReportWorkerSettings requires a 0-5 min poll interval, 1-4 concurrent jobs, a 10 s-1 day maintenance interval and a 1-1000 batch.");
}

/// <summary>
/// Runs report attempts (<see cref="ReportJobProcessor"/>) in bounded parallel lanes and the periodic retention pass
/// (<see cref="ReportMaintenance"/>). Each attempt uses its own scope. On shutdown the in-flight attempt hands its job
/// back without consuming the attempt; a crashed process is recovered by the expired lease (workflows §6).
/// </summary>
public sealed class ReportWorkerService(
    IServiceScopeFactory scopes,
    IOptions<ReportWorkerSettings> options,
    TimeProvider timeProvider,
    ILogger<ReportWorkerService> logger) : BackgroundService
{
    private readonly string owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return Task.CompletedTask;
        var lanes = Enumerable.Range(0, settings.MaxConcurrentJobs).Select(_ => RunLaneAsync(settings, stoppingToken));
        return Task.WhenAll([.. lanes, RunMaintenanceAsync(settings, stoppingToken)]);
    }

    private async Task RunLaneAsync(ReportWorkerSettings settings, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var (outcome, _) = await scope.ServiceProvider.GetRequiredService<ReportJobProcessor>().ProcessNextAsync(owner, stoppingToken);
                if (outcome == ReportProcessingOutcome.Idle)
                    await Task.Delay(settings.PollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(LogEvents.ReportJobFailed, "Report polling failed with {ExceptionType}; retrying", exception.GetType().Name);
                try { await Task.Delay(settings.PollInterval, timeProvider, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task RunMaintenanceAsync(ReportWorkerSettings settings, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ReportMaintenance>().RunAsync(settings.MaintenanceBatchSize, stoppingToken);
                await Task.Delay(settings.MaintenanceInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(LogEvents.ReportMaintenanceCompleted, "Report maintenance failed with {ExceptionType}; retrying", exception.GetType().Name);
                try { await Task.Delay(settings.MaintenanceInterval, timeProvider, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
