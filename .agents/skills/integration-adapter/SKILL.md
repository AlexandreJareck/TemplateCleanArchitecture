---
name: integration-adapter
description: Implement or modify a Redis, RabbitMQ, Elasticsearch, identity, localization, or other external-system adapter behind an application-owned port. Do not activate for pure application logic or vendor-free domain work.
---

# Integration adapter

Inputs are the application port, vendor contract, configuration keys, availability policy, and test ownership. Read [references/adapter-checklist.md](references/adapter-checklist.md).

1. Keep vendor types and packages outside Domain/Application and implement the smallest port in Infrastructure.
2. Add composition-root registration, typed options validation, cancellation, bounded failure behavior, and safe structured logging.
3. Test available, unavailable, cancellation, serialization/contract, and lifecycle paths without accidental live-service access.
4. Report operational assumptions, health behavior, compatibility, and validation.

Stop before topology/data-format/auth changes or live external writes not explicitly authorized. Never log payload secrets, credentials, tokens, or `.env` values. Do not make optional observability a host-startup prerequisite.
