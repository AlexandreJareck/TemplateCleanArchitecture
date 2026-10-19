# ADR-0007: Defer Storybook

- Status: Accepted (D26A-12)
- Date: 2026-10-18

## Decision

Do not install Storybook in Phase 26. Provide a development-only primitive gallery in the Angular application and keep it out of production navigation.

## Rationale and consequences

Storybook 10 supports current Angular, but one application with no separately released component library does not yet justify a second builder, dependency tree and test surface. Reconsider when primitives have multiple consumers or an independently reviewed design-system workflow.
