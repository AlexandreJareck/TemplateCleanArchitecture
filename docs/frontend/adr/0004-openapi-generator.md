# ADR-0004: Generate Infrastructure TypeScript with pinned Orval

- Status: Accepted (D26A-9)
- Date: 2026-10-18

## Decision

Pin Orval 8.43.0 as a pnpm development dependency and generate Angular `HttpClient` code from the committed v2 OpenAPI baseline into `infrastructure/api/generated`. Handwritten adapters isolate generated DTOs and transport behavior. Do not select Orval's `httpResource` output for the generated Infrastructure layer.

## Rationale and consequences

Orval aligns with the frontend's Node/pnpm toolchain, is MIT licensed, documents first-class Angular 22-compatible `HttpClient` generation and avoids a Java runtime. Its rapid release cadence is contained by an exact pin, the lockfile and monthly review rather than floating updates.

NSwag.ConsoleCore 14.7.1 is the pre-approved fallback only if the Phase 26B spike proves an Orval contract incompatibility, nondeterministic generation, unacceptable dependency/license result or Angular compilation failure. Kiota is not Angular-native and its TypeScript target remains experimental; OpenAPI Generator adds a Java JAR or container runtime. Clean generation and byte drift checks are release gates; generated files are never hand-edited.
