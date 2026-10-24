using Microsoft.Extensions.Options;

namespace Template.Infrastructure.Workflow;

/// <summary>
/// Client settings of the WorkflowFunctions internal endpoint (section <c>ApprovalWorkflowSettings</c>). <see cref="Key"/>
/// is a secret with no committed value (user secrets, <c>.env</c> or a secret store); it is never logged.
/// </summary>
public sealed class ApprovalWorkflowSettings
{
    public const string SectionName = "ApprovalWorkflowSettings";
    public const int MinimumKeyLength = 32;

    public bool Enabled { get; set; }

    /// <summary>Absolute http(s) base URL of the Functions host ending with <c>/</c> (private network only).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    /// <summary>Per-call timeout; an expired call is reported as unavailable.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Startup validation; failure messages name keys only.</summary>
public sealed class ApprovalWorkflowSettingsValidator : IValidateOptions<ApprovalWorkflowSettings>
{
    public ValidateOptionsResult Validate(string? name, ApprovalWorkflowSettings options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Success;
        var failures = new List<string>();
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !options.BaseUrl.EndsWith('/'))
            failures.Add($"{ApprovalWorkflowSettings.SectionName}:BaseUrl must be an absolute http(s) URL ending with '/' without credentials or query.");
        if (string.IsNullOrWhiteSpace(options.Key) || options.Key.Length < ApprovalWorkflowSettings.MinimumKeyLength)
            failures.Add($"{ApprovalWorkflowSettings.SectionName}:Key must be configured with at least {ApprovalWorkflowSettings.MinimumKeyLength} characters.");
        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromSeconds(60))
            failures.Add($"{ApprovalWorkflowSettings.SectionName}:Timeout must be between 0 and 60 seconds.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
