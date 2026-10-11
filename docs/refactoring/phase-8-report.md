# Relatório da Fase 8 — 2026-10-10

Checkpoint fechado em 2026-10-10 às 16:22 (America/Sao_Paulo). A Fase 8 (cache Redis atrás de um adapter em Infrastructure) foi implementada sobre o estado não commitado das Fases 0–7, restaurado do `stash@{0}` (aplicado com `apply`; o stash foi mantido como backup). Nada das Fases 9+ foi implementado.

## O que mudou

### Application

- Novo port `Interfaces/Caching/IProductCache` (`GetAsync`, `SetAsync`, `RemoveAsync`). Contrato: qualquer falha de cache (backend indisponível, timeout, payload ilegível) é tratada como miss; só o cancelamento solicitado pelo chamador propaga.
- `ProductService` usa o port: hit devolve o DTO em cache; miss consulta `GetProductByIdQuery` e só grava em cache quando o resultado é sucesso. Antes, o resultado inteiro (inclusive o de NotFound) era serializado em cache e depois desserializado como `ProductDto`; isso foi corrigido.
- `UpdateProductCommandHandler` e `DeleteProductCommandHandler` chamam `RemoveAsync` depois de `SaveChangesAsync`, e só quando o produto existe.
- Removidos `Microsoft.Extensions.Caching.StackExchangeRedis` e `Newtonsoft.Json` de `Template.Application.csproj`. A versão do `Newtonsoft.Json` saiu do `Directory.Packages.props`, porque nenhum outro projeto o usa.

### Infrastructure — `Template.Infrastructure.Caching` (novo)

- `DistributedProductCache` sobre `IDistributedCache`, com System.Text.Json (padrões Web) e chave versionada `template:product:v1:{id}`. O `InstanceName` não é mais usado; as chaves antigas `redis-localProduct_*` simplesmente expiram.
- `CacheSettings` (seção `CacheSettings`): `Enabled` (true), `AbsoluteExpiration` (10 min), `SlidingExpiration` (2 min) e `OperationTimeout` (1 s). A validação roda no startup (`ValidateOnStart`) e exige valores positivos e deslizante ≤ absoluta.
- Cada operação tem timeout por `CancellationTokenSource` vinculado mais `WaitAsync`, então um backend travado que ignora o token também é cortado.
- Payload ilegível, `null` ou com `Id` divergente vira miss e a entrada é removida.
- Os logs registram operação, chave e tipo da exceção, sem payload e sem mensagem da exceção.
- `NoOpProductCache` é usado quando `Enabled=false`. A escolha acontece em tempo de resolução via `IOptions<CacheSettings>`, para não depender da ordem de configuração do host.
- `AddCachingInfrastructure(configuration, useInMemoryCache)` usa `AddDistributedMemoryCache` no ambiente Test e `AddStackExchangeRedisCache` com `ConnectionStrings:RedisConnection` fora dele. A conexão Redis é preguiçosa: com o cache desabilitado, nunca há conexão.
- Pacotes novos no CPM, verificados no nuget.org em 2026-10-10 e alinhados à família 10.0.12 já fixada: `Microsoft.Extensions.Caching.Memory` 10.0.12 e `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.12.

### WebApi e solução

- `RedisExtensions.cs` foi removido. `Program.cs` chama `AddCachingInfrastructure(builder.Configuration, useInMemoryCache: isTestEnvironment)`.
- O projeto foi adicionado à `TemplateCleanArchitecture.sln`, na pasta de solução `Infrastructure` existente, e à `TemplateCleanArchitecture.App.slnf`. O WebApi o referencia.

### Regras de arquitetura

- Removidas as exceções da Fase 8 em `ArchitectureRulesTests.cs` (o pacote StackExchangeRedis e o arquivo `ProductService.cs`) e em `check-boundaries.ps1` (agora a allowlist cobre as Fases 9–10).
- Correção fora do escopo original: `WebApi_Is_The_Only_Project_That_Composes_Infrastructure` varria os `.csproj` do worktree local `.claude/worktrees/fase-8-redis-adapter-f4fda8` e falhava. A varredura agora ignora `.claude/`, da mesma forma que já ignorava `obj/` e `ExtensionVsix/`. A regra em si não foi afrouxada.

## Testes adicionados ou alterados

- **Unitários (+5):** `ProductServiceTests` (hit, miss com gravação e repasse do token, NotFound sem cache, cancelamento propagado). Os testes de Update e Delete verificam a invalidação, e um deles verifica a ordem (save antes de remove), a ausência de invalidação em NotFound e o repasse do token.
- **Integração (+16):** `DistributedProductCacheTests` cobre miss, hit e formato da chave, expiração absoluta, invalidação, 3 payloads ilegíveis, backend falhando, backend travado (timeout), cancelamento do chamador, Redis inacessível (loopback porta 1, sem serviço real), registro NoOp e defaults, e 3 configurações inválidas.
- **Funcionais (+4):** a implementação ativa no host de teste; invalidação após update (v1 PUT seguido de GET `/{id}/cache`); invalidação após delete (GET v2 `/{id}/cache` responde 404); e o caminho com cache desabilitado (`CacheSettings:Enabled=false` resolve `NoOpProductCache` e sempre serve dados atualizados). `CustomWebApplicationFactory` ganhou o hook `AdditionalConfiguration`.

## Validação

`powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1` rodou de 16:20 a 16:22 e todos os passos terminaram com exit=0:

| Passo | Resultado |
|---|---|
| restore / build WebApi | OK, 0 avisos, 0 erros |
| Architecture | 12/12 |
| Unit | 43/43 |
| Integration | 27/27 |
| PostgreSQL (Testcontainers) | 5/5 |
| Functional | 31/31 |
| Pacotes vulneráveis | nenhum, incluindo `Template.Infrastructure.Caching` |
| `docker compose config -q` | OK |
| `graphify update . --force` | 1.643 nós, 3.244 arestas, 107 comunidades |
| `git diff --check` | OK |

`check-boundaries.ps1` também passou: "Architecture boundary scan passed.".

### OpenAPI e baseline (`docs/refactoring/baseline-phase-6`)

- `openapi-v1.json` e `openapi-v2.json` são semanticamente idênticos ao baseline (comparação JSON). Não houve mudança de contrato.
- `health.json`, `product-v1-get.json` e `product-v2-get.json` diferem apenas em campos voláteis: `totalDuration` e o `createdDateTime` gerado no seed de cada execução.

## Não verificado e riscos

- Não houve teste contra um Redis real: o comportamento com Redis vivo, a serialização no servidor e a expiração no Redis não foram exercitados. Fica como pendência para um perfil opt-in ou para a Fase 15.
- Com Redis indisponível, cada operação pode levar até `OperationTimeout` (1 s) antes de virar miss, o que acrescenta latência ao GET com cache, mas não erro.
- Não há health check de Redis. Isso é deliberado: o cache é opcional e não deve impedir o startup.
- Rollback: restaurar `RedisExtensions` e o `ProductService` anterior, ou trocar o prefixo de chave para `v2` se houver mudança de formato.
