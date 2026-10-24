using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Workflow;

namespace Template.WorkflowFunctions.Hosting;

/// <summary>Correlation of one activity invocation: the orchestration input's correlation ID (NFR-AUD-3).</summary>
public sealed class WorkflowRequestContext : IRequestContext
{
    private string? correlationId;

    public string CorrelationId => correlationId ??= Guid.NewGuid().ToString("N");

    public void Use(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            correlationId = value.Length <= 128 ? value : value[..128];
    }
}

/// <summary>The Functions host acts only as the system actor: no user, no permission; it never authorizes by permission.</summary>
public sealed class WorkflowSystemUser : IAuthenticatedUserService
{
    public string? UserId => null;
    public string? UserName => null;
}

public sealed class WorkflowSystemPermissions : IPermissionReader
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(None);
}

/// <summary>
/// Shared internal key of the private HTTP boundary (section <c>ApprovalWorkflowSettings</c>, key <c>Key</c>; secret, never
/// committed or logged). The same value configures the WebApi and Worker clients.
/// </summary>
public sealed class WorkflowHostSettings
{
    public const string SectionName = "ApprovalWorkflowSettings";
    public const int MinimumKeyLength = 32;

    public string Key { get; set; } = string.Empty;
}

public sealed class WorkflowHostSettingsValidator : IValidateOptions<WorkflowHostSettings>
{
    public ValidateOptionsResult Validate(string? name, WorkflowHostSettings options) =>
        string.IsNullOrWhiteSpace(options.Key) || options.Key.Length < WorkflowHostSettings.MinimumKeyLength
            ? ValidateOptionsResult.Fail($"{WorkflowHostSettings.SectionName}:Key must be configured with at least {WorkflowHostSettings.MinimumKeyLength} characters.")
            : ValidateOptionsResult.Success;
}

/// <summary>Constant-time check of <see cref="ApprovalWorkflowHttp.KeyHeader"/> (SHA-256 of both values, fixed-time compare).</summary>
public sealed class InternalApiAuthorization(IOptions<WorkflowHostSettings> settings)
{
    public bool IsAuthorized(HttpRequestData request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Headers.TryGetValues(ApprovalWorkflowHttp.KeyHeader, out var values) || values.FirstOrDefault() is not { Length: > 0 } presented)
            return false;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(settings.Value.Key));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
