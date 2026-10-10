# Test instructions

- Keep domain tests mock-free, handler tests isolated behind ports, persistence tests explicit about provider semantics, and functional tests self-contained.
- A functional-test fixture must replace or own every external dependency; real services are opt-in only.
- Prove cancellation propagation, expected failure categories, and negative/unavailable-service paths where applicable.
- Run .NET test projects sequentially. Preserve deterministic seed/state and do not include secrets in test data or output.
- Architecture tests must exercise each forbidden rule with a controlled mutation or equivalent synthetic fixture.
