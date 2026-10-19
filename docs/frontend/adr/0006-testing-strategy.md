# ADR-0006: Layered Vitest, architecture and Playwright tests

- Status: Accepted (D26A-11)
- Date: 2026-10-18

## Decision

Use Angular CLI's Vitest setup for unit/component tests, direct framework-free Vitest suites for Domain/Application, dependency-cruiser plus negative mutations for boundaries, adapter contract fixtures, Playwright browser journeys and `@axe-core/playwright` scans.

## Rationale and consequences

This follows the Angular CLI default while separating framework-free logic from Angular TestBed. Browser automation proves session, focus and integration behavior that DOM emulation cannot. Automated accessibility checks are supplemented by a manual checklist.
