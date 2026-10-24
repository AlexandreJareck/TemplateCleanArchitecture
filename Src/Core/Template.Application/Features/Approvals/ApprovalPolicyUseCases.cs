#nullable enable
using FluentValidation;
using MediatR;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Approvals;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Application.Wrappers;
using Template.Domain.Approvals;

namespace Template.Application.Features.Approvals;

public sealed record ListApprovalPoliciesQuery : IRequest<Result<IReadOnlyList<ApprovalPolicyView>>>;

public sealed record GetApprovalPolicyQuery(int Version) : IRequest<Result<ApprovalPolicyView>>;

public sealed record ApprovalPolicyLevelInputModel(int Level, decimal MinimumAmountExclusive, string? Resolver, string? Role);

/// <summary>
/// POL-001/APR-017: a new immutable version effective from now or later; in-flight instances keep their pinned
/// version. Thresholds are in the organization base currency. Permission <c>approvals.policies.manage</c>.
/// </summary>
public sealed record CreateApprovalPolicyVersionCommand(
    DateTime? EffectiveFrom,
    IReadOnlyList<ApprovalPolicyLevelInputModel>? Levels,
    double? StepDurationHours,
    double? ReminderAfterHours,
    string? EscalationRole,
    int? MaxEscalations,
    string? Reason) : IRequest<Result<ApprovalPolicyView>>;

public sealed class CreateApprovalPolicyVersionCommandValidator : AbstractValidator<CreateApprovalPolicyVersionCommand>
{
    public CreateApprovalPolicyVersionCommandValidator()
    {
        RuleFor(command => command.EffectiveFrom).NotNull();
        RuleFor(command => command.Levels).NotEmpty();
        RuleFor(command => command.StepDurationHours).NotNull();
        RuleFor(command => command.ReminderAfterHours).NotNull();
        RuleFor(command => command.EscalationRole).NotEmpty().MaximumLength(ApprovalPolicyLevel.RoleMaxLength);
        RuleFor(command => command.MaxEscalations).NotNull();
        RuleFor(command => command.Reason).MaximumLength(ApprovalPolicy.ReasonMaxLength);
    }
}

public sealed class ApprovalPolicyHandlers(IApprovalPolicyRepository policies, ProcurementWriter writer) :
    IRequestHandler<ListApprovalPoliciesQuery, Result<IReadOnlyList<ApprovalPolicyView>>>,
    IRequestHandler<GetApprovalPolicyQuery, Result<ApprovalPolicyView>>,
    IRequestHandler<CreateApprovalPolicyVersionCommand, Result<ApprovalPolicyView>>
{
    public async Task<Result<IReadOnlyList<ApprovalPolicyView>>> Handle(ListApprovalPoliciesQuery request, CancellationToken cancellationToken)
    {
        if (!await MayReadAsync(cancellationToken))
            return ProcurementWriter.Forbidden();
        return (await policies.ListAsync(cancellationToken)).OrderBy(policy => policy.Version).Select(ApprovalPolicyView.From).ToArray();
    }

    public async Task<Result<ApprovalPolicyView>> Handle(GetApprovalPolicyQuery request, CancellationToken cancellationToken)
    {
        if (!await MayReadAsync(cancellationToken))
            return ProcurementWriter.Forbidden();
        var policy = await policies.FindByVersionAsync(request.Version, cancellationToken);
        return policy is null ? ProcurementWriter.NotFound("approval policy version") : ApprovalPolicyView.From(policy);
    }

    public async Task<Result<ApprovalPolicyView>> Handle(CreateApprovalPolicyVersionCommand request, CancellationToken cancellationToken)
    {
        var levels = new List<ApprovalPolicyLevelInput>();
        foreach (var level in request.Levels ?? [])
        {
            if (!Enum.TryParse<ApproverResolver>(level.Resolver, ignoreCase: false, out var resolver) || int.TryParse(level.Resolver, out _))
                return ApplicationError.Validation("levels", "resolver accepts CostCenterManager, DepartmentHead or RoleGroup.");
            if (level.Role is { } role && !ApprovalDecisionHandlers.AssignableRoleGroups.Contains(role))
                return ApplicationError.Validation("levels", "A role group must be a role that holds approvals.decide.");
            levels.Add(new ApprovalPolicyLevelInput(level.Level, level.MinimumAmountExclusive, resolver, level.Role));
        }

        if (!ApprovalDecisionHandlers.AssignableRoleGroups.Contains(request.EscalationRole!.Trim()))
            return ApplicationError.Validation("escalationRole", "The escalation group must be a role that holds approvals.decide.");
        if (request.StepDurationHours is not (> 0 and <= 2160) || request.ReminderAfterHours is not (> 0 and <= 2160))
            return ApplicationError.Validation("stepDurationHours", "Durations are positive hours of at most 90 days.");

        var settings = await writer.SettingsAsync(cancellationToken);
        var now = writer.UtcNow;
        var (policy, error) = ApprovalPolicy.Create(
            Guid.NewGuid(), await policies.GetLatestVersionAsync(cancellationToken) + 1, request.EffectiveFrom!.Value.ToUniversalTime(), settings.BaseCurrency,
            levels, TimeSpan.FromHours(request.StepDurationHours.Value), TimeSpan.FromHours(request.ReminderAfterHours.Value), request.EscalationRole,
            request.MaxEscalations!.Value, request.Reason, writer.ActorId, now, now);
        if (error is not null)
            return error.ToApplicationError();

        policies.Add(policy!);
        writer.Audit(ApprovalAudit.PolicyVersionCreated, ApprovalAudit.PolicyResource, policy!.Id, toState: "Created", reason: policy.Reason,
            ruleIds: ["POL-001", "APR-017"], details: new Dictionary<string, string>
            {
                ["version"] = ApprovalAudit.Text(policy.Version),
                ["effectiveFrom"] = policy.EffectiveFromUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["levels"] = ApprovalAudit.Text(policy.Levels.Count)
            });
        var commit = await writer.CommitAsync(_ => Task.FromResult<long?>(null),
            constraint => constraint == IApprovalPolicyRepository.VersionUniqueKey
                ? new ApplicationError(ProblemCodes.PolicyVersionInUse, ErrorCategory.Conflict, "Another version was created concurrently; retry.", "APR-017")
                : null,
            cancellationToken);
        return commit is null ? ApprovalPolicyView.From(policy) : commit;
    }

    private async Task<bool> MayReadAsync(CancellationToken cancellationToken)
    {
        var permissions = await writer.PermissionsAsync(cancellationToken);
        return permissions.Contains(Permissions.ApprovalsPoliciesManage) || permissions.Contains(Permissions.ApprovalsReadAll);
    }
}
