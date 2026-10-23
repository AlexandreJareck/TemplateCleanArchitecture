#nullable enable
using Microsoft.Extensions.Options;

namespace Template.Worker;

public sealed class OutboxDispatchSettings
{
    public const string SectionName = nameof(OutboxDispatchSettings);
    public int BatchSize { get; set; } = 50;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxAttempts { get; set; } = 8;
    public TimeSpan FirstRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan DispatchedRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    public static bool IsValid(OutboxDispatchSettings value) =>
        value.BatchSize is >= 1 and <= 500
        && value.PollInterval > TimeSpan.Zero
        && value.LeaseDuration > value.PollInterval
        && value.MaxAttempts is >= 1 and <= 100
        && value.FirstRetryDelay > TimeSpan.Zero
        && value.MaxRetryDelay >= value.FirstRetryDelay
        && value.DispatchedRetention > TimeSpan.Zero
        && value.CleanupInterval > TimeSpan.Zero;
}

public sealed class OutboxDispatchSettingsValidator : IValidateOptions<OutboxDispatchSettings>
{
    public ValidateOptionsResult Validate(string? name, OutboxDispatchSettings options) =>
        OutboxDispatchSettings.IsValid(options)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("OutboxDispatchSettings has invalid bounds.");
}
