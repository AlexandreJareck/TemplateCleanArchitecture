# Core instructions

- Domain contains entities, value objects, invariants, and domain errors only; it has no framework, transport, persistence, cache, broker, logging, search, Application, Infrastructure, or Presentation dependency.
- Application owns use cases, contracts, validators, results, and ports. Vendor clients and ASP.NET/EF implementations stay outside it.
- Preserve private setters and EF-compatible construction while keeping invariants in entities.
- Use `decimal` for money, nullable annotations, injected time for observable behavior, and propagate `CancellationToken` through async ports.
- Add domain tests without mocks and handler tests against ports for each behavior change.
