# ADR-0003: Scoped state with Signals/RxJS and stable Signal Forms

- Status: Accepted (D26A-7, D26A-8)
- Date: 2026-10-18

## Decision

Use Angular signals in Presentation, framework-free use-case stores in Application, RxJS at Angular HTTP/asynchronous boundaries, and Angular 22 stable Signal Forms for new forms. Do not add NgRx.

## Rationale and consequences

The application does not yet have coordination complexity that merits a global event store. Form models are plain Presentation data mapped explicitly to Application inputs. Server authorization, invariants, transitions and calculations remain authoritative.
