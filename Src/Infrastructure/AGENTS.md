# Infrastructure instructions

- Implement Application-owned ports; never reference Presentation.
- Keep EF/Npgsql, Identity, Redis, RabbitMQ, Elastic, and localization vendor types here and register them through project service-registration extensions composed by WebApi.
- Validate typed options, propagate cancellation, bound failures, dispose clients correctly, and never log credentials, tokens, or sensitive payloads.
- Do not rewrite applied migrations or hand-edit migration designer/snapshot files. Review generated SQL for PostgreSQL.
- Add deterministic adapter or persistence tests; live-service tests must use an explicit fixture/profile.
