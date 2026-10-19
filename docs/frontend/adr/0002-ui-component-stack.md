# ADR-0002: Tailwind and a minimal spartan/CDK component layer

- Status: Accepted (D26A-5, D26A-6)
- Date: 2026-10-18

## Decision

Use Tailwind 4 semantic tokens, spartan/ui 1.8 aligned packages and Angular CDK 22.2.3. Copy/install only Phase 26 primitives and treat copied `helm` sources as owned code with provenance and manual update diffs. Angular Material/CDK is the pre-approved fallback if the bootstrap evidence gate fails.

## Rationale and consequences

This gives an Angular-native shadcn-style ownership model without importing React code. The tradeoff is a smaller maintainer base and manual responsibility for copied fixes; accessibility and upgrade tests are mandatory. PrimeNG 22's licensing/lock-in and Angular Material's visual constraints make them weaker defaults for this application.
