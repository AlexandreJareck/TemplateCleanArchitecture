Trabalhe no repositório:

C:\Users\alexa\source\repos\TemplateCleanArchNET

Objetivo: executar integralmente somente as Fases 6 e 7 do plano de refatoração. Não implemente nada da Fase 8 em diante.

## Estado de partida (verificado em 2026-10-10 às 14:26)

- Fases 0–5 concluídas e validadas. Leia `docs/refactoring/phases-0-5-report.md` antes de tudo.
- Gates verdes: build da WebApi com 0 avisos; arquitetura 9/9; unitários 38/38; integração 11/11 (EF InMemory); funcionais 20/20, autocontidos; nenhum pacote vulnerável; `docker compose config -q` ok.
- O OpenAPI v1/v2 atual é idêntico a `docs/refactoring/baseline/`. Use esse diretório como baseline de contrato da Fase 6 e não o sobrescreva.
- Commit base: `95d04acbfeeaa1e259a3a76f74be3072c561c415`. A working tree está muito modificada (cerca de 160 entradas, entre staged, unstaged e não rastreadas). Preserve tudo. Não faça commit, push, PR, stash, reset, checkout nem clean sem autorização explícita.
- `git diff --check` sai com código 2 por espaços no fim de linha em arquivos pré-existentes (VSIX, `.resx`/Designers, `OrderConsumer.cs`, `docker-compose.override.yml`). Isso é conhecido. Não "corrija" arquivos gerados.

## Fontes obrigatórias (ler antes de alterar qualquer arquivo)

- `AGENTS.md`, `CLAUDE.md`
- `docs/fase-2-workspace.md` (matriz de versões e fontes)
- `docs/fase-3-plano-implementacao.md` (Fases 6 e 7 e a tabela de decisões no final)
- `Src/Core/AGENTS.md`, `Src/Infrastructure/AGENTS.md`, `Src/Presentation/AGENTS.md`, `Tests/AGENTS.md`
- Skills em `.agents/skills/`: `dotnet-change-verification`, `api-contract-change`, `package-upgrade`, `architecture-boundary-check` e `ef-migration` (esta só se uma migration for realmente necessária)
- Agentes revisores em `.agents/agents/`, quando quiser uma revisão independente

## Execução de comandos

- Use sempre `C:\Users\alexa\.dotnet\dotnet.exe` (SDK 10.0.401 fixado pelo `global.json`). Rode builds e testes em sequência, nunca em paralelo.
- Gate completo: `powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1`. Ele grava logs em `artifacts/validation/*.log` e `summary.txt`, captura o OpenAPI em `artifacts/validation/openapi` sem tocar no baseline e roda o Graphify. Funciona no Windows PowerShell 5.1.
- Para comparar contrato, faça um diff estrutural de JSON entre `artifacts/validation/openapi/*.json` e `docs/refactoring/baseline/*.json`. Ignore campos voláteis (`createdDateTime` do seed e `totalDuration` do health).
- Se o seu ambiente não puder executar o dotnet do Windows (por exemplo, um shell Linux sem SDK), peça ao usuário para rodar o script e leia os logs. Nunca declare um gate verde sem log.

## Contexto técnico relevante

- Application: os handlers ficam em `Features/Products/{Commands,Queries}`, com request, handler e validator no mesmo arquivo. Retornam `BaseResult`/`PagedResponse` com `Error(ErrorCode, ...)`.
- `ErrorCode` é contrato público (0 ModelStateNotValid, 1 FieldDataInvalid, 2 NotFound, 3 AccessDenied, 4 ErrorInIdentity, 5 Exception). Não renumere. `Wrappers/ErrorCategory.cs` já mapeia `ErrorCode` para Validation, NotFound, Conflict, Unauthorized, Forbidden e Unexpected (`ToCategory`, `GetErrorCategory`). Use isso no componente de Problem Details. Hoje nenhum `ErrorCode` cai em Conflict; se criar um, adicione valor novo (6+) e teste.
- `IClock`/`SystemClock` estão registrados. O `ValidationBehavior<,>` do MediatR 14 está registrado com `AddOpenBehavior` e lança `FluentValidation.ValidationException`.
- O `CancellationToken` já é propagado: controllers de produto v1/v2 → MediatR → handlers → `IGenericRepository`/`IProductRepository`/`IUnitOfWork` → EF.
- HTTP atual:
  - `BaseApiController` resolve `Mediator` por service locator (`HttpContext.RequestServices`), e `MapError` gera um objeto `{errors:[{code,field,message}]}` que não é Problem Details.
  - `ErrorHandlerMiddleware` serializa `BaseResult` e devolve `error.Message` no 500, ou seja, vaza detalhe interno.
  - `ApiResultFilterAttribute` não está aplicado.
  - `AddFluentValidationAutoValidation` (pacote `FluentValidation.AspNetCore` 11.3.1, sem suporte) faz o `[ApiController]` devolver `ValidationProblemDetails` em 400.
  - `Infrastructure/Enums/ErrorCodeEnum.cs` não é usado.
  - Rotas v1: `get-by-id?id=`, `get-paged-list`, POST/PUT/DELETE em `/api/v1/product`, com DELETE e PUT recebendo id por query/body.
  - Rotas v2: `/{id:guid}`, `/{id:guid}/cache`, `get-paged-list`, POST, PUT `/{id}` e DELETE `/{id}`. O POST v2 responde 200, embora a documentação diga 201.
- Versionamento: o pacote legado é `Microsoft.AspNetCore.Mvc.Versioning.ApiExplorer` 5.1.0. Configuração em `Infrastructure/Extensions/SwaggerExtensions.cs`. Os controllers usam `[ApiVersion]`. O alvo indicado é `Asp.Versioning.Mvc.ApiExplorer` 10.x; consulte a versão estável e o guia oficial de migração no dia da execução.
- Persistência:
  - `ServiceRegistration` registra os repositórios por reflexão.
  - `GenericRepository` usa `DbContext`; `ProductRepository` monta `IQueryable` e pagina com `OrderBy(Created)`, que não é determinístico em empates.
  - `UnitOfWork` não tem semântica transacional explícita e ainda expõe um `SaveChanges()` síncrono.
  - A auditoria (`EntityFrameworkExtensions.ApplyAuditing`) usa `dynamic` e `DateTime.UtcNow`.
  - Migration pendente: `20261010151729_ProductPriceDecimal`. A revisão, os riscos, a verificação prévia e o rollback estão no relatório 0–5. Ainda não foi aplicada em PostgreSQL real.
  - Os pacotes SQL Server já foram removidos na Fase 2. Confirme no código, nos csproj e nas migrations que não restou nenhuma referência.
- Testes de arquitetura (`Tests/Template.Architecture.Tests/ArchitectureRulesTests.cs`): toda regra nova precisa de mutação controlada. As exceções temporárias para Program.cs e serviços Redis/RabbitMQ/Elastic têm fase marcada; não remova as das Fases 8–10.

## Fase 6 — controllers e contratos HTTP

Use a v2 como referência, sem tratá-la como padrão já concluído.

1. Injetar `ISender`/`IMediator` por construtor e remover o service locator do `BaseApiController`.
2. Migrar `Microsoft.AspNetCore.Mvc.Versioning.*` para `Asp.Versioning.*` estável, após ler a documentação e o guia oficial. Atualizar `Directory.Packages.props`.
3. Criar um único componente de fronteira que converta resultados da Application em RFC Problem Details (`ProblemDetails`/`ValidationProblemDetails`, `application/problem+json`), usando `ErrorCategory`.
4. Mapear: validation → 400, unauthenticated → 401, forbidden → 403, not found → 404, conflict → 409, unexpected → 500 sem detalhes internos. O middleware de exceções também deve parar de expor `error.Message`.
5. Deixar explícito o binding de rota, query e body.
6. Propagar `CancellationToken` em todos os controllers, incluindo Account.
7. Rotas REST e status codes consistentes para CRUD (por exemplo, create → 201 com Location, delete → 204). Qualquer mudança só na v2 ou numa versão nova, de forma explícita.
8. **Decisão 2 registrada:** a v1 continua funcionando com as mesmas rotas e status codes e é marcada como deprecated no OpenAPI. Sem data de remoção. Documente o ciclo de vida da v1.
9. Avaliar a saída do `FluentValidation.AspNetCore` (sem suporte) em favor do pipeline já existente, garantindo que o formato 400 fique coberto por teste.
10. Regenerar e comparar o OpenAPI v1/v2. Registre o novo baseline aprovado em `docs/refactoring/baseline-fase-6/`, sem apagar o original.

Testes funcionais obrigatórios:
- rotas v1 e v2;
- autenticação (401) e autorização (403);
- validação (400);
- not found (404);
- conflict, se aplicável;
- create, update e delete;
- formato Problem Details;
- 405 e versão não suportada;
- diferenças aprovadas entre v1 e v2.

Critério de conclusão:
- WebApi compila e todas as suítes passam;
- o diff OpenAPI contém só mudanças aprovadas e documentadas;
- não há service locator nos controllers (adicione teste de arquitetura);
- erros não expõem detalhes internos.

Se a migração de versionamento alterar comportamento não previsto, reverta o conjunto de pacote, configuração e controllers e **não** inicie a Fase 7.

## Fase 7 — persistência e PostgreSQL (somente depois da Fase 6 aprovada)

1. Registro explícito dos repositórios, sem reflexão.
2. Avaliar o repositório genérico: mantê-lo só onde houver benefício comprovado, preservando os ports da Application durante a transição.
3. Nenhum `IQueryable` atravessando a fronteira da Infrastructure (adicione teste de arquitetura).
4. Ordenação determinística antes de toda paginação, por exemplo `Created` e depois `Id`.
5. Semântica explícita de Unit of Work e transações, com commit e rollback.
6. `CancellationToken` em toda operação assíncrona.
7. Remoção de qualquer resquício de SQL Server, depois de provar que não é usado.
8. Testes reais com PostgreSQL, em projeto ou perfil opt-in separado, usando uma imagem 16.x fixada e sem upgrade major. Devem cobrir:
   - migrations, incluindo `ProductPriceDecimal`, com a verificação prévia do relatório;
   - CRUD;
   - paginação e ordenação determinística;
   - precisão decimal 18,2;
   - concorrência;
   - commit e rollback;
   - cancelamento;
   - transação entre repositórios.

   EF InMemory não serve como prova. Para subir o PostgreSQL, prefira Testcontainers ou um serviço isolado do compose, sem expor credenciais em log.

Não reescreva migrations aplicadas. Se precisar mudar o schema, crie uma migration aditiva via CLI, revise o SQL gerado (`dotnet ef migrations script`) e documente o rollback.

Critério de conclusão:
- todas as suítes passam, incluindo PostgreSQL;
- nenhum `IQueryable` cruza a fronteira;
- paginação determinística;
- transações e cancelamento cobertos;
- nenhuma referência a SQL Server;
- HTTP da Fase 6 estável (OpenAPI idêntico ao baseline da Fase 6).

## Regras gerais

- Consulte versões de pacotes e guias de migração em fontes oficiais no dia da execução, e registre data e URL.
- Não mova Redis, RabbitMQ ou Elastic. Não faça upgrade major de PostgreSQL nem de outras integrações.
- Não inclua segredos nem valores de configuração em logs e documentação; só nomes de chaves.
- Não edite à mão migrations, Designers, snapshots, Designers de `.resx` nem `graphify-out`. Regenere o Graphify após mudanças estruturais.
- Cada mudança de comportamento precisa de teste.

## Entrega

Gere `docs/refactoring/phases-6-7-report.md` com:
- arquivos alterados;
- resultado de cada fase;
- versões e fontes consultadas;
- mudanças de contrato HTTP e seus testes;
- diff OpenAPI;
- mudanças de persistência;
- migrations e SQL revisados;
- testes PostgreSQL executados;
- comandos e resultados;
- riscos, rollbacks e pendências.

Atualize `AGENTS.md` só com comandos realmente verificados, e confirme explicitamente que nada das Fases 8+ foi feito. Não declare conclusão com algum critério pendente.
