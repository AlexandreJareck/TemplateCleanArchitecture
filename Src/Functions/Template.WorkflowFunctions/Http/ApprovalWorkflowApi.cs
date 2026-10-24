using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Template.Application.Workflow;
using Template.Infrastructure.Persistence;
using Template.WorkflowFunctions.Hosting;

namespace Template.WorkflowFunctions.Http;

/// <summary>
/// Private HTTP boundary used only by the Infrastructure workflow client of the WebApi and Worker (ADR-0003 §5). Each
/// operation requires the internal key (<see cref="ApprovalWorkflowHttp.KeyHeader"/>), accepts only instance IDs derived
/// from an award ID (APR-001) and never returns the Durable management URLs or system keys. Purge is not exposed:
/// history retention (30 days, NFR) is an operator task that needs explicit authorization.
/// </summary>
public sealed class ApprovalWorkflowApi(InternalApiAuthorization authorization, IServiceScopeFactory scopes, ILogger<ApprovalWorkflowApi> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("StartApprovalWorkflow")]
    public async Task<HttpResponseData> StartAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = ApprovalWorkflowHttp.WorkflowsRoute)] HttpRequestData request,
        [DurableClient] DurableTaskClient client)
    {
        if (!authorization.IsAuthorized(request))
            return Status(request, HttpStatusCode.Unauthorized);
        ApprovalWorkflowInput? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<ApprovalWorkflowInput>(request.Body, Json, request.FunctionContext.CancellationToken);
        }
        catch (JsonException)
        {
            return Status(request, HttpStatusCode.BadRequest);
        }

        if (input is null || input.AwardId == Guid.Empty || input.InputVersion != ApprovalWorkflowInput.Version1 || string.IsNullOrWhiteSpace(input.CorrelationId))
            return Status(request, HttpStatusCode.BadRequest);

        var instanceId = ApprovalWorkflowNames.InstanceIdFor(input.AwardId);
        var existing = await client.GetInstanceAsync(instanceId, getInputsAndOutputs: false, request.FunctionContext.CancellationToken);
        if (existing is not null && IsActive(existing.RuntimeStatus))
            return await JsonAsync(request, HttpStatusCode.OK, new ApprovalWorkflowStartResponse(instanceId, "AlreadyRunning"));
        try
        {
            await client.ScheduleNewOrchestrationInstanceAsync(
                new TaskName(ApprovalWorkflowNames.Orchestrator), input, new StartOrchestrationOptions(instanceId), request.FunctionContext.CancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Grpc.Core.RpcException)
        {
            // A concurrent duplicate start won the race for the same deterministic ID (APR-001).
            logger.LogInformation("Approval workflow {InstanceId} start raced a duplicate", instanceId);
            return await JsonAsync(request, HttpStatusCode.OK, new ApprovalWorkflowStartResponse(instanceId, "AlreadyRunning"));
        }

        logger.LogInformation("Approval workflow {InstanceId} scheduled", instanceId);
        return await JsonAsync(request, HttpStatusCode.Accepted, new ApprovalWorkflowStartResponse(instanceId, existing is null ? "Started" : "Restarted"));
    }

    [Function("GetApprovalWorkflow")]
    public async Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ApprovalWorkflowHttp.WorkflowsRoute + "/{instanceId}")] HttpRequestData request,
        string instanceId,
        [DurableClient] DurableTaskClient client)
    {
        if (!authorization.IsAuthorized(request))
            return Status(request, HttpStatusCode.Unauthorized);
        if (ApprovalWorkflowNames.AwardIdFrom(instanceId) is null)
            return Status(request, HttpStatusCode.NotFound);
        var metadata = await client.GetInstanceAsync(instanceId, getInputsAndOutputs: false, request.FunctionContext.CancellationToken);
        if (metadata is null)
            return Status(request, HttpStatusCode.NotFound);
        return await JsonAsync(request, HttpStatusCode.OK, new ApprovalWorkflowStatusResponse(
            instanceId, Map(metadata.RuntimeStatus), null, metadata.CreatedAt.UtcDateTime, metadata.LastUpdatedAt.UtcDateTime));
    }

    [Function("SignalApprovalWorkflow")]
    public async Task<HttpResponseData> SignalAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = ApprovalWorkflowHttp.WorkflowsRoute + "/{instanceId}/signals")] HttpRequestData request,
        string instanceId,
        [DurableClient] DurableTaskClient client)
    {
        if (!authorization.IsAuthorized(request))
            return Status(request, HttpStatusCode.Unauthorized);
        if (ApprovalWorkflowNames.AwardIdFrom(instanceId) is null)
            return Status(request, HttpStatusCode.NotFound);
        ApprovalSignal? signal;
        try
        {
            signal = await JsonSerializer.DeserializeAsync<ApprovalSignal>(request.Body, Json, request.FunctionContext.CancellationToken);
        }
        catch (JsonException)
        {
            return Status(request, HttpStatusCode.BadRequest);
        }

        if (signal is null || signal.SignalId == Guid.Empty || string.IsNullOrWhiteSpace(signal.Kind) || signal.Kind.Length > 40)
            return Status(request, HttpStatusCode.BadRequest);
        var metadata = await client.GetInstanceAsync(instanceId, getInputsAndOutputs: false, request.FunctionContext.CancellationToken);
        if (metadata is null || !IsActive(metadata.RuntimeStatus))
            return await JsonAsync(request, HttpStatusCode.OK, new ApprovalWorkflowSignalResponse(instanceId, "NotRunning"));

        await client.RaiseEventAsync(instanceId, ApprovalWorkflowNames.SignalEvent, signal, request.FunctionContext.CancellationToken);
        return await JsonAsync(request, HttpStatusCode.OK, new ApprovalWorkflowSignalResponse(instanceId, "Delivered"));
    }

    [Function("TerminateApprovalWorkflow")]
    public async Task<HttpResponseData> TerminateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = ApprovalWorkflowHttp.WorkflowsRoute + "/{instanceId}/terminate")] HttpRequestData request,
        string instanceId,
        [DurableClient] DurableTaskClient client)
    {
        if (!authorization.IsAuthorized(request))
            return Status(request, HttpStatusCode.Unauthorized);
        if (ApprovalWorkflowNames.AwardIdFrom(instanceId) is null)
            return Status(request, HttpStatusCode.NotFound);
        var metadata = await client.GetInstanceAsync(instanceId, getInputsAndOutputs: false, request.FunctionContext.CancellationToken);
        if (metadata is null || !IsActive(metadata.RuntimeStatus))
            return Status(request, HttpStatusCode.NotFound);
        await client.TerminateInstanceAsync(instanceId, "terminated-by-administrator", request.FunctionContext.CancellationToken);
        logger.LogWarning("Approval workflow {InstanceId} terminated administratively", instanceId);
        return await JsonAsync(request, HttpStatusCode.OK, new ApprovalWorkflowSignalResponse(instanceId, "Terminated"));
    }

    /// <summary>Liveness: the worker process answers (no key, no detail).</summary>
    [Function("WorkflowHealthLive")]
    public Task<HttpResponseData> LiveAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/live")] HttpRequestData request) =>
        JsonAsync(request, HttpStatusCode.OK, new { status = "Healthy" });

    /// <summary>Readiness: the application database and the Durable task hub (Azurite / Azure Storage) answer.</summary>
    [Function("WorkflowHealthReady")]
    public async Task<HttpResponseData> ReadyAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/ready")] HttpRequestData request,
        [DurableClient] DurableTaskClient client)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(request.FunctionContext.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (!await scope.ServiceProvider.CanConnectToApplicationDatabaseAsync(timeout.Token))
                return await JsonAsync(request, HttpStatusCode.ServiceUnavailable, new { status = "Unhealthy", dependency = "application-database" });
            await client.GetInstanceAsync("health-probe", getInputsAndOutputs: false, timeout.Token);
            return await JsonAsync(request, HttpStatusCode.OK, new { status = "Healthy" });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("Workflow readiness failed with {ErrorType}", exception.GetType().Name);
            return await JsonAsync(request, HttpStatusCode.ServiceUnavailable, new { status = "Unhealthy", dependency = "task-hub" });
        }
    }

    private static bool IsActive(OrchestrationRuntimeStatus status) =>
        status is OrchestrationRuntimeStatus.Running or OrchestrationRuntimeStatus.Pending or OrchestrationRuntimeStatus.Suspended;

    private static string Map(OrchestrationRuntimeStatus status) => status switch
    {
        OrchestrationRuntimeStatus.Running => "Running",
        OrchestrationRuntimeStatus.Pending => "Pending",
        OrchestrationRuntimeStatus.Completed => "Completed",
        OrchestrationRuntimeStatus.Failed => "Failed",
        OrchestrationRuntimeStatus.Terminated => "Terminated",
        OrchestrationRuntimeStatus.Suspended => "Suspended",
        _ => "Unknown"
    };

    private static HttpResponseData Status(HttpRequestData request, HttpStatusCode status) => request.CreateResponse(status);

    private static async Task<HttpResponseData> JsonAsync<T>(HttpRequestData request, HttpStatusCode status, T body)
    {
        var response = request.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, Json));
        return response;
    }
}
