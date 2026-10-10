# API v1 lifecycle

Status em 2026-10-10: **deprecated, suportada e sem data de remoção**.

- As rotas, métodos, parâmetros, status codes e envelopes de sucesso/erro da v1 permanecem compatíveis com o baseline original em `docs/refactoring/baseline/`.
- O OpenAPI v1 informa a depreciação no campo `info.description`. A v2 não é marcada como deprecated.
- Correções REST e a adoção de Problem Details para resultados da Application são feitas na v2. A v1 mantém inclusive respostas de falha de negócio dentro do envelope legado com HTTP 200 quando esse era o comportamento publicado.
- Não há data de sunset. Uma remoção futura exige decisão explícita, comunicação aos consumidores, período de migração e um novo baseline de contrato.
- O baseline aprovado após a migração de versionamento está em `docs/refactoring/baseline-fase-6/`; o baseline original não foi sobrescrito.

Rollback da Fase 6: restaurar em conjunto o pacote de versionamento anterior, a configuração do API Explorer e os controllers, e então confirmar que `docs/refactoring/baseline/` volta a ser reproduzido.
