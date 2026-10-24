using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Template.Application.Workflow;

namespace Template.WorkflowFunctions.Orchestration;

/// <summary>Functions binding of <see cref="ApprovalOrchestration"/>; the name is part of the Durable contract.</summary>
public static class ApprovalOrchestratorFunction
{
    [Function(ApprovalWorkflowNames.Orchestrator)]
    public static Task<string> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context) =>
        ApprovalOrchestration.RunAsync(context, context.GetInput<ApprovalWorkflowInput>());
}
