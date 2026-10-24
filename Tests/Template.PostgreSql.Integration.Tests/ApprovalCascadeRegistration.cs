using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Template.Application.Features.Approvals;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Approvals;
using Template.Application.Services.Procurement;
using Template.Application.Workflow;

namespace Template.PostgreSql.Integration.Tests;

/// <summary>
/// Phase 31 made requisition cancellation (REQ-008) and supplier suspension (APR-012) end a pending approval in the same
/// commit, so fixtures that compose those handlers by hand also need the approval cascade. Registrations a fixture
/// already made win; the workflow client always answers as a running orchestration.
/// </summary>
internal static class ApprovalCascadeRegistration
{
    public static IServiceCollection AddApprovalCascade(this IServiceCollection services)
    {
        services.TryAddScoped<ProcurementWriter>();
        services.TryAddScoped<ApprovalOutcomes>();
        services.TryAddScoped<ApprovalCascade>();
        services.TryAddSingleton<IApprovalWorkflowClient>(new RunningWorkflowClient());
        services.TryAddSingleton<IPermissionReader>(new NoPermissions());
        return services;
    }

    private sealed class RunningWorkflowClient : IApprovalWorkflowClient
    {
        public Task<ApprovalWorkflowStartResult> StartAsync(ApprovalWorkflowInput input, CancellationToken cancellationToken) =>
            Task.FromResult(ApprovalWorkflowStartResult.Started);

        public Task<ApprovalWorkflowSignalResult> SignalAsync(Guid awardId, ApprovalSignal signal, CancellationToken cancellationToken) =>
            Task.FromResult(ApprovalWorkflowSignalResult.Delivered);

        public Task<ApprovalWorkflowStatus> GetStatusAsync(Guid awardId, CancellationToken cancellationToken) =>
            Task.FromResult(new ApprovalWorkflowStatus(
                ApprovalWorkflowNames.InstanceIdFor(awardId), ApprovalWorkflowRuntimeStatus.Running, ApprovalWorkflowNames.CurrentVersion, null, null));

        public Task<bool> TerminateAsync(Guid awardId, string reason, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class NoPermissions : IPermissionReader
    {
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }
}
