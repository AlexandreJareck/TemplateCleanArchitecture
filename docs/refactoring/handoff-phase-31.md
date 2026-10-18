# Phase 31 Execution Handoff — Azure Durable Approval Workflow

## Mission

Implement the first long-running stateful workflow with Azure Durable Functions: deterministic, multi-step human approval of procurement decisions with external events, durable timers, reminders/escalation, rejection, cancellation, retries, compensation, versioning, observability, and frontend approval inbox/timeline.

## Required context and skills

Read all instructions, Phase 23 workflow/permission/event/ADR decisions, Phases 24/27–30 reports, Microsoft Durable Functions current official documentation, application state transitions, Outbox/Inbox, report/storage security, frontend conventions, Compose and validation scripts.

Use `package-upgrade` for Functions/Durable/Azure package families, `integration-adapter`, `api-contract-change`, `architecture-boundary-check`, `dotnet-change-verification`, and `ef-migration` only for approved application metadata changes. Verify execution-time .NET isolated support, Functions runtime, hosting/storage providers, emulator/Azurite, package compatibility, deployment/versioning guidance and licenses from official sources.

## Preconditions and hard stops

- Phases 23–30 are complete and green.
- Approval policy, steps, thresholds, approver resolution, separation of duties, deadlines, delegation, escalation and compensation are approved.
- Confirm the supported TFM for the Functions isolated worker. It may target a different supported .NET TFM from the WebApi if required; do not force an unsupported runtime.
- Record HEAD/dirty tree and baselines.
- No real Azure provisioning, subscription mutation, role assignment, secret creation, or deployment without explicit authorization.

## Architecture

Create `Src/Functions/Template.WorkflowFunctions` (or approved equivalent) as a separate composition root. It may reference Application and approved Infrastructure adapters, but Domain/Application must not reference Azure Functions/Durable packages.

Add an Application-owned approval-workflow client port used by WebApi/use cases. Its Infrastructure adapter communicates with the Functions boundary using the approved authenticated mechanism. Do not expose Durable management URLs or system keys directly to the browser.

The application database is authoritative for requisition/RFQ/selection/approval business state and audit. Durable history is authoritative only for orchestration execution. Reconciliation must detect and safely repair/report divergence.

## Orchestration requirements

Implement an approval orchestration that:

1. receives a small versioned input containing IDs and policy/version references;
2. creates/records the approval instance through an Activity;
3. resolves the next approver through an Activity;
4. queues a notification through an Activity/application port;
5. waits for `Approved`, `Rejected`, `Cancelled`, or approved delegation events;
6. races external events against durable deadlines;
7. reminds/escalates according to policy;
8. advances through levels or completes rejection/cancellation;
9. confirms or releases budget through idempotent application Activities;
10. publishes the final outcome and records correlation.

Orchestrator code must be deterministic and replay-safe: no direct database/network/file/log side effects, ambient current time, random IDs, thread blocking, or nondeterministic collection behavior. Use replay-safe logging. Activities own side effects, cancellation boundaries, retry classification, and idempotency.

The orchestrator coordinates durable time and events; it is not the owner of approval policy. Application/Domain activities evaluate `APR-*` rules and return explicit decisions. Do not make the orchestration history the only place where a threshold, separation-of-duty rule, waiver, or business outcome can be understood.

Define deterministic instance IDs, duplicate start/event behavior, late/duplicate events, restart/replay, poison activity, timeout, terminate/purge authorization, `ContinueAsNew` if history can grow, and safe deployment/versioning while instances are running.

Enforce the approved `APR-*` rules in Application/Domain activities, not inside orchestration branching alone:

- snapshot or reference an immutable approval-policy version when the workflow starts;
- derive required levels deterministically from approved amount/currency/category/cost-center inputs;
- the requester cannot provide final approval for their own requisition, and an actor cannot approve a step not currently assigned to them;
- approval, rejection, delegation, escalation, cancellation, and administrative intervention require distinct permissions and append-only audit records;
- rejection and administrative cancellation require a reason; delegation cannot target the same actor and must preserve accountability;
- material requisition, quote, amount, supplier, or policy-input changes invalidate the pending approval and require an explicit restart/revision path;
- duplicate, late, stale, or out-of-order decisions return stable outcomes and never apply the business effect twice;
- do not add a fully dynamic workflow designer: use a small seeded/versioned policy model sufficient to demonstrate threshold-based corporate approval.

## Local and operational environment

- Run locally with the currently supported Azurite or Durable Task emulator/provider selected by ADR.
- Add the Functions host to an isolated Compose/development profile without exposing secrets.
- Add health/diagnostic status appropriate to Functions and orchestration instances.
- Propagate correlation/causation across WebApi, Outbox, Worker, Functions, activities, and notifications.
- Keep orchestration input/history free of secrets and unnecessary personal/commercial data because history is persisted.

## API and frontend

Add permission-protected v2 endpoints to start/query/cancel where appropriate and to submit approve/reject/delegate decisions with idempotency and optimistic concurrency. Implement the approval UI through frontend Application workflow use cases, Infrastructure API adapters, and Angular Presentation; generated Durable/API types must not enter the inner client layers. Add approval inbox, detail/timeline, approve/reject reason, deadline/escalation display, stale-action handling, and accessible/E2E behavior. Backend policy remains authoritative.

## Tests and validation

Add a rule-to-test matrix for every applicable `APR-*` rule. Add deterministic orchestrator unit tests for every branch/timer/event/retry/compensation; Activity/application tests for thresholds, maker-checker separation, assignment, required reasons, policy version and invalidation; fake workflow-client tests; local emulator/Azurite integration for start, wait, external event, restart/replay, duplicate/late event, timeout/escalation, cancellation and version deployment strategy; PostgreSQL/Rabbit coordination tests; functional API permissions/contracts; frontend E2E.

Run sequential .NET/frontend gates, current Functions build/test commands, package/license/vulnerability audits, secret scan, isolated Compose/emulator restart, PostgreSQL/Rabbit suites, OpenAPI/generated-client, coverage, Graphify, and `git diff --check`.

## Out of scope

- Generic workflow designer, Durable Entities unless separately justified, Azure Service Bus migration, AI approval, public approval links, real email/SMS provider, cloud deployment, purchase orders/receipts, or storing business truth only in Durable history.

## Acceptance criteria

- A real approved procurement scenario completes through multiple human steps and timeouts.
- Replay, duplicate/late events, restarts, and retries do not duplicate business effects.
- Rejection/cancellation/expiry compensates budget exactly once.
- Long-running-version deployment and rollback are documented and tested to the feasible local extent.
- No Azure/vendor type crosses into Domain/Application.
- Required gates pass; otherwise the phase is `Not ready`.

## Report and final response

Create `docs/refactoring/phase-31-report.md` with runtime/package research, project graph, workflow diagram, determinism review, state ownership, security, instance/version strategy, tests/counts, emulator evidence, Compose, contracts/screens, rollback, risks and gates. Lead with readiness and the proven orchestration journey.

Do not commit/push/merge, change branches/worktrees, open a PR, deploy/provision Azure, or delete orchestration/storage resources without explicit authorization.
