namespace Template.WebApi.Infrastructure.HealthChecks;

/// <summary>
/// Tags selecting which registrations each health endpoint runs. <see cref="Legacy"/> keeps <c>/health</c> on the
/// checks it ran before Phase 13; <see cref="Live"/> checks never touch the network; <see cref="Ready"/> checks are
/// either <see cref="Required"/> (failure makes the process unready) or <see cref="Optional"/> (failure only degrades).
/// </summary>
public static class HealthTags
{
    public const string Legacy = "legacy";
    public const string Live = "live";
    public const string Ready = "ready";
    public const string Required = "required";
    public const string Optional = "optional";
}
