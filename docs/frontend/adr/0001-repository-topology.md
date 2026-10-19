# ADR-0001: Keep the Angular application in the product monorepo

- Status: Accepted (D26A-1)
- Date: 2026-10-18

## Decision

Create `Client/Template.WebApp` in the existing repository. Do not create a nested Git repository or remote.

## Rationale and consequences

One product and team-sized academic workflow benefit from atomic backend/OpenAPI/client/UI changes and one release gate. A separate repository would require independent CI, versioning, contract publication, access and coordinated rollback that are not currently justified. Extraction with history remains possible after a new owner decision and named destination.
