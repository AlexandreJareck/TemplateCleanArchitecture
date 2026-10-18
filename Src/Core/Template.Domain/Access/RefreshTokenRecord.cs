using System;

namespace Template.Domain.Access;

/// <summary>
/// One refresh token issued inside a <see cref="SessionFamily"/>. Only the verifier hash is kept, never the token.
/// Replaced tokens stay so that a later presentation is recognized as reuse (SEC-003).
/// </summary>
public sealed class RefreshTokenRecord
{
    private RefreshTokenRecord()
    {
        TokenHash = string.Empty;
    }

    internal RefreshTokenRecord(Guid id, Guid familyId, string tokenHash, int sequence, DateTime issuedAtUtc)
    {
        Id = id;
        FamilyId = familyId;
        TokenHash = tokenHash;
        Sequence = sequence;
        IssuedAtUtc = issuedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; }

    /// <summary>1 for the token issued at login, then +1 per rotation.</summary>
    public int Sequence { get; private set; }

    public DateTime IssuedAtUtc { get; private set; }
    public DateTime? ReplacedAtUtc { get; private set; }

    public bool IsCurrent => ReplacedAtUtc is null;

    internal void MarkReplaced(DateTime nowUtc) => ReplacedAtUtc = nowUtc;
}
