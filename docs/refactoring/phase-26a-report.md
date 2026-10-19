# Phase 26A report — Frontend repository and technology decisions

Specification: `docs/refactoring/handoff-phase-26a.md`, executed through `docs/refactoring/execute-phase-26.md` on 2026-10-18.

## Release decision

**Ready.** The evidence review is complete and the owner explicitly approved D26A-1 through D26A-14 on 2026-10-18. D26A-6 keeps spartan/ui + Angular CDK with Angular Material as the evidence-gated fallback. D26A-9 was revised and approved with Orval 8.43.0 as primary and NSwag.ConsoleCore 14.7.1 as the evidence-gated fallback. Phase 26B may begin. No package was installed, no generator was run and no frontend runtime code was created during 26A.

## Starting state

- Authoritative checkout: `C:\Users\alexa\source\repos\TemplateCleanArchNET`.
- Start HEAD: `ad06cad64d993cc73cfa490a7bff8262e3f2f356`.
- Start working tree: clean.
- Phase 23 report: complete; Phase 24 report: Ready; Phase 25 report: Ready.
- Current client contract: committed OpenAPI 3.0.4 v2 baseline with 61 schemas and 65 HTTP operations, including session, account, audit, organization, supplier, catalog, enrichment and legacy sample operations.

## Accepted decisions

The decision record is [technology-decisions.md](../frontend/technology-decisions.md). In short: existing monorepo, one Angular CLI app, pnpm, Angular 22/Node 24 LTS/TypeScript 6/RxJS 7, Tailwind 4, minimal spartan/ui + CDK with Material fallback, Signals/RxJS, stable Signal Forms, Orval Angular `HttpClient` generation with NSwag fallback, Transloco, Vitest/Playwright/axe/architecture mutations, no Storybook, local-only delivery boundary and exact-pin supported-major maintenance.

The repository-topology alternative and its independent CI/release/OpenAPI/rollback consequences are recorded. The UI matrix explicitly covers spartan/ui, Angular Material/CDK and PrimeNG. Official shadcn/ui was verified not to document Angular installation and is not treated as an Angular dependency.

## Deliverables

- `docs/frontend/technology-decisions.md`
- `docs/frontend/frontend-architecture.md`
- `docs/frontend/design-system-strategy.md`
- `docs/frontend/toolchain-and-maintenance.md`
- `docs/frontend/adr/0001-repository-topology.md`
- `docs/frontend/adr/0002-ui-component-stack.md`
- `docs/frontend/adr/0003-state-and-forms.md`
- `docs/frontend/adr/0004-openapi-generator.md`
- `docs/frontend/adr/0005-runtime-localization.md`
- `docs/frontend/adr/0006-testing-strategy.md`
- `docs/frontend/adr/0007-storybook.md`
- this report

## Review passes

### Evidence and currency

Passed. Primary official sources were consulted on 2026-10-18. Angular 22 compatibility/support, Node 24 LTS, stable Signal Forms, CLI Vitest, Tailwind 4 Angular/browser requirements, spartan Angular 22/Tailwind 4/support/update model/MIT source, Angular Material/CDK, PrimeNG 22 licensing and migration, shadcn framework templates, Orval, NSwag, ng-openapi-gen, Kiota, OpenAPI Generator, Transloco, Playwright/axe and Storybook were checked. Exact registry metadata will be queried again before lockfile creation.

### Architecture and scope

Passed. The proposed stack preserves `Domain <- Application <- Infrastructure / Presentation <- Bootstrap`, generated-type containment, in-memory session credentials, backend authority, same-origin CSRF contract, pt-BR default, WCAG 2.2 AA target and Phase 33 delivery boundary. No NgRx, Nx, SSR, micro-frontends, broad component suite or deployment was introduced.

### Executability

Passed. The local path, runtime/tool families, package manager, UI fallback, generator, state/forms, localization, test strategy, delivery boundary and update rules are explicit and approved. Phase 26B still must recheck exact package peers/licenses, create the lockfile, run the spartan and Orval evidence spikes and prove every bootstrap gate.

## Gates

| Gate | Result | Evidence |
|---|---|---|
| Preconditions and working-tree inventory | Passed | Phase 23 complete; Phase 24/25 Ready; clean start at recorded HEAD |
| Current v2 OpenAPI review | Passed | OpenAPI 3.0.4, 61 schemas, 65 operations parsed from `baseline-phase-25` |
| Current primary-source research | Passed | Sources and access date recorded in `technology-decisions.md` |
| UI library comparison | Passed | Twelve-criterion matrix and license/upgrade consequences recorded |
| Repository topology comparison | Passed | Monorepo selected; separate-repository operating model and migration path recorded |
| Architecture review | Passed | Layer, state, port/adapter, session and test boundaries documented |
| Executability review | Passed | No unresolved technical placeholder; exact patches rechecked at bootstrap |
| Owner approval D26A-1..14 | Passed | Explicit approval received on 2026-10-18; D26A-6 and revised D26A-9 confirmed separately |
| Markdown/local-link check | Passed | 12 Markdown files checked; 0 missing local targets |
| `git diff --check` | Passed | Exit 0 |

## Risks and rollback

- Spartan copied styles require owned maintenance; Phase 26B may use the recorded Angular Material fallback only if its evidence gate fails and the report explains the trigger.
- Transloco and spartan are third-party Angular dependencies and receive explicit peer, license and audit checks.
- Orval has a rapid release cadence; the exact pin, frozen lockfile, generated-output drift check and monthly review contain it. NSwag may replace it only under the accepted evidence-gate conditions.
- Exact patches can change between approval and bootstrap; a supported patch update is recorded, while any major/family substitution returns to owner decision.
- Documentation-only rollback is removal/reversion of the files listed above. No runtime, package, schema, remote or infrastructure state exists to roll back.

## Owner action

Complete. The owner approved D26A-1 through D26A-14 on 2026-10-18. Phase 26B is authorized to implement the accepted baseline and must stop if either evidence-gated fallback condition is reached without a documented comparison.
