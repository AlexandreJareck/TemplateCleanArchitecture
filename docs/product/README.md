# Procurement Hub product blueprint

Status: approved Phase 23 blueprint (2026-10-18). Owner decisions D23-1 through D23-18 are recorded in [decision-log.md](decision-log.md). This folder is the authoritative product and architecture design input for Phases 24–33 in [phases-23-33-product-roadmap.md](../refactoring/phases-23-33-product-roadmap.md). It describes target behavior; none of it is implemented yet unless a later phase report says so.

| Document | Purpose |
|---|---|
| [product-vision.md](product-vision.md) | Users, problems, goals, non-goals, release boundary, success scenarios |
| [ubiquitous-language.md](ubiquitous-language.md) | Glossary used by every other document, the code, the API and the UI |
| [domain-model.md](domain-model.md) | Modules, aggregates, entities, value objects, invariants, lifecycles, cross-module references |
| [business-rules.md](business-rules.md) | Stable rule IDs, classification, policy values and versions, waivers, failures, planned tests, scenario traceability |
| [workflows.md](workflows.md) | State-transition tables and Mermaid diagrams |
| [permissions.md](permissions.md) | Roles, permissions, actor matrix, separation-of-duty constraints |
| [api-guidelines.md](api-guidelines.md) | v2 conventions for new procurement endpoints |
| [frontend-information-architecture.md](frontend-information-architecture.md) | Routes, screens, states, accessibility, frontend Clean Architecture |
| [non-functional-requirements.md](non-functional-requirements.md) | Security, audit, availability, performance, retention, recovery, observability, localization, privacy |
| [event-catalog.md](event-catalog.md) | Domain and integration events, payload ownership, idempotency keys |
| [decision-log.md](decision-log.md) | D23 decisions, alternatives, rationale, consequences, deferrals and spikes |
| [adr/](adr/) | Architecture Decision Records |

## How later phases use this blueprint

1. Each phase implements only the rule IDs assigned to it in [business-rules.md](business-rules.md) (column "Phase").
2. Each phase report contains a rule-to-test matrix that references these rule IDs and the planned test IDs (`T-<RULE>-<letter>`), adding the real test names.
3. A phase that needs to change a rule, a policy value, a permission, a state or an event updates this blueprint in the same change, records the change in [decision-log.md](decision-log.md) with owner approval, and never rewrites a historical report.
4. Policy values listed here are seeded defaults for policy version 1. They are configuration data, not constants in code.
