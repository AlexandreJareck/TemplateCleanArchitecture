# ADR-0003: Durable Functions only for the long-running approval workflow

- Status: Accepted (Phase 23, 2026-10-18); implemented in Phase 31; provider/TFM details by spike SP-3
- Decision owners: D23-8, D23-9, D23-13

## Context

Award approval waits for human decisions for days, with reminders, escalation and expiry (APR-011). Implementing durable timers and external-event waits in the Worker would mean building a workflow engine. The roadmap selected Azure Durable Functions (.NET isolated) as the orchestration host, run locally with an emulator/provider and never deployed to Azure without authorization.

## Decision

1. Create `Src/Functions/Template.WorkflowFunctions` as a separate composition root (.NET isolated worker on the TFM officially supported at Phase 31; it may differ from WebApi).
2. Durable Functions is used **only** for approval orchestration (one orchestration type, versioned) and, from Phase 33, for recurring timer triggers (RFQ deadline close, requisition expiry, cleanup sweeps) that call Application use cases.
3. The PostgreSQL application database is the business source of truth. Durable history is process state only. Orchestrator code is deterministic: no I/O, clock, random or logging side effects; activities call Application use cases that enforce APR rules and are idempotent.
4. Orchestration inputs contain IDs, policy references and amounts needed for routing only; no personal data or secrets (NFR-SEC-7).
5. Domain and Application never reference Functions/Durable packages. Application owns `IApprovalWorkflowClient` (start, raise decision, terminate, query status); its Infrastructure adapter talks to the Functions host through an authenticated internal mechanism selected in Phase 31. The browser never sees Durable management URLs or keys.
6. Instance ID = award ID; duplicate starts, late events and replays are no-ops by design (APR-001, APR-014).
7. A reconciliation query lists divergence between database approval state and Durable runtime status (APR-016).
8. No Azure resource is provisioned; Phase 33 adds Bicep (or the approved alternative) validated by syntax/what-if only.

## Alternatives

- **Worker + database timers**: no new runtime, but reimplements timers/escalation/replay. Rejected for the approval process; acceptable for simple sweeps if Functions timers prove impractical locally (decision recorded in Phase 33 if so).
- **Durable for every async task**: overloads orchestration history with reports and notifications. Rejected (ADR-0002 covers them).
- **Durable Entities**: not needed. Rejected unless separately justified.

## Consequences

- An additional host, local emulator/storage provider and test tier.
- Versioning of running orchestrations must be planned before deployment changes (Phase 31 report).
- If Functions is unavailable, award submission still commits and the start waits in the Outbox (NFR availability table).
