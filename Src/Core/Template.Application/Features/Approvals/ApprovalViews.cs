#nullable enable
using System.Globalization;
using Template.Application.Common.Concurrency;
using Template.Application.Features.Catalog;
using Template.Domain.Approvals;
using Template.Domain.Sourcing;

namespace Template.Application.Features.Approvals;

/// <summary>A step of an approval instance; names are display names only (no email or claim).</summary>
public sealed record ApprovalStepView(
    int Level,
    string Resolver,
    string Status,
    Guid? AssigneeUserId,
    string? AssigneeName,
    string? AssigneeRole,
    DateTime? AssignedAt,
    DateTime? DueAt,
    DateTime? ReminderDueAt,
    DateTime? RemindedAt,
    int EscalationCount,
    bool Escalated,
    string? Decision,
    Guid? DecidedBy,
    string? DecidedByName,
    DateTime? DecidedAt,
    string? Reason);

/// <summary>One ranked or invalid entry of the frozen comparison shown to approval participants (D31-6).</summary>
public sealed record ApprovalComparisonEntryView(
    Guid SupplierId, string SupplierName, int? Rank, bool IsValid, MoneyView Quoted, MoneyView? Normalized, int DeliveryDays, bool Awarded);

/// <summary>Award summary of the instance: the decision subject, frozen at award submission.</summary>
public sealed record ApprovalAwardView(
    Guid AwardId,
    Guid RfqId,
    string RfqNumber,
    Guid SupplierId,
    string SupplierName,
    int Rank,
    MoneyView Awarded,
    MoneyView Normalized,
    string? Justification,
    IReadOnlyList<string> Waivers,
    IReadOnlyList<ApprovalComparisonEntryView> Comparison);

public sealed record ApprovalInstanceView(
    Guid Id,
    string Version,
    string Status,
    Guid RequisitionId,
    string RequisitionNumber,
    Guid RequesterId,
    string? RequesterName,
    MoneyView AmountEvaluated,
    MoneyView RequisitionAmount,
    string Policy,
    int CurrentLevel,
    int FinalLevel,
    string NextAction,
    DateTime StartedAt,
    DateTime? EndedAt,
    string? EndCause,
    string? EndReason,
    IReadOnlyList<ApprovalStepView> Steps,
    ApprovalAwardView? Award,
    IReadOnlyList<string> AllowedActions);

/// <summary>Inbox and list row.</summary>
public sealed record ApprovalSummaryView(
    Guid Id,
    string Version,
    string Status,
    string RequisitionNumber,
    Guid RequisitionId,
    MoneyView AmountEvaluated,
    int CurrentLevel,
    int FinalLevel,
    Guid? AssigneeUserId,
    string? AssigneeRole,
    DateTime? DueAt,
    bool Escalated,
    DateTime StartedAt,
    DateTime UpdatedAt)
{
    public static ApprovalSummaryView From(ApprovalInstance instance)
    {
        var step = instance.Status == ApprovalStatus.Pending ? instance.CurrentStep : null;
        return new ApprovalSummaryView(
            instance.Id, VersionPrecondition.Format(instance.Version), instance.Status.ToString(), instance.RequisitionNumber, instance.RequisitionId,
            ApprovalViewMapping.Money(instance.AmountEvaluated, instance.BaseCurrency), instance.CurrentLevel, instance.FinalLevel,
            step?.AssigneeUserId, step?.AssigneeRole, step?.DueAtUtc, step?.EscalationCount > 0, instance.StartedAtUtc, instance.UpdatedAtUtc);
    }
}

public sealed record ApprovalHistoryEntryView(
    int Sequence, string Action, int? Level, Guid? ActorId, string? ActorName, Guid? AssigneeUserId, string? AssigneeRole, DateTime At, string? Reason,
    string CorrelationId);

public sealed record ApprovalHistoryView(Guid Id, IReadOnlyList<ApprovalHistoryEntryView> Entries);

public sealed record ApprovalPolicyLevelView(int Level, decimal MinimumAmountExclusive, string Resolver, string? Role);

public sealed record ApprovalPolicyView(
    Guid Id,
    int Version,
    DateTime EffectiveFrom,
    string BaseCurrency,
    IReadOnlyList<ApprovalPolicyLevelView> Levels,
    double StepDurationHours,
    double ReminderAfterHours,
    string EscalationRole,
    int MaxEscalations,
    string? Reason,
    DateTime CreatedAt,
    DateTime? FirstUsedAt)
{
    public static ApprovalPolicyView From(ApprovalPolicy policy) => new(
        policy.Id, policy.Version, policy.EffectiveFromUtc, policy.BaseCurrency,
        policy.Levels.OrderBy(level => level.Level)
            .Select(level => new ApprovalPolicyLevelView(level.Level, level.MinimumAmountExclusive, level.Resolver.ToString(), level.Role)).ToArray(),
        policy.StepDuration.TotalHours, policy.ReminderAfter.TotalHours, policy.EscalationRole, policy.MaxEscalations, policy.Reason,
        policy.CreatedAtUtc, policy.FirstUsedAtUtc);
}

/// <summary>Durable runtime status next to the database status of one instance (APR-016 diagnostics).</summary>
public sealed record ApprovalWorkflowView(
    Guid Id, string WorkflowInstanceId, string DatabaseStatus, string NextAction, string RuntimeStatus, string? Version, DateTime? CreatedAt, DateTime? LastUpdatedAt);

/// <summary>One divergence between the database and the orchestration (APR-016).</summary>
public sealed record ApprovalReconciliationItemView(
    Guid AwardId, Guid? InstanceId, string DatabaseStatus, string RuntimeStatus, string Issue, string Repair);

public sealed record ApprovalReconciliationView(DateTime CheckedAt, int Checked, IReadOnlyList<ApprovalReconciliationItemView> Items);

public static class ApprovalActions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Delegate = "delegate";
    public const string Reassign = "reassign";
    public const string Cancel = "cancel";
}

internal static class ApprovalViewMapping
{
    public static ApprovalStepView Step(ApprovalStep step, IReadOnlyDictionary<Guid, string> names) => new(
        step.Level, step.Resolver.ToString(), step.Status.ToString(), step.AssigneeUserId, Name(names, step.AssigneeUserId), step.AssigneeRole,
        step.AssignedAtUtc, step.DueAtUtc, step.ReminderDueAtUtc, step.RemindedAtUtc, step.EscalationCount, step.EscalationCount > 0,
        step.Decision?.ToString(), step.DecidedBy, Name(names, step.DecidedBy), step.DecidedAtUtc, step.Reason);

    public static ApprovalAwardView? Award(ApprovalInstance instance, RequestForQuotation? rfq)
    {
        var award = rfq?.Awards.FirstOrDefault(candidate => candidate.Id == instance.AwardId);
        if (rfq is null || award is null)
            return null;
        var comparison = rfq.Comparisons.FirstOrDefault(candidate => candidate.Id == award.ComparisonId);
        return new ApprovalAwardView(
            award.Id, rfq.Id, rfq.Number, award.SupplierId, award.SupplierLegalName, award.Rank,
            ApprovalViewMapping.Money(award.AwardedTotal, award.Currency), ApprovalViewMapping.Money(award.NormalizedTotal, award.BaseCurrency), award.Justification,
            rfq.Waivers.Where(waiver => waiver.ScopeId == award.Id).Select(waiver => waiver.Type.ToString()).OrderBy(type => type, StringComparer.Ordinal).ToArray(),
            comparison?.Entries.OrderBy(entry => entry.Rank ?? int.MaxValue).ThenBy(entry => entry.SupplierLegalName, StringComparer.Ordinal)
                .Select(entry => new ApprovalComparisonEntryView(
                    entry.SupplierId, entry.SupplierLegalName, entry.Rank, entry.IsValid, ApprovalViewMapping.Money(entry.RawTotal, entry.Currency),
                    entry.NormalizedTotal is { } normalized ? ApprovalViewMapping.Money(normalized, rfq.BaseCurrency) : null, entry.DeliveryDays,
                    entry.QuoteRevisionId == award.QuoteRevisionId))
                .ToArray() ?? []);
    }

    /// <summary>Decimal amounts travel as invariant strings, like every other v2 money value.</summary>
    public static MoneyView Money(decimal amount, string currency) => new(amount.ToString(CultureInfo.InvariantCulture), currency);

    public static string? Name(IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id is { } value && names.TryGetValue(value, out var name) ? name : null;
}
