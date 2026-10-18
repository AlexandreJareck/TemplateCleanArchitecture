# Phase 25 Execution Handoff — Organization and Procurement Master Data

## Mission

Implement the stable master data required by procurement: departments, cost centers, suppliers, supplier contacts and addresses, catalog items, categories, and units of measure. Add optional external enrichment behind Application ports and deliver fully tested v2 APIs without coupling core layers to external providers.

## Required context and skills

Read root/nested instructions, the Phase 23 blueprint/ADRs, Phase 24 report, current Domain/Application/Persistence/WebApi patterns, API baseline, localization, Identity/permission policies, Compose, and relevant tests.

Use `api-contract-change`, `ef-migration`, `integration-adapter`, `architecture-boundary-check`, and `dotnet-change-verification` as triggered. Verify current official API terms, schemas, rate limits, licenses, packages, and image versions on the execution date.

## Preconditions

- Phases 23–24 are complete and green.
- Record HEAD/dirty tree and preserve concurrent work.
- Confirm approved names, identifiers, required fields, permissions, data retention, and single-organization decision.
- Capture OpenAPI/test/package/architecture baselines before editing.
- External providers must be reachable only from disposable tests or explicitly authorized calls. Use local fake servers for deterministic suites.

## Domain and schema

Implement the approved model, including at minimum:

- `Department`: stable code, name, active status;
- `CostCenter`: stable code, name, department ownership, active status;
- `Supplier`: legal/trade name, normalized tax identifier, status, contacts, addresses, categories, and external-enrichment snapshot metadata;
- `CatalogItem`: code/SKU, description, item/service kind, category, unit of measure, active status;
- controlled `Category` and `UnitOfMeasure` concepts according to the blueprint.

Use behavior methods/private setters, typed or validated identifiers/value objects where they add invariant value, UTC through the approved clock, and optimistic concurrency. Define uniqueness with PostgreSQL-correct indexes. Deactivation must preserve historical referential use; do not hard-delete referenced master data.

Use the approved supplier lifecycle, at minimum Draft, Active, Suspended, and Inactive (or the Phase 23 equivalent). Only Active suppliers are eligible for new invitations, awards, and purchase orders. Suspension/inactivation never mutates historical snapshots. Status transitions require the appropriate permission, reason when required by policy, concurrency protection, and an audit record. Treat tax-identifier validation as structural/normalization validation only; do not claim government verification or compliance certification from optional enrichment.

Create additive migrations and inspect generated SQL. Never edit designer/snapshot files manually.

## Application and API

- Add focused commands/queries/validators and aggregate-specific repositories or ports; do not expand a generic repository merely for convenience.
- Add v2 CRUD/search endpoints with Phase 23 pagination/filter/sort, Problem Details, permission, cancellation, idempotency, and concurrency conventions.
- Avoid returning persistence entities directly.
- Support practical filters: active/status, code/name, supplier tax identifier/category, catalog item type/category.
- Record audit history according to the blueprint.
- Preserve every existing sample route and OpenAPI contract.

## External integrations

Implement separate adapters for approved optional enrichment:

- BrasilAPI supplier/CNPJ and postal-address lookup;
- Banco Central do Brasil currency/PTAX lookup contract needed by later quotation comparison.

Requirements:

- Application owns narrow ports and provider-neutral DTOs.
- Use typed `HttpClient`, bounded timeout, cancellation, safe retry only for transient/idempotent calls, circuit breaking when supported, and structured sanitized logs.
- Validate and normalize provider data before presenting it; users explicitly accept or override enrichment.
- Persist the accepted snapshot and provenance timestamp, not a live provider object.
- Provider outage, rejection, schema mismatch, and rate limiting have explicit outcomes and never block manual supplier entry.
- Cache only according to approved freshness and privacy rules.

## Out of scope

- budgets, requisitions, RFQs, quotes, approvals, reports, Worker, Durable Functions, supplier portal, bulk import, multi-tenancy, or Elasticsearch projection;
- replacing existing Product/Person endpoints;
- using external data as authoritative truth.

## Tests and validation

Add a rule-to-test matrix for applicable organization, master-data, and `SUP-*` rules. Add mock-free domain tests, handler tests, PostgreSQL uniqueness/status-transition/concurrency/filter tests, fake-server adapter tests for success/timeouts/429/5xx/malformed/unavailable/cancellation, functional endpoint/permission/contract tests, architecture mutation tests, migration idempotency, localization tests, and OpenAPI review.

Run sequential build/test gates, vulnerability/license audits for new packages, secret scan, Compose config, PostgreSQL suite, functional suite, coverage, Graphify after structural changes, OpenAPI comparison, and `git diff --check`. No live provider is required for deterministic acceptance; an optional sanitized smoke call may be documented separately with authorization.

## Acceptance criteria

- Master data invariants, uniqueness, supplier eligibility/status transitions, deactivation, audit, concurrency, permissions, and API contracts are enforced and traced to Phase 23 rule IDs.
- External enrichment is replaceable, fail-soft, bounded, and thoroughly fake-tested.
- No provider or ASP.NET/EF type enters Domain/Application.
- Migrations apply twice and roll back according to the documented strategy.
- Existing v1/v2 contracts are preserved.
- Required gates pass and the full result is recorded.

## Report and final response

Create `docs/refactoring/phase-25-report.md` covering schema, contracts, endpoints, adapters, provider research/date, tests, migration evidence, OpenAPI delta, rollback, residual risks, and gates. Lead the final response with readiness and delivered capabilities.

Never commit/push/merge, change branches/worktrees, open a PR, call production providers, or delete data/resources without explicit authorization.
