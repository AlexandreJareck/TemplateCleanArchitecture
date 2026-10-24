using System;
using System.Collections.Generic;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Approvals;

/// <summary>How the assignee of a level is resolved (APR-003).</summary>
public enum ApproverResolver
{
    CostCenterManager,
    DepartmentHead,
    RoleGroup
}

/// <summary>One amount-threshold level of an <see cref="ApprovalPolicy"/> (APR-002).</summary>
public sealed class ApprovalPolicyLevel
{
    public const int RoleMaxLength = 64;

    private ApprovalPolicyLevel()
    {
    }

    internal ApprovalPolicyLevel(Guid id, Guid policyId, int level, decimal minimumAmountExclusive, ApproverResolver resolver, string? role)
    {
        Id = id;
        PolicyId = policyId;
        Level = level;
        MinimumAmountExclusive = minimumAmountExclusive;
        Resolver = resolver;
        Role = role;
    }

    public Guid Id { get; private set; }
    public Guid PolicyId { get; private set; }
    public int Level { get; private set; }

    /// <summary>The level is required iff the amount in base currency is strictly greater than this value.</summary>
    public decimal MinimumAmountExclusive { get; private set; }

    public ApproverResolver Resolver { get; private set; }

    /// <summary>Role group for <see cref="ApproverResolver.RoleGroup"/>; <c>null</c> otherwise.</summary>
    public string? Role { get; private set; }
}

/// <summary>Input of one level when a policy version is created.</summary>
public sealed record ApprovalPolicyLevelInput(int Level, decimal MinimumAmountExclusive, ApproverResolver Resolver, string? Role);

/// <summary>
/// Versioned approval policy (docs/product/domain-model.md §Approvals, B23-10, D23-13). A version is immutable once an
/// approval instance pinned it (APR-017, POL-001): a change creates a new version whose effective instant is not in the
/// past, and in-flight instances keep the version they started with. Levels, durations and the escalation group are
/// configuration data; the orchestration never reads them (APR-016).
/// </summary>
public sealed class ApprovalPolicy
{
    public const string PolicyType = "ApprovalPolicy";
    public const int MaxLevels = 5;
    public const int ReasonMaxLength = 500;

    /// <summary>Stable ID of the seeded version 1 (business-rules.md §Policy versions).</summary>
    public static readonly Guid Version1Id = Guid.Parse("6a1f3c52-7d0e-4b8a-9b51-31a0c0de0001");

    /// <summary>Version 1 is effective from this instant so every award of the program pins it.</summary>
    public static readonly DateTime Version1EffectiveFromUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly List<ApprovalPolicyLevel> levels = [];

    private ApprovalPolicy()
    {
        BaseCurrency = string.Empty;
        EscalationRole = string.Empty;
    }

    public Guid Id { get; private set; }
    public int Version { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public string BaseCurrency { get; private set; }

    /// <summary>Calendar duration of a step before escalation or expiry (APR-011, B23-11).</summary>
    public TimeSpan StepDuration { get; private set; }

    /// <summary>Calendar delay before the reminder of a pending step (APR-011).</summary>
    public TimeSpan ReminderAfter { get; private set; }

    /// <summary>Role group that receives escalated and fallback assignments (APR-003/006/011).</summary>
    public string EscalationRole { get; private set; }

    public int MaxEscalations { get; private set; }
    public string? Reason { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>First instant an approval instance pinned this version; set once (APR-017).</summary>
    public DateTime? FirstUsedAtUtc { get; private set; }

    public IReadOnlyCollection<ApprovalPolicyLevel> Levels => levels;

    public bool IsInUse => FirstUsedAtUtc is not null;

    public string Reference => $"{PolicyType}:{Version.ToString(System.Globalization.NumberFormatInfo.InvariantInfo)}";

    /// <summary>
    /// Version 1 seed: L1 &gt; 0.00 BRL cost-center manager, L2 &gt; 10,000.00 BRL department head, L3 &gt; 100,000.00
    /// BRL <c>FinanceApprover</c> group; 120 h steps, reminder after 72 h, one escalation to <c>ProcurementManager</c>.
    /// </summary>
    public static ApprovalPolicy Version1(string financeApproverRole, string escalationRole)
    {
        var (policy, error) = Create(
            Version1Id, 1, Version1EffectiveFromUtc, "BRL",
            [
                new ApprovalPolicyLevelInput(1, 0m, ApproverResolver.CostCenterManager, null),
                new ApprovalPolicyLevelInput(2, 10_000m, ApproverResolver.DepartmentHead, null),
                new ApprovalPolicyLevelInput(3, 100_000m, ApproverResolver.RoleGroup, financeApproverRole)
            ],
            TimeSpan.FromHours(120), TimeSpan.FromHours(72), escalationRole, 1, "Policy version 1 seed (B23-11).", null,
            Version1EffectiveFromUtc, Version1EffectiveFromUtc);
        return policy ?? throw new InvalidOperationException(error!.Message);
    }

    /// <summary>
    /// Creates a version. Levels are numbered 1..n with strictly increasing thresholds, the first at 0; the reminder
    /// precedes the step deadline; the effective instant is not before <paramref name="nowUtc"/> (POL-001).
    /// </summary>
    public static (ApprovalPolicy? Policy, DomainError? Error) Create(
        Guid id, int version, DateTime effectiveFromUtc, string baseCurrency, IReadOnlyList<ApprovalPolicyLevelInput> levelInputs,
        TimeSpan stepDuration, TimeSpan reminderAfter, string escalationRole, int maxEscalations, string? reason, Guid? createdBy,
        DateTime nowUtc, DateTime earliestEffectiveUtc)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(levelInputs);
        if (version < 1)
            return (null, Validation("version", "The version is a positive number."));
        if (effectiveFromUtc.Kind != DateTimeKind.Utc || effectiveFromUtc < earliestEffectiveUtc)
            return (null, Validation("effectiveFrom", "The effective instant is a UTC instant that is not in the past (POL-001)."));
        var currency = CurrencyCodes.Normalize(baseCurrency);
        if (currency is null)
            return (null, Validation("baseCurrency", "The base currency is an ISO 4217 code."));
        if (levelInputs.Count is 0 or > MaxLevels)
            return (null, Validation("levels", $"A policy has 1 to {MaxLevels} levels."));
        var ordered = levelInputs.OrderBy(level => level.Level).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var level = ordered[index];
            if (level.Level != index + 1)
                return (null, Validation("levels", "Levels are numbered 1..n without gaps."));
            if (level.MinimumAmountExclusive < 0 || !CalculationRules.HasMinorUnitPrecision(level.MinimumAmountExclusive, currency)
                || level.MinimumAmountExclusive > CalculationRules.MaxAmount)
                return (null, Validation("levels", "Thresholds are zero or positive amounts in base currency."));
            if (index == 0 && level.MinimumAmountExclusive != 0)
                return (null, Validation("levels", "Level 1 applies to every positive amount (APR-002)."));
            if (index > 0 && level.MinimumAmountExclusive <= ordered[index - 1].MinimumAmountExclusive)
                return (null, Validation("levels", "Thresholds strictly increase with the level (APR-002)."));
            if (!Enum.IsDefined(level.Resolver))
                return (null, Validation("levels", "The resolver is CostCenterManager, DepartmentHead or RoleGroup."));
            if ((level.Resolver == ApproverResolver.RoleGroup) != !string.IsNullOrWhiteSpace(level.Role)
                || level.Role is { Length: > ApprovalPolicyLevel.RoleMaxLength })
                return (null, Validation("levels", "A RoleGroup level names one role; other resolvers name none."));
        }

        if (stepDuration < TimeSpan.FromMinutes(1) || stepDuration > TimeSpan.FromDays(90))
            return (null, Validation("stepDuration", "The step duration is 1 minute to 90 days."));
        if (reminderAfter <= TimeSpan.Zero || reminderAfter >= stepDuration)
            return (null, Validation("reminderAfter", "The reminder is positive and earlier than the step deadline."));
        if (string.IsNullOrWhiteSpace(escalationRole) || escalationRole.Length > ApprovalPolicyLevel.RoleMaxLength)
            return (null, Validation("escalationRole", "The escalation group names one role."));
        if (maxEscalations is < 0 or > 3)
            return (null, Validation("maxEscalations", "A step escalates 0 to 3 times."));
        var trimmedReason = reason?.Trim();
        if (trimmedReason is { Length: > ReasonMaxLength })
            return (null, Validation("reason", $"The reason has at most {ReasonMaxLength} characters."));

        var policy = new ApprovalPolicy
        {
            Id = id,
            Version = version,
            EffectiveFromUtc = effectiveFromUtc,
            BaseCurrency = currency,
            StepDuration = stepDuration,
            ReminderAfter = reminderAfter,
            EscalationRole = escalationRole.Trim(),
            MaxEscalations = maxEscalations,
            Reason = string.IsNullOrEmpty(trimmedReason) ? null : trimmedReason,
            CreatedBy = createdBy,
            CreatedAtUtc = nowUtc
        };
        foreach (var level in ordered)
            policy.levels.Add(new ApprovalPolicyLevel(
                DeterministicLevelId(id, level.Level), id, level.Level, level.MinimumAmountExclusive, level.Resolver, level.Role?.Trim()));
        return (policy, null);
    }

    /// <summary>APR-002: the levels whose threshold the amount in base currency exceeds, in ascending order.</summary>
    public IReadOnlyList<ApprovalPolicyLevel> RequiredLevels(decimal amountInBaseCurrency) =>
        levels.Where(level => amountInBaseCurrency > level.MinimumAmountExclusive).OrderBy(level => level.Level).ToArray();

    /// <summary>APR-017: records the first pinning instant; later calls keep it.</summary>
    public void MarkUsed(DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        FirstUsedAtUtc ??= nowUtc;
    }

    private static Guid DeterministicLevelId(Guid policyId, int level)
    {
        var bytes = policyId.ToByteArray();
        bytes[15] = (byte)level;
        return new Guid(bytes);
    }

    private static DomainError Validation(string field, string message) =>
        new("validation", DomainErrorKind.Validation, message, "APR-017", field);
}
