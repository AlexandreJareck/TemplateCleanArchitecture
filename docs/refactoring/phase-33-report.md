# Phase 33 report — Operational readiness and product release gate

Date: 2026-10-25 (America/Sao_Paulo). Scope: [handoff-phase-33.md](handoff-phase-33.md).

## Release decision

**Not ready**

Every functional, security, accessibility, recovery, observability and CI gate passed against the final workspace, including the complete SC-01 journey through the browser on the real stack and a green hosted GitHub Actions run. One required gate is **Blocked**: the volume performance budgets NFR-PERF-1/2/3/5 and the 10,000-row XLSX of NFR-PERF-6 are defined against a synthetic dataset (20,000 requisitions, 15,000 quotes, 4,000 orders, …) and a load harness that do not exist yet. They were not measured on a smaller dataset because the handoff forbids defining success after measurement. Everything else needed for the release is in place; the release becomes **Ready** once that harness exists and the budgets pass (see "Remaining work").

What is proven:

- **The complete product journey works on the real stack.** `Client/Template.WebApp/e2e-real` drives SC-01 in Chromium against the local Compose stack (no mocks): requester creates and submits a requisition (budget reserved) → buyer starts a competitive RFQ, invites three suppliers, publishes, records three quotes, closes early and submits the award → `demo.manager` decides level 1 and `demo.department-head` level 2 through the Durable orchestration → buyer issues the order, the Worker renders its PDF and notifies the supplier → receiver posts the full receipt and the order completes → requester generates the requisition PDF and sees the closed history. axe reports no violation on any screen and no Content-Security-Policy violation occurred. 1/1 passed in 37.8 s.
- **Recurring work runs.** RFQ close at the deadline (SRC-012) and requisition expiry with reservation release (REQ-009) run every minute in the Worker; approval reminders, escalation and expiry stay durable timers (Phase 31); retention passes stay in the Worker.
- **CI exists and passed on GitHub.** `ci` run 40199382062 on commit `1554f62`: backend, frontend and repository jobs all green on `ubuntu-24.04`.
- **Recovery is rehearsed.** Both databases and the blob data restored into isolated containers with identical row counts and hashes in about 20 s (RTO budget 4 h).
- **Traceability is complete and enforced.** 140/140 catalog rules are cited by production code and by at least one test; the generator fails if a rule loses either.

## Owner decisions (2026-10-25)

| Topic | Decision |
|---|---|
| CI provider | GitHub Actions |
| Azure IaC | Not included (Bicep was explained; the owner declined it) |
| Notifications | Local fake adapter only; no SMTP/Mailpit |
| Release severity | High/critical vulnerabilities block; moderate/low are recorded as risks |
| Cloud deployment | None |

Implementation decisions D33-1..14 are in [decision-log.md](../product/decision-log.md#phase-33-implementation-decisions-2026-10-25).

## Preconditions and reconciliation

- Phases 23–32 have accepted reports, all **Ready**. Their residual limits were rechecked: e-mail delivery stays out by owner decision; attachment byte upload and malware scanning stay out (FIL-006 no-op port, D30-13); Durable history purge, scale-out and Azure hosting stay out (no deployment).
- Starting state: `main` at `5299f2a` (#38). PRs #39–#41 had been merged into their stacked intermediate branches instead of `main`; PR #42 (merged by the owner) brought them into `main` with an empty diff against the reviewed stack. This branch was rebased onto that `main`.
- Concurrent resources: the owner's local Compose stack (demo data) was used read-only for diagnostics, the recovery dump and the real-stack journey (which only adds its own records), and was stopped — never removed — during the full gate to free memory, then started again.

## What changed

| Area | Change |
|---|---|
| Scheduling (D33-1, D33-2) | `ScheduledSweepService` in the Worker; `RequisitionExpiryHandlers` split from the user transitions so the sweep needs no Identity port; the Worker composes the enrichment ports for the RFQ comparison. Worker composition tests caught both missing registrations. |
| Observability (D33-3, D33-4) | Stable per-scope Worker correlation; consumers adopt the event's validated correlation ID (`IInboundCorrelation`); log scopes in dispatcher and consumers; meter `Template.Worker`; `docs/operations/observability.md`; `scripts/procurement-diagnostics.ps1`. |
| CI (D33-5, D33-6) | `.github/workflows/ci.yml` and `real-services.yml`; `OpenApiContractTests`; scripts portable to Linux. |
| Security (D33-7, D33-12, D33-13) | SPA CSP and security headers; non-root, digest-pinned webapp image; 1 MB request body limit; refresh retry on `auth.refresh-in-progress`. |
| Defects found by the real journey (D33-11, D33-13, D33-14) | Quotes failed with 400 when the browser clock ran ahead of the server (clock-skew tolerance); a reload during a refresh ended the session; a fast second supplier invite lost its selection. |
| Accessibility (D33-8) | Primary token darkened for 4.96:1 on hover; keyboard-only smoke test; dark-mode axe test. |
| Demo stack fixes | The Compose baseline turns the demo seed off and generates a policy-compliant demo password; `DemoIdentityData` gained tests (Identity coverage had fallen below its ratchet). |

No migration, no new package (NuGet or npm) and no public contract change: OpenAPI v1/v2 equal `docs/refactoring/baseline-phase-32`.

## Gate table

Full sequence `run-phase-validation.ps1 -ComposeBaseline` on 2026-10-25 10:18–10:48 (America/Sao_Paulo), then reruns of the three steps that failed after their fixes.

| Gate | Result | Evidence |
|---|---|---|
| 01 restore / 02 builds (WebApi, Worker, WorkflowFunctions) | Passed | 0 warnings, 0 errors |
| 03 architecture | Passed (rerun) | 77/77. First run 76/77: the reservation allow-list missed `RequisitionExpiryUseCases.cs` (also caught by hosted CI); list updated with D33-2 |
| 04 unit | Passed | 834/834 |
| 05 integration | Passed | 268/268 |
| 06 PostgreSQL 16.15 | Passed | 75/75 (includes `T_NFR_PERF_4`) |
| 06b RabbitMQ 4.3.6 | Passed | 30/30 |
| 06c Elasticsearch 9.5.5 | Passed | 13/13 |
| 06f Redis 8.10.2 | Passed | 23/23 |
| 06p Azurite 3.37.0 | Passed | 6/6 |
| 06q Worker host | Passed | 4/4 (sweeps resolve and run; correlation adoption) |
| 06r WorkflowFunctions | Passed | 26/26 |
| 06s Functions emulator | Passed | 7/7 (6 m 18 s) |
| 06e Compose baseline | Passed (rerun) | 181/181 from clean volumes (`p14-baseline-20261025112221`): WebApi/Worker/WorkflowFunctions, exact OpenAPI, approvals, purchasing and reports end to end, outages and restarts. First runs failed at startup and then on 43 checks because `.env.example` enables the demo seed (weak generated password, then a fiscal-period conflict); the baseline now turns the demo seed off |
| 07 functional | Passed | 386/386 (adds OpenAPI contract, body limit, demo personas) |
| 07b coverage ratchet | Passed (rerun) | Domain 99.16 line; Application 97.92/90.02; Identity 94.35/82.72 (first run 86.82/75.31 before the `DemoIdentityData` tests). No threshold lowered |
| 08 vulnerable packages | Passed | none in 27 projects |
| Deprecated packages | Passed (policy) | only the test-only xunit 2.9.3 family ("Legacy") |
| License inventory | Passed | 0 packages outside the policy |
| 09 Compose config (placeholders) | Passed | |
| 10 OpenAPI capture | Passed | v1/v2 identical to `baseline-phase-32` |
| 11 Graphify | Passed | 16,009 nodes, 44,124 edges, 491 communities |
| 12 `git diff --check` | Passed | |
| 13 secret scan | Passed | 8 settings, 8 Compose/env templates, compromised key absent from 11,473 files |
| Frontend validate | Passed | format, lint, types, Vitest 90/90, architecture + mutations, API drift, build |
| Frontend audits | Passed | licenses; `pnpm audit --audit-level high` |
| Playwright (mocked API) + axe | Passed | 21/21 including the dark-mode axe test and the keyboard-only smoke |
| Real-stack SC-01 journey | Passed | 1/1, axe and CSP clean |
| Hosted CI (GitHub Actions) | Passed | run 40199382062: backend, frontend, repository |
| Recovery rehearsal | Passed | 8/8 checks, run `20261025085208` |
| Rule traceability | Passed | 140/140 traced |
| Performance — SPA (PERF-8) | Passed | initial ≈ 121 kB gzip (budget 350), largest lazy chunk 23.4 kB (150) |
| Performance — concurrent reservation (PERF-4) | Passed | 50 parallel submissions ≤ 5 s, exactly 10 reservations, no over-reservation |
| Performance — PDF reports (PERF-6, PDF part) | Passed | PO PDF 3.15 s, requisition PDF 0.74 s (budget 15 s) |
| Performance — approval start (PERF-7) | Passed | 0.44–2.97 s (budget 10 s) |
| Performance — volume lists/details/commands (PERF-1/2/3), Outbox drain of 1,000 (PERF-5), XLSX 10,000 rows (PERF-6) | **Blocked** | the synthetic volume dataset and load harness do not exist |
| Hosted `real-services` workflow | Skipped | scheduled weekly/manual; not run in this phase (the same suites passed locally) |
| Older upgrade rehearsals (Redis 8, PostgreSQL 16 patch, RabbitMQ 4, Redis Insight, Elastic) | Skipped | out of Phase 33 scope; unchanged since their last green run |
| VSIX validation | Skipped | no VSIX-scoped change (decision 1) |

## E2E and business-rule journeys

| Journey (handoff) | Evidence |
|---|---|
| Full SC-01 through the browser on the real stack | `e2e-real/procurement-journey.spec.ts` (Chromium, Compose stack) |
| Standard competitive purchase | real journey; `sourcing.spec.ts`; functional SC-01 |
| Low-value / direct purchase or insufficient-quotation waiver | `T_SRC_017_*`, `T_SRC_006_*` (unit/functional), `sourcing.spec.ts` non-leading waiver |
| Rejected / cancelled purchase | `approvals.spec.ts` rejection; `T_APR_009_*`; requisition cancel in `requisitions.spec.ts` |
| Supplier suspension before commitment | `T_SRC_003_b`, `T_APR_012_*` (PostgreSQL invalidation in the supplier commit) |
| Approval self-action denial | `T_APR_005_a`, `T_APR_008_a`, `T_APR_007_a` |
| Concurrency / oversubscription denial | `T_BUD_006_a`, `T_NFR_PERF_4`, `SC_08_*` |
| Partial receipt, over-receipt denial, compensating correction | `purchasing.spec.ts`, `T_RCV_*`, Compose baseline purchasing checks |

## Security review (STRIDE, release)

| Threat | Control | Evidence |
|---|---|---|
| Spoofing | Identity + session families with rotating hashed refresh tokens, `sid` check, CSRF double-submit + `Origin`, rate-limited login/refresh | session functional/PostgreSQL suites; `SEC-005-b` test |
| Tampering | `If-Match` versions, idempotency keys, append-only audit/history/receipts, server-computed totals | architecture rules, PostgreSQL concurrency tests |
| Repudiation | Audit records with actor, rule IDs and one correlation ID across HTTP, Outbox, Worker and Functions | D33-3, traceability audit column |
| Information disclosure | No secret in committed files; logs carry IDs and codes only; CSP, `nosniff`, `no-referrer`, `DENY` framing; non-root images | secret scan, headers checked on the running webapp |
| Denial of service | 1 MB body limit, page size ≤ 100, rate limiting, bounded retries/DLQ, report concurrency limits | `RequestBodyLimitFunctionalTests`, messaging suites |
| Elevation of privilege | Permission policies on every action (architecture-tested), server-side `allowedActions`, permission matrix | `PermissionMatrixFunctionalTests` |

Residual: the WorkflowFunctions container runs as root on the Azure Functions base image (Phase 31, unchanged); attachments carry metadata only and no malware scanning (FIL-006).

## Observability, CI and recovery

- **Observability:** [docs/operations/observability.md](../operations/observability.md). Diagnostics verified on the live stack: Outbox, Inbox, reports, approvals, notifications, all eight procurement queues/DLQs and the three readiness endpoints.
- **CI:** `ci.yml` (fast gate, every PR) and `real-services.yml` (weekly/manual). Actions pinned by commit SHA (`checkout` v7.0.1, `setup-dotnet` v6.0.0, `setup-node` v7.1.0, `upload-artifact` v7.0.2, verified on GitHub on 2026-10-25); `contents: read` only. The first hosted run failed on the architecture allow-list, which was fixed; the second run passed.
- **Recovery (NFR-REC-1):** `scripts/recovery-rehearsal.ps1` — backup 3.7 s, restore 16.6 s, 63 + 8 tables with identical row counts, blob SHA-256 equal, restored Azurite running.

## Retained resources (nothing was deleted)

- Stopped containers `p33-pg-20261025085103`, `p33-blob-20261025085103`, `p33-pg-20261025085208`, `p33-blob-20261025085208` and volumes `p33-blob-20261025085103-data`, `p33-blob-20261025085208-data` (recovery rehearsals).
- Stopped Compose baseline projects `p14-baseline-20261025103332`, `p14-baseline-20261025105419` and `p14-baseline-20261025112221`, with their volumes and networks.
- Records created in the owner's local demo stack by the real-stack journey runs (requisitions, RFQs, orders and receipts named "Jornada de release …"). They can stay; removing them needs a decision.
- Evidence under `artifacts/validation/` (git-ignored), including the recovery dumps (synthetic demo data).

Cleanup of these resources requires explicit authorization.

## Remaining work

1. **Volume performance harness (blocks Ready).** Seed the NFR-PERF dataset into an isolated stack and measure PERF-1/2/3 p95, the PERF-5 drain of 1,000 Outbox events and the 10,000-row XLSX of PERF-6 against the Phase 23 budgets.
2. Scheduled RFQ reminders need an owner decision on when a reminder is due (D33-10).
3. Run the `real-services` workflow on GitHub once (manual dispatch) to have hosted evidence of the Testcontainers tier.

## Known limitations

- No e-mail delivery, IaC or Azure deployment (owner decisions).
- No volume performance evidence yet (above).
- Durable history purge and multi-instance Functions are not exercised (Phase 31).
- The legacy Product/Person/Order samples and API v1 remain, as the handoff requires without a separate sunset decision.
