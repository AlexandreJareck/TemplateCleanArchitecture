# Frontend toolchain and maintenance

Status: accepted by the owner on 2026-10-18. Exact registry metadata is rechecked immediately before Phase 26B creates the lockfile.

## Approved version set

| Tool/family | Version | Support/license note |
|---|---:|---|
| Node.js | 24.22.0 LTS | Angular-compatible LTS runtime; Node license |
| pnpm | 12.14.0 | MIT; exact `packageManager` pin |
| Angular framework/CLI/CDK | 22.2.3 | Active-support major; MIT |
| TypeScript | 6.0.3 | Within Angular `>=6.0 <6.1`; Apache-2.0 |
| RxJS | 7.8.2 | Angular-compatible; Apache-2.0 |
| Tailwind CSS/PostCSS adapter | 4.3.3 | MIT; modern-browser baseline |
| spartan/ui CLI and brain | 1.8.0 aligned line | MIT; verify exact peers before generation; copied `helm` provenance retained |
| Transloco | 8.4.0 | Latest stable; MIT; v9 alpha excluded |
| Orval | 8.43.0 | MIT; exact pnpm development dependency; Angular `HttpClient` output |
| NSwag.ConsoleCore | 14.7.1 fallback only | MIT; local .NET tool; use only when the recorded Phase 26B Orval evidence gate fails |
| Unit runner | Angular CLI-provided Vitest line | Exact transitive/direct versions captured by generated manifest/lockfile |
| Playwright / axe | Current stable compatible with Node 24 at bootstrap | Apache-2.0 / MPL-2.0; exact pins after recheck |
| Storybook | Not installed | Reconsider only with multiple consumers or a design-system release workflow |

The Phase 26B report records every direct and transitive package, resolved version, license, purpose and material install/build script. A peer conflict, non-permissive/unknown license, high/critical advisory or unsupported runtime stops bootstrap for decision revision.

## Commands to establish in Phase 26B

The workspace will expose deterministic scripts for frozen install, development HTTPS/proxy, format write/check, lint, type-check, framework-free tests, adapter tests, component tests, architecture checks and negative mutations, OpenAPI generate/drift, production build and budgets, dependency/license/vulnerability audit, secret scan and Playwright bootstrap.

Commands use repository scripts where Windows and POSIX behavior differs. No script depends on a global Angular CLI, pnpm, Orval, NSwag or Playwright installation.

## Browser policy

Application support follows Angular 22's Baseline browser set. Required Phase 26 automated journeys run in Chromium; Firefox and WebKit smoke coverage is added before the product release gate. The Tailwind 4 minimum (Safari 16.4, Chrome 111, Firefox 128) is inside the modern-browser boundary. The application remains usable from 360 px through desktop widths.

## Updates

- Review patches/minors monthly and before each release phase.
- Keep Angular framework, CLI, build and CDK on one patch line.
- Keep spartan brain and CLI aligned; health-check and manually diff copied helm sources.
- Regenerate the OpenAPI client only with the pinned Orval dependency and classify output. If the documented fallback is triggered, pin NSwag in the local .NET tool manifest and record why.
- Group coherent tool-family updates; do not mix a major framework change with product features.
- Major upgrades require current official compatibility/migration research, a rollback point, frozen restore, all architecture/tests/build/E2E/audits and explicit owner authorization.
- Dependency bots are deferred until Phase 33 introduces CI; grouped Renovate or Dependabot updates can then be proposed.

Rollback is source-level: restore the previous manifest, lockfile, generator config and copied-component provenance together, run the frozen install and full frontend gate. No deployment or remote package publication is part of Phase 26.
