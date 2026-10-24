using System.Data.Common;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Template.Application.Features.Approvals;
using Template.Application.Workflow;
using Template.WorkflowFunctions.Hosting;
using Template.WorkflowFunctions.Orchestration;

namespace Template.WorkflowFunctions.Activities;

/// <summary>
/// Durable activities: thin adapters over <see cref="ApprovalWorkflowActivities"/> (Application) in a per-invocation DI
/// scope. They own the side effects, the correlation of the invocation (NFR-AUD-3) and the retry classification:
/// database and timeout failures become <see cref="TransientActivityException"/> (retried by the orchestration's policy);
/// anything else is permanent. Every activity is idempotent, so a retried or replayed call never applies an effect twice.
/// </summary>
public sealed class ApprovalActivityFunctions(ApprovalWorkflowActivities activities, WorkflowRequestContext requestContext)
{
    [Function(ApprovalWorkflowNames.StartActivity)]
    public Task<ApprovalProgress> StartAsync([ActivityTrigger] ApprovalStartRequest request, CancellationToken cancellationToken) =>
        RunAsync(request.Input.CorrelationId, () => activities.StartAsync(request, cancellationToken));

    [Function(ApprovalWorkflowNames.AssignStepActivity)]
    public Task<ApprovalProgress> AssignStepAsync([ActivityTrigger] ApprovalActivityRequest request, CancellationToken cancellationToken) =>
        RunAsync(request.CorrelationId, () => activities.AssignStepAsync(request, cancellationToken));

    [Function(ApprovalWorkflowNames.ProgressActivity)]
    public Task<ApprovalProgress> GetProgressAsync([ActivityTrigger] ApprovalActivityRequest request, CancellationToken cancellationToken) =>
        RunAsync(request.CorrelationId, () => activities.GetProgressAsync(request, cancellationToken));

    [Function(ApprovalWorkflowNames.TimerActivity)]
    public Task<ApprovalProgress> ProcessTimerAsync([ActivityTrigger] ApprovalTimerRequest request, CancellationToken cancellationToken) =>
        RunAsync(request.CorrelationId, () => activities.ProcessTimerAsync(request, cancellationToken));

    [Function(ApprovalWorkflowNames.FinalizeActivity)]
    public Task<ApprovalProgress> FinalizeAsync([ActivityTrigger] ApprovalActivityRequest request, CancellationToken cancellationToken) =>
        RunAsync(request.CorrelationId, () => activities.FinalizeAsync(request, cancellationToken));

    private async Task<ApprovalProgress> RunAsync(string correlationId, Func<Task<ApprovalProgress>> activity)
    {
        requestContext.Use(correlationId);
        try
        {
            return await activity();
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            throw new TransientActivityException($"Transient {exception.GetType().Name} in an approval activity.", exception);
        }
    }

    public static bool IsTransient(Exception exception) => exception switch
    {
        DbException or TimeoutException or DbUpdateException => true,
        OperationCanceledException => true,
        InvalidOperationException { InnerException: { } inner } => IsTransient(inner),
        _ => false
    };
}
