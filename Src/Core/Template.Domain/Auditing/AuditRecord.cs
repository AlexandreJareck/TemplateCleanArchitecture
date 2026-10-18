using System;
using System.Collections.Generic;
using System.Linq;

namespace Template.Domain.Auditing;

/// <summary>
/// Append-only audit entry (AUD-001, AUD-002). It has no mutating member after creation. <see cref="Details"/>
/// accepts only <see cref="AllowedDetailKeys"/> so passwords, tokens, cookies, hashes or personal payloads cannot be
/// attached by accident (AUD-003).
/// </summary>
public sealed class AuditRecord
{
    public const int ActionMaxLength = 100;
    public const int ResourceTypeMaxLength = 100;
    public const int ResourceIdMaxLength = 100;
    public const int StateMaxLength = 50;
    public const int ReasonMaxLength = 500;
    public const int CorrelationIdMaxLength = 128;
    public const int RuleIdMaxLength = 20;
    public const int DetailValueMaxLength = 200;

    /// <summary>Detail keys allowed by AUD-003. Extend deliberately; never add credential, token, hash or address keys.</summary>
    public static readonly IReadOnlySet<string> AllowedDetailKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "reason", "sessionFamilyId", "revokedCount", "permission", "policy", "endpoint"
    };

    private AuditRecord()
    {
        Action = string.Empty;
        ResourceType = string.Empty;
        ResourceId = string.Empty;
        CorrelationId = string.Empty;
        RuleIds = [];
        Details = new Dictionary<string, string>();
    }

    public Guid Id { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    /// <summary><c>null</c> for system or anonymous actions.</summary>
    public Guid? ActorId { get; private set; }

    public string Action { get; private set; }
    public string ResourceType { get; private set; }
    public string ResourceId { get; private set; }
    public string? FromState { get; private set; }
    public string? ToState { get; private set; }
    public string[] RuleIds { get; private set; }
    public Guid? WaiverId { get; private set; }
    public string? Reason { get; private set; }
    public string CorrelationId { get; private set; }
    public string? CausationId { get; private set; }
    public IReadOnlyDictionary<string, string> Details { get; private set; }

    public static AuditRecord Create(
        Guid id,
        DateTime occurredAtUtc,
        Guid? actorId,
        string action,
        string resourceType,
        string resourceId,
        string correlationId,
        string? fromState = null,
        string? toState = null,
        IEnumerable<string>? ruleIds = null,
        Guid? waiverId = null,
        string? reason = null,
        string? causationId = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("An audit record needs an identifier.", nameof(id));
        if (occurredAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Audit instants must be UTC.", nameof(occurredAtUtc));

        var rules = (ruleIds ?? []).ToArray();
        foreach (var rule in rules)
            Require(rule, RuleIdMaxLength, nameof(ruleIds));

        var safeDetails = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in details ?? new Dictionary<string, string>())
        {
            if (!AllowedDetailKeys.Contains(key))
                throw new ArgumentException($"Audit detail key '{key}' is not allow-listed.", nameof(details));
            Require(value, DetailValueMaxLength, nameof(details));
            safeDetails[key] = value;
        }

        return new AuditRecord
        {
            Id = id,
            OccurredAtUtc = occurredAtUtc,
            ActorId = actorId == Guid.Empty ? null : actorId,
            Action = Require(action, ActionMaxLength, nameof(action)),
            ResourceType = Require(resourceType, ResourceTypeMaxLength, nameof(resourceType)),
            ResourceId = Require(resourceId, ResourceIdMaxLength, nameof(resourceId)),
            CorrelationId = Require(correlationId, CorrelationIdMaxLength, nameof(correlationId)),
            FromState = Optional(fromState, StateMaxLength, nameof(fromState)),
            ToState = Optional(toState, StateMaxLength, nameof(toState)),
            RuleIds = rules,
            WaiverId = waiverId,
            Reason = Optional(reason, ReasonMaxLength, nameof(reason)),
            CausationId = Optional(causationId, CorrelationIdMaxLength, nameof(causationId)),
            Details = safeDetails
        };
    }

    private static string Require(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new ArgumentException($"A non-empty value of at most {maxLength} characters is required.", parameterName);
        return value;
    }

    private static string? Optional(string? value, int maxLength, string parameterName) =>
        value is null ? null : Require(value, maxLength, parameterName);
}
