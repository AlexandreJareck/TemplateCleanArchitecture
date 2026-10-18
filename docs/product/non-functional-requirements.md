# Non-functional requirements

Targets apply to the local reference deployment (single WebApi, Worker and Functions instance each, PostgreSQL, RabbitMQ, Redis, Azurite, on a developer workstation with Docker). They are acceptance thresholds for Phase 33 and for the phase that introduces each capability. They are not production SLAs, and nothing here claims legal or regulatory certification.

## 1. Security

| ID | Requirement |
|---|---|
| NFR-SEC-1 | No secret in committed files; `scripts/secret-scan.ps1` and `CommittedSecretsTests` stay green ([configuration-secrets.md](../configuration-secrets.md)). New secrets (blob connection, SMTP, CSRF HMAC key, Functions keys) are added to the secret-key list with key names only. |
| NFR-SEC-2 | Session design per [api-guidelines.md](api-guidelines.md#7-browser-session-and-csrf) and SEC-001..010; tokens, cookies and hashes are never logged or returned except the access token in the login/refresh body. |
| NFR-SEC-3 | Every endpoint declares a permission policy or `[AllowAnonymous]` explicitly; an architecture test fails on a procurement controller action without either. |
| NFR-SEC-4 | Input limits: JSON body ≤ 1 MB; uploads per `FilePolicy`; list `pageSize` ≤ 100; string fields have explicit maximum lengths. |
| NFR-SEC-5 | Security headers on API responses: `X-Content-Type-Options: nosniff`, `Cache-Control: no-store` for authenticated JSON, `Referrer-Policy: no-referrer`; the SPA host adds a CSP without `unsafe-eval` (exact policy in Phase 26B/33). |
| NFR-SEC-6 | Dependency, image and license audits per existing policy (high/critical vulnerabilities block; license allow-list). |
| NFR-SEC-7 | Durable orchestration input/history, Outbox payloads and logs carry IDs and minimal data only (no personal or commercial detail beyond what consumers need). |
| NFR-SEC-8 | Threat review at Phase 24 (session), 30 (files/reports) and 33 (release) using STRIDE-style tables in the phase report. |

## 2. Auditability

| ID | Requirement |
|---|---|
| NFR-AUD-1 | AUD-001..004: every business state change, waiver and security event has an audit record written atomically. |
| NFR-AUD-2 | Each requisition detail exposes a complete timeline: state changes, reservation changes, RFQ/quote/award events, approval decisions, PO and receipts, with actor, UTC time (rendered in the organization time zone) and reason. |
| NFR-AUD-3 | Correlation: `CorrelationId` = HTTP `traceId` for synchronous requests; Outbox envelopes carry `correlationId` and `causationId`; Worker, Durable activities and notifications continue the chain (Phase 28/31/33). |

## 3. Availability and degradation

| Dependency | Classification | Behavior when unavailable |
|---|---|---|
| PostgreSQL (application, identity) | Required | `/health/ready` unhealthy; API returns 503 via readiness; no partial writes |
| RabbitMQ | Optional for HTTP | Commands still commit (Outbox); events queue until recovery; readiness degraded |
| Worker | Optional for HTTP | Backlog grows; notifications/reports/PO drafts delayed; visible as pending states |
| WorkflowFunctions | Optional for HTTP | Award submission still commits; approval start waits in the Outbox; decisions on started instances return `503 approval.workflow-unavailable` without changing state |
| Azurite/Blob | Optional for core flows | Upload/download/report output fail with explicit errors; procurement transitions unaffected |
| Redis | Optional | Cache misses (existing behavior) |
| Elasticsearch | Optional | Existing sample behavior unchanged |
| BrasilAPI, BCB | Optional | INT-001; manual entry or manual rate waiver |
| Mailpit/SMTP | Optional | Notification marked failed and retried; business state unaffected |

Graceful shutdown: WebApi drains in-flight requests; Worker stops consuming, finishes or abandons (requeues) the current message within 30 s; Functions follow the host lifecycle.

## 4. Performance budgets

Measured in Phase 33 with the synthetic demo dataset: 50 cost centers, 1,000 suppliers, 5,000 catalog items, 20,000 requisitions (average 5 lines), 5,000 RFQs, 15,000 quotes, 4,000 POs, 8,000 receipts.

| ID | Operation | Budget |
|---|---|---|
| NFR-PERF-1 | List endpoints (page ≤ 50, indexed filters) | p95 ≤ 300 ms server time |
| NFR-PERF-2 | Detail reads | p95 ≤ 200 ms |
| NFR-PERF-3 | Commands (submit, award, decide, issue, receipt) | p95 ≤ 500 ms excluding asynchronous follow-ups |
| NFR-PERF-4 | Concurrent reservation | 50 parallel submissions against one allocation: zero over-reservation, all complete ≤ 5 s |
| NFR-PERF-5 | Outbox dispatch | backlog of 1,000 events drained ≤ 60 s by one Worker |
| NFR-PERF-6 | Report generation | requisition/comparison/PO PDF ≤ 15 s; XLSX with 10,000 rows ≤ 30 s |
| NFR-PERF-7 | Approval start | award submission to first step assigned ≤ 10 s with all hosts running |
| NFR-PERF-8 | SPA | initial JavaScript transfer ≤ 350 kB gzip; route chunks ≤ 150 kB gzip; Largest Contentful Paint ≤ 2.5 s on the local production build (exact Angular budgets set in 26B) |

## 5. Data retention

Assumed defaults chosen by the blueprint; they are not legal advice and can be changed by owner decision.

| Data | Retention | Mechanism |
|---|---|---|
| Business records (requisitions, RFQs, quotes, approvals, POs, receipts, reservations) | 5 years after closure, then eligible for an owner-approved purge procedure (not automated in the first release) | Never hard-deleted by the application |
| Audit records | Same as the owning record; security events 1 year | Append-only |
| Attachments | Same as the owning record | FIL-004/005 |
| Report outputs | 7 days | FIL-005, RPT-006 |
| Pending uploads | 24 h | FIL-005 |
| Idempotency records | 24 h | Cleanup job |
| Outbox (dispatched) / Inbox (completed) | 7 days | Cleanup job |
| Session families | Until absolute expiry + 30 days (revoked/expired kept for reuse detection and audit) | Cleanup job |
| Application logs | 30 days in Elasticsearch data stream (local ILM, Phase 33) | — |
| Durable history | Purged 30 days after instance completion | Functions purge (Phase 31/33) |

## 6. Recovery objectives

| ID | Requirement |
|---|---|
| NFR-REC-1 | RPO ≤ 24 h and RTO ≤ 4 h for the local reference stack through documented `pg_dump`/restore of both databases plus blob container copy; rehearsed with synthetic data in Phase 33 (same discipline as `scripts/postgres16-patch-rehearsal.ps1`). |
| NFR-REC-2 | Every new migration is additive and has a documented rollback (previous image + restore) per repository rules. |
| NFR-REC-3 | Restoring the database without blob content leaves files `Unavailable` (metadata present) but never corrupts procurement state; restoring blobs without metadata leaves orphans detectable by FIL-005. |
| NFR-REC-4 | RabbitMQ and Durable state are rebuildable: after restore, an operator command republishes undispatched Outbox records and the reconciliation query (APR-016) reports approval instances to restart. |

## 7. Observability

- Structured logs with the existing `LogEvents` IDs plus module-specific ranges; `CorrelationId` scope everywhere; `SensitiveDataRedactionEnricher` applies.
- Metrics (Phase 33): requests by route/status, command latency, Outbox backlog and age, Inbox failures, dead-letter count, report job durations/failures, approval instances by state/overdue, notification failures.
- Health: WebApi, Worker and Functions expose liveness and readiness with required/optional classification per section 3.
- Never log: tokens, cookies, passwords, hashes, full addresses, contact details, quote prices tied to supplier names, attachment content.

## 8. Localization

- pt-BR is the default UI and API message culture; English structure ready (D23-10). Problem `title`/`detail` localized through the existing `ITranslator`; `code` values are culture-invariant.
- Dates and numbers formatted with the user culture in the UI; money with the currency's code/symbol; the API always uses invariant formats.
- The current WebApi `SupportedCultures` (`pt-BR`, `en`, `fa`) stays unchanged for sample endpoints.

## 9. Privacy and data classification

| Classification | Fields | Handling |
|---|---|---|
| Personal data | user names, display names, emails; supplier contact names, emails, phones; addresses of individuals (sole proprietors) | Read only with the module's read permission; excluded from logs, Outbox payloads and Durable inputs; included in reports only when the report purpose needs it |
| Commercially sensitive | quote prices, terms and rankings; awarded amounts; budget allocations | SRC-021, BUD-012, PUR-011; reports require data permission (RPT-002) |
| Internal | requisition purpose and line descriptions | Read per REQ-011; not logged |
| Public business identifiers | supplier legal name, CNPJ | May appear in events and documents |

Enrichment calls send only the tax ID or postal code required by the provider. No production data is used in tests or rehearsals; demo data is clearly synthetic.
