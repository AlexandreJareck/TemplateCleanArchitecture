# ADR-0007: External data as optional, snapshot-based enrichment

- Status: Accepted (Phase 23, 2026-10-18); adapters in Phase 25, PTAX use in Phase 29
- Decision owners: roadmap integrations; D23-16, D23-17

## Context

BrasilAPI can suggest supplier (CNPJ) and postal-address data; Banco Central do Brasil PTAX provides exchange rates for quote normalization. Providers have rate limits, outages and schema changes, and their data is not a legal verification.

## Decision

1. Application owns narrow ports (`ISupplierRegistryLookup`, `IPostalCodeLookup`, `IExchangeRateProvider`) with provider-neutral DTOs and explicit result kinds (`Found`, `NotFound`, `Unavailable`, `Rejected`, `Invalid`).
2. Infrastructure adapters use typed `HttpClient`, 5 s timeouts, at most 2 retries for transient failures on idempotent GETs, circuit breaking, cancellation and sanitized logs (INT-001).
3. Enrichment is advisory: the user explicitly accepts fields; the accepted values are persisted with provider and retrieval time (INT-002). Provider failure never blocks manual entry.
4. Exchange rates are stored as `ExchangeRateSnapshot` on the comparison; a comparison is reproducible from stored provenance; a missing rate yields an `Incomplete` comparison or a manual rate with the `ManualExchangeRate` waiver (SRC-013, CALC-004). No live rate is used after capture.
5. Deterministic tests use local fake servers; live provider calls only in explicitly authorized smoke tests.
6. Browser code never calls external providers directly.

## Alternatives

- **Mandatory validation against providers:** blocks work during outages and implies a verification the providers do not offer. Rejected.
- **Live lookups at read time:** history would change. Rejected.

## Consequences

- Supplier and comparison records carry provenance fields.
- Provider terms, limits and endpoints are re-verified at Phase 25/29 execution (spike SP-5).
