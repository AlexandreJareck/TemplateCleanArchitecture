---
name: api-contract-change
description: Protect API routes, versions, bindings, status codes, authentication behavior, response shapes, and OpenAPI when controllers or public HTTP contracts change. Do not activate for internal refactors with no HTTP effect.
---

# API contract change

Inputs are affected endpoints, intended behavior, existing snapshots, and compatibility policy. Read [references/contract-checklist.md](references/contract-checklist.md).

1. Capture the relevant baseline before editing.
2. Enumerate observable deltas and classify each as compatible, deprecated, versioned, or breaking.
3. Add functional coverage for success, validation, auth, not-found/conflict, and cancellation as applicable.
4. Regenerate and diff OpenAPI, then report approved and unexpected differences.

Stop if a breaking route/status/schema change lacks explicit approval or the baseline cannot be reproduced. Do not remove v1, silently normalize status codes, or include credentials/tokens in fixtures or snapshots. Output a contract-delta report and test evidence.
