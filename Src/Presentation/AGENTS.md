# Presentation instructions

- WebApi is the composition root and HTTP adapter. Controllers do not access DbContexts or repositories directly and contain no business rules.
- Preserve v1 routes/status codes. Treat v2 as the closer baseline without silently changing public behavior.
- Pass request cancellation, map application results at the boundary, and keep OpenAPI plus functional tests synchronized with intentional contract changes.
- Test startup must not require optional Elastic, Redis, or RabbitMQ services.
- Never place or log secrets in appsettings, launch profiles, responses, or snapshots.
