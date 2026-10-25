# Observability and diagnostics

Operational view of the local reference stack (Phase 33). Every command here was run against the Compose stack; key and variable names only, never values.

## Correlation across processes

| Hop | How the correlation ID travels |
|---|---|
| Browser → WebApi | `TraceIdentifier` becomes the logging `CorrelationId` scope and the Problem Details `traceId` (Phase 13). |
| WebApi → PostgreSQL | Audit records and domain history store the same `CorrelationId` (`IRequestContext`). |
| WebApi → Outbox → RabbitMQ | The integration envelope carries `CorrelationId`; the Worker dispatcher logs inside a scope with it. |
| RabbitMQ → Worker consumer | The consumer validates the envelope's `CorrelationId` (`CorrelationId.Normalize`) and adopts it for the effect's scope (`IInboundCorrelation`), so the effect's logs and audit records share the originating request's ID. |
| Worker sweeps and report attempts | One ID per unit of work (`WorkerRequestContext` is stable per scope). |
| Worker/WebApi → WorkflowFunctions | `HttpApprovalWorkflowClient` sends the correlation header; activities log with it (Phase 31). |

Search one ID in Kibana (data stream `logs-api-example-*`, field `CorrelationId`) or in the console logs of each container.

## Metrics

Instruments use `System.Diagnostics.Metrics` and can be read with `dotnet-counters` or any OpenTelemetry collector. No exporter is configured in the committed profiles.

| Meter | Instruments |
|---|---|
| `Template.Procurement` | domain events written, optimistic-concurrency conflicts (WebApi and Worker use cases) |
| `Template.Reports` | report attempt duration and outcomes |
| `Template.Worker` | `worker.outbox_dispatched`, `worker.outbox_dispatch_failures`, `worker.outbox_dispatch_latency` (s, event → broker confirmation), `worker.sweep_items`, `worker.sweep_failures` |

Tags carry event types, sweep names and outcomes only, never record identifiers, names or amounts.

```powershell
dotnet-counters monitor --name Template.Worker --counters Template.Worker,Template.Reports,Template.Procurement
```

## Health endpoints

| Process | Liveness | Readiness | Required / optional dependencies |
|---|---|---|---|
| WebApi | `/health/live` | `/health/ready` | required: application and Identity databases; optional (degraded): cache, broker, Elasticsearch, file storage |
| Worker | `/health/live` | `/health/ready` | required: application database; optional (degraded): broker, file storage |
| WorkflowFunctions | `/api/health/live` | `/api/health/ready` | required: application database and the Durable task hub (a status query) |

Startup is covered by readiness: each host applies migrations (WebApi seeds) before Kestrel starts listening, so a container is not ready until startup work has finished. Compose health checks use these endpoints.

## Diagnostics

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\procurement-diagnostics.ps1
```

It runs `scripts/diagnostics/procurement-health.sql` in a read-only transaction and prints:

- **Outbox**: pending, poisoned, age of the oldest pending event, failures grouped by event type and failure code;
- **Inbox**: received but not completed entries per consumer;
- **Reports**: jobs by status with the highest attempt count, failures by safe failure code;
- **Approvals**: instances by status;
- **Notifications**: delivery rows by status and template;
- **RabbitMQ**: ready and unacknowledged messages of every `procurement.*` queue and dead-letter queue;
- **Readiness** of WebApi, Worker and WorkflowFunctions.

Interpretation:

| Symptom | Likely cause | Next step |
|---|---|---|
| Outbox `pending` grows, `oldest_pending_seconds` rises | broker down or Worker stopped | check `rabbitmq` and `worker` containers; events stay safe in PostgreSQL and drain after recovery |
| Outbox `poisoned` > 0 | an event failed `MaxAttempts` publishes | inspect `LastFailureCode`; fix the cause, then re-queue deliberately (no automatic purge) |
| A `.dead-letter` queue has messages | a consumer exhausted its retries or the contract was incompatible | inspect in RabbitMQ Management (`http://localhost:15672`); the Inbox guarantees a re-delivered message applies once |
| Report jobs `Failed` | renderer or storage failure | failure code in the job; Worker readiness shows storage degradation |
| Approval instances stuck `Pending` | lost start or signal | the Worker reconciliation sweep repairs every 5 minutes; inspect the orchestration in the Durable Functions Monitor (`http://localhost:7072`, hub `TemplateApprovals`) |

## Scheduled work

| Work | Host | Interval | Rule |
|---|---|---|---|
| RFQ close at the deadline | Worker `ScheduledSweepService` | `ScheduledSweepSettings:Interval` (1 min) | SRC-012 |
| Requisition expiry and reservation release | Worker `ScheduledSweepService` | same | REQ-009, BUD-007 |
| Approval reminders, escalation, expiry | WorkflowFunctions durable timers | policy v1: reminder at 72 h, escalation at 120 h, expiry after a second 120 h | APR-011 |
| Approval reconciliation | Worker `ApprovalReconciliationService` | 5 min | APR-016 |
| Report and file retention | Worker `ReportWorkerService` | 15 min | RPT/FIL retention |
| Dispatched Outbox cleanup | Worker `OutboxDispatcherService` | 1 h | 30-day retention |

Sweeps are idempotent and retry optimistic conflicts, so several Worker instances can run them at once (D33-1).
