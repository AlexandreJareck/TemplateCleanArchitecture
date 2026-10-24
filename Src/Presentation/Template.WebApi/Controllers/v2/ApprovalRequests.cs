using Template.Application.Features.Approvals;

namespace Template.WebApi.Controllers.v2;

/// <summary>Decision on the current step: <c>Approved</c> or <c>Rejected</c>; rejection needs a 10–500 character reason (APR-009).</summary>
public sealed record ApprovalDecisionRequest(string? Decision, string? Reason);

/// <summary>Delegation of the current step to another eligible approver with a 10–500 character reason (APR-010).</summary>
public sealed record ApprovalDelegationRequest(Guid DelegateUserId, string? Reason);

/// <summary>Administrative reassignment to exactly one of a user or a role group, with a reason (APR-015).</summary>
public sealed record ApprovalReassignmentRequest(Guid? AssigneeUserId, string? AssigneeRole, string? Reason);

/// <summary>Administrative cancellation with a 10–500 character reason (APR-015).</summary>
public sealed record ApprovalCancellationRequest(string? Reason);

/// <summary>A new approval policy version effective from now or later (POL-001, APR-017); thresholds in base currency.</summary>
public sealed record CreateApprovalPolicyRequest(
    DateTime? EffectiveFrom,
    IReadOnlyList<ApprovalPolicyLevelInputModel>? Levels,
    double? StepDurationHours,
    double? ReminderAfterHours,
    string? EscalationRole,
    int? MaxEscalations,
    string? Reason);
