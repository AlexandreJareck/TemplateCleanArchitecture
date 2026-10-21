# Template.WebApp

Client-rendered Angular frontend for TemplateCleanArchNET. It is a standalone, routed, strict, zoneless Angular CLI application; it has no SSR, Nx, Turborepo, micro-frontends, or NgRx.

## Toolchain

Use Node 24.22.0 and pnpm 12.14.0. Both versions are pinned in `package.json`, `.node-version`, and `.nvmrc`. Install exactly what is locked:

```shell
pnpm install --frozen-lockfile
```

On Windows PowerShell and other shells, the commands below are the same:

```shell
pnpm start
pnpm validate
pnpm audit:licenses
pnpm audit:security
pnpm e2e
```

`pnpm start` serves HTTPS locally and proxies `/api` to the ASP.NET Core API. Browser E2E uses a deterministic HTTP development server created by Playwright only for the test.

## Architecture

The enforced dependency direction is:

```text
Domain <- Application <- Infrastructure / Presentation <- Bootstrap
```

- `domain`: framework-free value behavior.
- `application`: models, errors, ports, and use cases; plain TypeScript only.
- `infrastructure`: generated HTTP transport, adapters, localization loader, and runtime configuration.
- `presentation`: Angular components, routes, Signal Forms, and local Signals.
- `bootstrap`: the only composition root.

`dependency-cruiser.config.cjs` enforces these boundaries and `pnpm architecture:mutations` proves each forbidden direction fails.

## API generation

The v2 client is generated from `docs/refactoring/baseline-phase-27/openapi-v2.json` with Orval 8.43.0 and Angular `HttpClient` output:

```shell
pnpm api:generate
pnpm api:drift
```

Never hand-edit `src/app/infrastructure/api/generated`. Handwritten adapters isolate generated DTOs and RFC Problem Details from Application and Presentation.

## Runtime configuration

`public/config/runtime-config.json` is fetched with `no-store` before Angular bootstraps. It may contain only a public API base URL, non-secret build label, and public feature flags. Never place credentials, tokens, storage keys, or internal service URLs in browser configuration. Production hosting and configuration injection are Phase 33 work.

## UI foundation

Tailwind CSS semantic variables and selected spartan/ui + Angular CDK primitives live in Presentation. The root route is an internal development gallery, not a product screen. Provenance and update instructions are in `src/app/presentation/shared-ui/SPARTAN_PROVENANCE.md`. The generator CLI is intentionally ephemeral and is not part of the installed dependency graph.
