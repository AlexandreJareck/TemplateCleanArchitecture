using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Approvals;
using Template.Application.Workflow;

namespace Template.Infrastructure.Workflow;

/// <summary>
/// <see cref="IApprovalWorkflowClient"/> over the WorkflowFunctions internal HTTP endpoint (ADR-0003 §5). Every request
/// carries the shared internal key in <see cref="ApprovalWorkflowHttp.KeyHeader"/>; the Durable management API and its
/// system keys are never used or exposed. Network errors, timeouts, server errors and rejected credentials all surface
/// as <see cref="ApprovalWorkflowUnavailableException"/>; logs carry the operation and status only, never the key.
/// </summary>
public sealed class HttpApprovalWorkflowClient(HttpClient client, IOptions<ApprovalWorkflowSettings> options, ILogger<HttpApprovalWorkflowClient> logger)
    : IApprovalWorkflowClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ApprovalWorkflowStartResult> StartAsync(ApprovalWorkflowInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var response = await SendAsync<ApprovalWorkflowStartResponse>(
            HttpMethod.Post, ApprovalWorkflowHttp.WorkflowsRoute, input, input.CorrelationId, "start", allowNotFound: false, cancellationToken);
        return Enum.TryParse<ApprovalWorkflowStartResult>(response!.Result, out var result)
            ? result
            : throw new ApprovalWorkflowUnavailableException("The workflow host returned an unknown start result.");
    }

    public async Task<ApprovalWorkflowSignalResult> SignalAsync(Guid awardId, ApprovalSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var response = await SendAsync<ApprovalWorkflowSignalResponse>(
            HttpMethod.Post, $"{ApprovalWorkflowHttp.InstanceRoute(ApprovalWorkflowNames.InstanceIdFor(awardId))}/signals", signal, signal.CorrelationId,
            "signal", allowNotFound: true, cancellationToken);
        return response is not null && Enum.TryParse<ApprovalWorkflowSignalResult>(response.Result, out var result) ? result : ApprovalWorkflowSignalResult.NotRunning;
    }

    public async Task<ApprovalWorkflowStatus> GetStatusAsync(Guid awardId, CancellationToken cancellationToken)
    {
        var instanceId = ApprovalWorkflowNames.InstanceIdFor(awardId);
        var response = await SendAsync<ApprovalWorkflowStatusResponse>(
            HttpMethod.Get, ApprovalWorkflowHttp.InstanceRoute(instanceId), null, null, "status", allowNotFound: true, cancellationToken);
        if (response is null)
            return new ApprovalWorkflowStatus(instanceId, ApprovalWorkflowRuntimeStatus.NotFound, null, null, null);
        return new ApprovalWorkflowStatus(
            response.InstanceId,
            Enum.TryParse<ApprovalWorkflowRuntimeStatus>(response.RuntimeStatus, out var status) ? status : ApprovalWorkflowRuntimeStatus.Unknown,
            response.Version, response.CreatedAt, response.LastUpdatedAt);
    }

    public async Task<bool> TerminateAsync(Guid awardId, string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var response = await SendAsync<ApprovalWorkflowSignalResponse>(
            HttpMethod.Post, $"{ApprovalWorkflowHttp.InstanceRoute(ApprovalWorkflowNames.InstanceIdFor(awardId))}/terminate",
            new ApprovalWorkflowTerminateRequest(reason), null, "terminate", allowNotFound: true, cancellationToken);
        return response is not null;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method, string route, object? body, string? correlationId, string operation, bool allowNotFound, CancellationToken cancellationToken)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.Timeout);
        using var request = new HttpRequestMessage(method, route);
        request.Headers.TryAddWithoutValidation(ApprovalWorkflowHttp.KeyHeader, options.Value.Key);
        if (correlationId is not null)
            request.Headers.TryAddWithoutValidation(ApprovalWorkflowHttp.CorrelationHeader, correlationId);
        if (body is not null)
            // Buffered with a Content-Length: the Functions HTTP worker does not read chunked request bodies.
            request.Content = new StringContent(JsonSerializer.Serialize(body, body.GetType(), Json), System.Text.Encoding.UTF8, "application/json");
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Approval workflow {Operation} answered {StatusCode}", operation, (int)response.StatusCode);
                throw new ApprovalWorkflowUnavailableException($"The workflow host answered {(int)response.StatusCode} to {operation}.");
            }

            return await response.Content.ReadFromJsonAsync<T>(Json, timeout.Token)
                ?? throw new ApprovalWorkflowUnavailableException($"The workflow host returned an empty {operation} response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Approval workflow {Operation} timed out", operation);
            throw new ApprovalWorkflowUnavailableException($"The workflow host did not answer {operation} in time.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException)
        {
            logger.LogWarning("Approval workflow {Operation} failed with {ErrorType}", operation, exception.GetType().Name);
            throw new ApprovalWorkflowUnavailableException($"The workflow host is unreachable for {operation}.", exception);
        }
    }
}
