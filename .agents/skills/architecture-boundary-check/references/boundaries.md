# Repository boundaries

- Domain: BCL only; no Application, Infrastructure, Presentation, transports, databases, caches, brokers, logging, search, or transport DTOs.
- Application: Domain plus abstractions; no EF implementations, ASP.NET transport, Redis, RabbitMQ, Elasticsearch, or Serilog implementations.
- Infrastructure: implements Application ports and must not depend on Presentation.
- Presentation: HTTP and composition; no direct DbContext or repository use.
- WebApi is the composition root and may reference infrastructure registration extensions.

Temporary exceptions must name the symbol/package and the phase that removes it.
