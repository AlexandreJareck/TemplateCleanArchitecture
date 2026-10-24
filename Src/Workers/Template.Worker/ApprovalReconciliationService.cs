#nullable enable
using Microsoft.Extensions.Options;
using Template.Application.Features.Approvals;
using Template.Application.Logging;

namespace Template.Worker;

/// <summary>Approval reconciliation sweep options of the Worker (<c>ApprovalReconciliationSettings</c>); no secret.</summary>
public sealed class ApprovalReconciliationSettings
{
    public const string SectionName = nameof(ApprovalReconciliationSettings);

    public bool Enabled { get; set; } = true;

    /// <summary>Delay between sweeps (APR-016 repair of lost starts and signals).</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    public int BatchSize { get; set; } = 100;
}

public sealed class ApprovalReconciliationSettingsValidator : IValidateOptions<ApprovalReconciliationSettings>
{
    public ValidateOptionsResult Validate(string? name, ApprovalReconciliationSettings options) =>
        options.Interval >= TimeSpan.FromSeconds(5) && options.Interval <= TimeSpan.FromDays(1) && options.BatchSize is >= 1 and <= 500
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("ApprovalReconciliationSettings requires a 5 s-1 day interval and a 1-500 batch.");
}

/// <summary>
/// APR-016 / NFR-REC-4: periodically repairs divergences between the database and the orchestration through the
/// Application use case — awards whose start was lost (for example after the broker exhausted its retries during a
/// WorkflowFunctions outage), pending instances whose orchestration is not running and stalled instances whose signal
/// was lost. Each sweep uses its own scope; failures are logged by type and retried at the next interval.
/// </summary>
public sealed class ApprovalReconciliationService(
    IServiceScopeFactory scopes,
    IOptions<ApprovalReconciliationSettings> options,
    TimeProvider timeProvider,
    ILogger<ApprovalReconciliationService> logger) : BackgroundService
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
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ApprovalReconciliation>().RunAsync(repair: true, settings.BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(LogEvents.ApprovalReconciliationRepairFailed, "Approval reconciliation failed with {ExceptionType}; retrying", exception.GetType().Name);
            }
        }
    }
}
