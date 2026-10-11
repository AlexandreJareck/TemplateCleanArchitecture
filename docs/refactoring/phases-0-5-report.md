# Relatório das Fases 0–5 — 2026-10-10

Checkpoint fechado em 2026-10-10 às 14:26 (America/Sao_Paulo), com a validação sequencial verde. **As Fases 6 e posteriores não foram iniciadas.** Não houve commit, push nem PR, e as mudanças pré-existentes da working tree foram preservadas.

## Mudanças por fase

| Fase | Mudanças |
|---|---|
| 0 — Baseline | SDK fixado pelo `global.json` (10.0.401, sem prerelease). `TemplateCleanArchitecture.App.slnf` isola a aplicação do VSIX. Baseline registrado em `baseline-2026-10-10.md`. Script `scripts/capture-api-baseline.ps1` e snapshots em `docs/refactoring/baseline/`. |
| Workspace de IA | 6 skills em `.agents/skills`, 4 agentes em `.agents/agents`, avaliação de ativação 12/12 em `.agents/evals`. |
| 1 — Host de teste | O ambiente Test não inicializa Elasticsearch, Redis externo nem consumidores RabbitMQ. Usa cache distribuído em memória e bancos InMemory isolados por factory. Teste negativo de serviços externos adicionado. |
| 2 — Pacotes | Central Package Management (`Directory.Packages.props`). Microsoft/EF 10.0.12, Npgsql EF 10.0.3, Swashbuckle 10.3.0, Microsoft.NET.Test.Sdk 18.10.1, System.IdentityModel.Tokens.Jwt 8.23.0. Removidos ASP.NET Identity 2.3.9 e SQL Server não usados. |
| 3 — Arquitetura | Projeto `Tests/Template.Architecture.Tests` com exceções temporárias vinculadas às fases futuras. |
| 4 — Domínio | Domain em `net10.0`. `ProductEntity.Price` passa a `decimal`, com invariantes. `ProductDto` movido para a Application. Precisão 18,2. `ApplyConfigurationsFromAssembly`. Factory de design-time. Migration `20261010151729_ProductPriceDecimal`. |
| 5 — Casos de uso | Ver o detalhamento abaixo. |

### Fase 5 em detalhe

- **Categorias de resultado:** `Wrappers/ErrorCategory.cs` define Validation, NotFound, Conflict, Unauthorized, Forbidden e Unexpected. O adaptador `ToCategory`/`GetCategory`/`GetErrorCategory` usa métodos de extensão e não é serializado. `ErrorCode` não foi renumerado (0–5), e um teste fixa esses valores. Nenhum `ErrorCode` atual mapeia para Conflict. Como criar um código novo exigiria um mapeamento HTTP, isso fica para a Fase 6.
- **Relógio:** `Interfaces/IClock.cs` e `Services/Time/SystemClock.cs`, registrados com `TryAddSingleton`. O `CreateProductCommandHandler` usa `clock.UtcNow`. A auditoria em `EntityFrameworkExtensions` mantém `DateTime.UtcNow` para não alterar o construtor do `ApplicationDbContext` (risco: microssegundos de diferença entre `CreatedDateTime` da resposta e `Created` persistido, igual ao comportamento anterior).
- **CancellationToken ponta a ponta:** `IGenericRepository`, `IProductRepository` e `IUnitOfWork` recebem `CancellationToken cancellationToken = default`. As implementações EF repassam o token para `FindAsync`, `AddAsync`, `ToListAsync`, `CountAsync` e `SaveChangesAsync`, e os handlers e o `ProductService` (cache) também. Os controllers de produto v1/v2 passam a receber o token da requisição e a enviá-lo ao MediatR. Isso não altera o OpenAPI, conforme a comparação abaixo.
- **Pipeline de validação:** `Behaviors/ValidationBehavior.cs`, registrado via `AddOpenBehavior`. Valida de forma assíncrona com o token, usando um `ValidationContext` novo por validador. Um contexto compartilhado acumula falhas e duplicava erros; o defeito foi pego pelo teste de agregação. Lança `FluentValidation.ValidationException`, já tratada pelo `ErrorHandlerMiddleware`.
- **Organização:** request, handler e validator continuam agrupados por arquivo de feature. Nenhuma referência ASP.NET nos handlers, o que agora é verificado por teste de arquitetura.
- **Scripts:** `scripts/run-phase-validation.ps1` (gates sequenciais, logs em `artifacts/validation`, que é git-ignored). `capture-api-baseline.ps1` ganhou `-OutputDirectory` e passou a ser compatível com Windows PowerShell 5.1, que não tem `Start-Process -Environment`.

## Versões e fontes oficiais

As versões e fontes verificadas estão em `docs/phase-2-workspace.md`: SDK em <https://dotnet.microsoft.com/en-us/download/dotnet/10.0> e pacotes em suas páginas do NuGet, consultados em 2026-10-08 e 2026-10-10. Nenhum pacote foi alterado na Fase 5. MediatR permanece em 14.0.0 (14.2.0 disponível, não adotado).

## Testes e gates (execução de 2026-10-10, 14:25–14:26)

Comandos executados em sequência com `C:\Users\alexa\.dotnet\dotnet.exe` por `scripts/run-phase-validation.ps1`:

| Gate | Resultado |
|---|---|
| `restore TemplateCleanArchitecture.App.slnf` | OK |
| `build Template.WebApi` | OK, 0 avisos, 0 erros |
| Arquitetura | 9/9 |
| Unitários | 38/38 |
| Integração | 11/11 |
| Funcionais | 20/20 |
| `list package --vulnerable --include-transitive` | Nenhum pacote vulnerável |
| `docker compose config -q` | OK |
| Graphify `update . --force` | 1.333 nós, 2.458 arestas, 100 comunidades. Avisos: `tree_sitter_sql` ausente; rótulos de comunidade desatualizados (`graphify label`). |
| `git diff --check` | Exit 2, somente em arquivos pré-existentes (ver Riscos) |

Na primeira execução (14:01), o unitário falhou 37/38 por causa do defeito de contexto compartilhado e a captura do OpenAPI falhou por incompatibilidade com o PowerShell 5.1. As duas coisas foram corrigidas antes da segunda execução.

## Comparação de contrato

`artifacts/validation/openapi` comparado estruturalmente com `docs/refactoring/baseline`, que não foi sobrescrito:

- `openapi-v1.json` e `openapi-v2.json`: **idênticos** (0 diferenças).
- `product-v1-get.json` e `product-v2-get.json`: só muda `createdDateTime`, que é o horário do seed em cada execução.
- `health.json`: só muda `totalDuration`.

## Revisão da migration e do SQL (Fase 4)

- Migration gerada pelo CLI e não editada à mão. O `Up` altera `Price` de `double precision` para `numeric(18,2)` e altera `Name`/`BarCode` de `text` nulo para `varchar(100)`/`varchar(50)` não nulos, com default `''`. O `Down` reverte tipos e nulabilidade.
- `docs/refactoring/product-price-decimal.sql` mostra a transação. Riscos operacionais:
  - Valores de preço com mais de 2 casas são arredondados.
  - Valores fora de 18,2 fazem o `ALTER` falhar.
  - `Name` acima de 100 ou `BarCode` acima de 50 caracteres fazem o `ALTER` falhar.
  - Nulos viram `''`, e o `Down` não restaura esse dado.
- **Preflight recomendado:** antes de aplicar, rodar `SELECT count(*)` para `Name`/`BarCode` nulos, `length(Name) > 100`, `length(BarCode) > 50`, `Price <> round(Price::numeric, 2)` e `abs(Price) >= 1e16`. Fazer backup da tabela `Products`.
- **Rollback:** `dotnet ef database update InitialPostgresMigration` ou restaurar o backup. Arredondamentos e nulos convertidos só voltam pelo backup.
- Ainda não foi aplicada contra um PostgreSQL real; isso é da Fase 7.

## Riscos e pendências

- `git diff --check` acusa espaços no fim de linha em arquivos que já estavam modificados antes deste trabalho: VSIX, `.resx`/`.Designer.cs` gerados, `OrderConsumer.cs`, a versão staged de `Template.Application.csproj`, `Resources/ServiceRegistration.cs` e `docker-compose.override.yml`. Não foram tocados, porque os arquivos gerados não podem ser editados à mão. Nenhum arquivo das Fases 4–5 aparece na lista.
- Com `AddFluentValidationAutoValidation` ainda ativo, as requisições HTTP válidas são validadas duas vezes, e as inválidas continuam devolvendo o 400 do MVC. A remoção do pacote sem suporte fica para a Fase 6.
- Falta uma categoria Conflict com código público. A auditoria ainda usa o relógio do sistema.
- Os testes de cancelamento de persistência usam EF InMemory e não provam o comportamento do PostgreSQL (Fase 7).
- O VSIX continua fora do gate.

## Confirmação

Nenhuma mudança das Fases 6+ foi feita: não houve migração de versionamento, Problem Details, mudança de rotas ou status codes, nem alteração de repositórios além da propagação de cancelamento prevista na Fase 5.
