# ADR-0005: Runtime localization with Transloco

- Status: Accepted (D26A-10)
- Date: 2026-10-18

## Decision

Use Transloco 8.4.0 with pt-BR as default and checked-in English resources with identical keys. Use platform `Intl` for locale formatting and do not persist language choice in Phase 26.

## Rationale and consequences

Angular localization is first-party and strong for compile-time localized bundles, but runtime switching is more naturally expressed by Transloco. Only the stable core package is accepted; the 9.0 alpha and optional plugins are excluded.
