# Phase 33 Execution Handoff — Operational Readiness and Product Release Gate

## Mission

Finish the Procurement Hub as a demonstrable, operable reference application and issue the release decision. Add only the missing operational capabilities needed by the approved product: recurring reminders/expirations, local email, cross-process observability, CI, container/development workflows, optional Azure IaC without deployment, complete E2E/performance/accessibility/security evidence, demo data, recovery/rollback procedures, and reconciled documentation.

This is not permission for unrelated feature expansion or dependency churn.

## Required context and skills

Read every Phase 23–32 report and accepted ADR, Phase 21–22 release/security reports, current code/projects/configuration/Compose/scripts/tests/coverage/package/license evidence, API baselines, frontend docs, Docker resources, and retained rollback inventory. Read all scoped instructions.

Use `package-upgrade` for any required dependency/tool update, `api-contract-change` for observable endpoints, `integration-adapter` for notification/schedule adapters, `architecture-boundary-check`, and `dotnet-change-verification`. Re-check all external support/version/license facts from official sources on the execution date.

## Preconditions and hard stops

- Phases 23–32 must be complete with accepted reports; reconcile implementation, not handoff existence.
- Record HEAD/dirty tree and every concurrent change.
- Inventory current Docker/Node/.NET/Azure emulator resources without deleting them.
- Obtain explicit owner decisions for required CI provider, Azure IaC language, notification adapter scope, release severity policy, and whether any optional cloud deployment/smoke is authorized. Default: GitHub Actions, Bicep, Mailpit/local fake only, no Azure deployment.
- If a required prior gate is red, fix it only when the correction belongs to Phase 33 operational scope; otherwise report the prerequisite blocker.

## Operational capabilities

1. Implement recurring quote expiry, approval reminders/escalation triggers, stale report/file cleanup selection, and reservation expiry using the approved Phase 23/31 scheduler boundary. Prefer Functions timer triggers when Functions is the approved host; do not add Hangfire/Quartz without a new decision.
2. Add an application-owned notification port and local SMTP/Mailpit adapter if not already completed. Templates are versioned/localized, delivery is queued/idempotent, and logs contain no message body or recipient secrets.
3. Complete correlation, metrics, traces and structured logs across HTTP, PostgreSQL, Outbox, RabbitMQ, Worker, report jobs, storage, Functions and activities using the adopted observability strategy. Do not duplicate or expose sensitive payloads.
4. Add dashboards/diagnostic queries or documented views for outbox backlog, inbox failures, dead letters, report failures, orchestration status, notification failure and dependency health.
5. Separate liveness/readiness/startup health for WebApi, Worker and Functions with correct required/optional dependency classification.

## CI and reproducibility

Create CI appropriate to the approved provider with least privilege and pinned actions/tools where supported:

- secret/config scan;
- restore/build/analyzers;
- architecture/unit/integration/functional tests;
- PostgreSQL/RabbitMQ/Redis/Elasticsearch/Azurite/Functions integration tiers as feasible;
- frontend install lock verification, lint, type-check, framework-free Domain/Application tests, Infrastructure adapter contracts, Angular component tests, architecture-boundary mutations, build and Playwright E2E;
- coverage policy;
- OpenAPI and generated-client drift;
- package vulnerability/deprecation/license audits;
- container/Compose validation and image provenance;
- artifact retention with secret-safe logs.

Split fast PR gates from expensive scheduled/manual real-service rehearsals. No workflow receives write/cloud/deployment credentials unless separately approved. Prove CI configuration locally where possible and never claim hosted CI success without an actual run.

## Containers, local developer experience, and IaC

- Make the normal documented local stack work from placeholder configuration with WebApi, Worker, Functions, PostgreSQL, RabbitMQ, Redis, Elasticsearch/Kibana as retained, Azurite, Mailpit and frontend.
- Preserve isolated baseline/rehearsal profiles and user data boundaries.
- Add reproducible container builds, non-root/runtime hardening, health, graceful shutdown and bounded resources for new executables.
- Add optional Bicep (or approved alternative) describing required Azure resources, managed identity/RBAC, networking, storage, Function hosting, WebApi/Worker hosting, observability and secret references. Validate syntax/plan only. Do not deploy or create resources without explicit authorization.
- Document local, container and Azure configuration key names only.

## Product quality gates

- Seed a clearly synthetic demonstration organization, roles, users, master data, budget and scenario through a safe idempotent development/test mechanism. Never ship usable default credentials in committed configuration.
- Execute full browser E2E: authenticate -> create requisition -> reserve budget -> RFQ -> quotes/comparison -> timed/multi-level approval -> purchase order/document -> partial/final receipt -> reports/audit.
- Execute business-rule journeys for standard competitive purchase, approved low-value/direct-purchase or insufficient-quotation waiver, rejected/cancelled purchase, supplier suspension before commitment, approval self-action denial, concurrency/oversubscription denial, partial receipt, over-receipt denial, and compensating receipt correction.
- Add accessibility automated checks plus documented keyboard/manual smoke for critical flows.
- Add bounded performance/load smoke for list queries, concurrent reservation, outbox dispatch, worker consumption, report generation and workflow start/events. Define thresholds from Phase 23 rather than inventing success after measurement.
- Execute backup/restore and rollback rehearsal for new PostgreSQL schema and blob metadata/content using synthetic data.
- Re-run security threat review, secret scan, auth/session abuse cases, authorization matrix, upload/download protections, dependency/image audits and sanitized logging checks.

## Cleanup and compatibility

Remove only code proven obsolete by Phases 23–32 and covered by tests. Legacy Product/Person/Order examples and API v1 remain unless the owner explicitly approves a separate sunset and contract migration; ambiguity means keep them. Do not combine an API-v1 removal with this release gate.

Reconcile `AGENTS.md`, developer setup, product docs, API docs, frontend docs, configuration/secrets docs, operations/runbooks, rollback, diagrams, validation scripts, and known limitations. Documentation may record only commands/results actually verified.

## Required final matrix

Run the complete repository gate sequentially against the final workspace, including all backend suites, every real-service suite, Compose baseline/rollbacks/rehearsals still required, Worker, Azurite/report, Functions emulator, frontend unit/E2E/accessibility, coverage, OpenAPI/generated client, Graphify, package/image vulnerability/deprecation/license audits, secret scan, Docker/container checks, backup/restore/rollback, and `git diff --check`.

Mark every gate `Passed`, `Failed`, `Skipped`, or `Blocked`. A required skipped/blocked/failed gate means `Not ready`. Do not infer success from earlier phases.

## Out of scope

- New product modules, multi-tenancy, supplier portal, payments/tax/NFe, microservices, Azure Service Bus migration, public deployment, production data migration, API v1 removal, or unapproved major upgrades.

## Acceptance criteria

- The complete product journey works through the UI and survives documented restarts/failures.
- Required CI configuration exists and any claimed hosted results are real.
- Local setup is reproducible and Azure IaC validates without unauthorized deployment.
- Security, accessibility, performance, observability, recovery, rollback and operations meet the recorded requirements.
- Docs and code agree; no secret or material user data is exposed/deleted.
- The final business-rule traceability matrix maps every required rule ID to implementation, API outcome, audit evidence, and passing tests; no policy remains only in UI code, orchestration code, or documentation.
- Release is `Ready` only when all required gates pass.

## Required report and final response

Create `docs/refactoring/phase-33-report.md` with executive release decision, reconciliation matrix, architecture/package/image inventory, full gate table, E2E evidence, security/accessibility/performance/observability/CI/IaC/recovery results, commands/counts/artifacts, failed/skipped/blocked gates, retained resources, rollback and known limitations.

Lead the final response with `Ready` or `Not ready`, followed by the complete journey, architecture/operations, gates, risks, retained resources, cleanup requiring approval, and report path.

Do not commit/push/merge, change branches/worktrees, open a PR, deploy/provision cloud resources, modify production-like environments, or delete data/volumes/queues/blobs/orchestration history without explicit authorization.
