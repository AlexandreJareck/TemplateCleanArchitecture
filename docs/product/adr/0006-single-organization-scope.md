# ADR-0006: Single organization per installation

- Status: Accepted (Phase 23, 2026-10-18)
- Decision owners: D23-1

## Context

Multi-tenancy affects every table, query, cache key, event, report and authorization check, and needs isolation tests. The release definition serves one organization's purchasing process.

## Decision

1. One installation serves exactly one organization. `OrganizationSettings` is a singleton (base currency BRL, time zone `America/Sao_Paulo`, display name).
2. No `TenantId` column, tenant resolution middleware, tenant-scoped cache keys or tenant claims.
3. Base currency and time zone are locked after the first budget allocation (ORG-004).
4. Departments and cost centers express internal structure; they are not tenants and do not isolate data beyond the permission scopes in [permissions.md](../permissions.md).

## Alternatives

- **Shared schema with tenant column:** most common SaaS approach; large cross-cutting cost. Deferred.
- **Database per tenant:** strong isolation; operational cost. Deferred.

## Consequences

- Simpler queries, indexes and tests.
- Moving to multi-tenancy later requires a new ADR, a data migration and a re-review of every rule ID, event payload and report.
