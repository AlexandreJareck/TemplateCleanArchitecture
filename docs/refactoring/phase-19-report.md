# Fase 19 — Atualização do ferramental de testes e cobertura

Data de execução e verificação: 2026-10-12 (America/Sao_Paulo). Checkout autoritativo `C:\Users\alexa\source\repos\TemplateCleanArchNET`, branch `main` em `c0d4786` (merge da Fase 18), `git status` limpo antes das edições. SDK fixado `C:\Users\alexa\.dotnet\dotnet.exe` 10.0.401 (`global.json`).

## 1. Resumo executivo e disposição final

| Família | Antes | Depois | Decisão |
|---|---|---|---|
| `Microsoft.NET.Test.Sdk` | 18.10.1 | 18.10.1 | Sem mudança: já é o último estável (publicado 2026-09-15). |
| Moq | 4.20.72 | **4.21.0** | Atualizado (Castle.Core transitivo 5.1.1 → 5.2.1, só em testes). |
| `coverlet.collector` | 6.0.4 | **10.1.0** | Atualizado em lote separado; metadados de assets alinhados no projeto funcional. |
| `xunit.runner.visualstudio` | 3.1.5 | 3.1.5 | **Mantido.** O 4.0.1 foi testado: CLI e `vstest.console` do VS 2022 funcionaram, mas o Test Explorer não foi validado (detalhes na seção 7). |
| `xunit` | 2.9.3 | 2.9.3 | Fora de escopo (não alterado). |

- Descoberta e execução **idênticas por projeto e por nome de teste** antes/depois: 30, 107, 141, 5, 19, 13, 23 e 191 (total 529), todos aprovados, sem testes ignorados.
- Cobertura: entre coverlet 6.0.4 e 10.1.0, módulos, fontes, linhas e métodos ficaram idênticos. Só a contagem de branches mudou, em 15 linhas com padrões `is { … }`, `and`/`or`, `??=` e lambdas, todas explicadas pelas correções do coverlet 8–10. O formato Cobertura 1.9 foi mantido; o atributo `filename` passou a usar `/`.
- Novo consumidor local de cobertura (`scripts/coverage-report.ps1`). Ele valida a estrutura, consolida os relatórios por módulo e aplica limites **por módulo** (`Tests/test-coverage-policy.json`). Entrou no gate como passo `07b-coverage`. A prova negativa retornou exit 1.
- O grafo de pacotes de **produção** resolvido ficou idêntico: 246 entradas, 0 diferenças. Nenhum pacote de teste aparece em produção.
- OpenAPI v1/v2 idênticos ao baseline. Nenhuma vulnerabilidade nos 17 projetos. `docker compose config -q` passou, e o gate completo também (todos os passos com exit 0).
- **Disposição: Fase 19 concluída**, com o runner mantido em 3.1.5. A validação do Test Explorer do Visual Studio fica **não verificada** (seção 7). Fase 20 não iniciada.

## 2. Checkout, pré-requisitos e preservação de trabalho

- Relatórios presentes: `phases-0-5`, `phases-6-7`, `phase-8` … `phase-18`. A Fase 18 (último merge, `c0d4786`) registrou 529 testes descobertos (30/107/141/5/19/13/23/191). Essas contagens foram **reproduzidas** nesta fase antes de qualquer edição (seção 5); o relatório não foi tomado como prova.
- Árvore limpa no início (`git status --short` vazio). Nenhuma mudança concorrente foi encontrada nem sobrescrita. Ao longo da fase, os arquivos foram relidos antes de cada edição.
- Docker Desktop 28.4.0 disponível: as suítes opt-in (PostgreSQL, RabbitMQ, Elasticsearch, Redis) rodaram de verdade, com containers descartáveis do Testcontainers. Nenhum volume, fila, índice ou banco do usuário foi tocado.
- Visual Studio Community 2022 17.14 e VS 18 Insiders estão instalados. Apenas o `vstest.console.exe` do VS 2022 (VSTest 17.14.0) foi usado, em linha de comando.

## 3. Inventário pré-mudança

### Projetos de teste (todos `net10.0`, adaptador VSTest `xunit.runner.visualstudio` 3.1.5, framework xUnit 2.9.3, host `Microsoft.NET.Test.Sdk` 18.10.1)

| Projeto | Moq | coverlet | Pré-requisito / opt-in |
|---|---|---|---|
| `Tests/Template.Architecture.Tests` | — | — | nenhum |
| `Tests/Template.Unit.Tests` | sim | sim (`PrivateAssets=all`) | nenhum |
| `Tests/Template.Integration.Tests` | — | sim (`PrivateAssets=all`) | nenhum (fakes loopback) |
| `Tests/Template.PostgreSql.Integration.Tests` | — | — | Docker (`postgres:16.15` por digest) |
| `Tests/Template.RabbitMq.Integration.Tests` | — | — | Docker (`rabbitmq:4.3.6-management`; `TEMPLATE_RABBITMQ_TEST_IMAGE` opcional) |
| `Tests/Template.Elasticsearch.Integration.Tests` | — | — | Docker (`elasticsearch:9.5.5` e `8.19.23`) |
| `Tests/Template.Redis.Integration.Tests` | sim | — | Docker (`redis:8.10.2`) |
| `Tests/Template.Functional.Tests` | — | sim (**sem** `PrivateAssets`) | nenhum (host Test autocontido) |

- As versões vêm todas do `Directory.Packages.props` (CPM com `CentralPackageTransitivePinningEnabled`). Nenhum projeto declara versão local. O runner tem `PrivateAssets=all` em todos os projetos.
- Não há `[Trait]`, filtros, `Skip` nem `.runsettings`. O único opt-in por variável de ambiente é `TEMPLATE_RABBITMQ_TEST_IMAGE`.
- As suítes opt-in não coletam cobertura (intencional): elas dependem de Docker, e o limite precisa ser reproduzível sem serviços reais. A suíte de arquitetura analisa fontes e projetos, sem executar código de produção.
- Pipeline de cobertura antes da fase: só a referência ao coletor existia. Não havia comando, `.runsettings`, exclusões, limites, script de consumo ou CI no repositório (sem `.github/`, `azure-pipelines`, etc.). **Não existia nenhum consumidor dentro do repositório.**
- O script de validação (`run-phase-validation.ps1`) não coletava cobertura e não comparava descoberta.

## 4. Pesquisa oficial (2026-10-12) e matriz de compatibilidade

Fontes primárias consultadas em 2026-10-12:

- Índice e metadados oficiais do NuGet (versões, data de publicação, `listed`, nuspec): <https://api.nuget.org/v3-flatcontainer/microsoft.net.test.sdk/index.json>, <https://api.nuget.org/v3-flatcontainer/moq/index.json>, <https://api.nuget.org/v3-flatcontainer/coverlet.collector/index.json>, <https://api.nuget.org/v3-flatcontainer/xunit/index.json>, <https://api.nuget.org/v3-flatcontainer/xunit.runner.visualstudio/index.json>; páginas <https://www.nuget.org/packages/Microsoft.NET.Test.Sdk>, <https://www.nuget.org/packages/Moq>, <https://www.nuget.org/packages/coverlet.collector>, <https://www.nuget.org/packages/xunit.runner.visualstudio>.
- Microsoft.NET.Test.Sdk / VSTest: nuspec 18.10.1 (dependências `Microsoft.TestPlatform.TestHost` e `Microsoft.CodeCoverage` 18.10.1), <https://github.com/microsoft/vstest/releases>.
- Moq 4.21.0: <https://github.com/devlooped/moq/releases/tag/v4.21.0> (publicado em 2026-09-24).
- coverlet: <https://github.com/coverlet-coverage/coverlet/blob/master/Documentation/Changelog.md> (6.0.4 → 8.0.0 → 8.0.1 → 10.0.0 → 10.0.1 → 10.1.0).
- xUnit VS runner: <https://xunit.net/releases/visualstudio/4.0.0>, <https://xunit.net/releases/visualstudio/4.0.1>, <https://github.com/xunit/visualstudio.xunit> (README) e nuspec 3.1.5/4.0.1.

| Pacote | Último estável | Publicado | Pontos relevantes |
|---|---|---|---|
| Microsoft.NET.Test.Sdk | 18.10.1 | 2026-09-15 | Já em uso. |
| Moq | 4.21.0 | 2026-09-24 | Minor. Muda o desempenho do matcher `It.Is`, a nulabilidade dos parâmetros de construtor e o registro lazy de `ValueTask` (#1648). Não há quebra declarada. Castle.Core vai para 5.2.1. |
| coverlet.collector | 10.1.0 | 2026-09-27 | Breaking em 8.0.0: SDK/runtime mínimo .NET 8. A 10.0.0 suporta .NET 10. Na 10.0.1, "paths inconsistentes em cobertura" passam a usar `/`. Várias correções de branch em 8–10: lambdas, `is … or`/`and`, `finally` assíncrono. `developmentDependency=true`. |
| xunit | 2.9.3 | 2025-01-08 | Último v2. Não alterado. |
| xunit.runner.visualstudio | 4.0.1 | 2026-10-09 | A 4.0.0 (2026-08-14) chegou junto com o xUnit v3 4.0. Nuspec da 4.0.1: *"Capable of running xUnit.net v1, v2, and v3 tests. Supports .NET 4.7.2 or later, and .NET 8 or later."* O README também diz que roda xUnit 1.9.2+. As notas não declaram quebras e indicam possível descontinuação futura do pacote. |

| Componente | Valor no repositório | Compatível com Test SDK 18.10.1 | coverlet 10.1.0 | runner 3.1.5 | runner 4.0.1 |
|---|---|---|---|---|---|
| SDK .NET 10.0.401 / `net10.0` | fixo | sim (verificado) | sim (≥ .NET 8; 10 suportado desde 10.0.0) | sim (.NET 8+) | sim (.NET 8+, verificado em CLI) |
| xUnit 2.9.3 (v2) | fixo | — | indiferente (coletor VSTest) | sim (v1/v2/v3) | sim pela documentação oficial; CLI verificado |
| Host CLI `dotnet test` (VSTest) | SDK 10.0.401 | sim | sim (`XPlat Code Coverage`) | sim | sim (verificado) |
| `vstest.console` do VS 2022 17.14 (VSTest 17.14.0) | instalado | sim | não exercido | sim (verificado) | sim (verificado) |
| Test Explorer (VS 2022) | instalado | **não verificado** | — | **não verificado** | **não verificado** |
| Consumidor de cobertura | `scripts/coverage-report.ps1` (novo) | — | Cobertura 1.9 (verificado) | — | — |

## 5. Descoberta e execução por projeto

Comando por projeto (`scripts/test-tooling-evidence.ps1`, após build da `TemplateCleanArchitecture.App.slnf` sem paralelismo):

- descoberta: `dotnet test <proj> --no-build --list-tests` (saída forçada em inglês), com a lista ordenada salva em `discovered.txt`;
- execução: `dotnet test <proj> --no-build --logger "trx;LogFileName=results.trx" --results-directory …` (com `--collect "XPlat Code Coverage"` nos 3 projetos com coletor), contando pelos contadores do TRX;
- em `before`/`final`, também a descoberta com `vstest.console.exe /ListTests` do VS 2022;
- `-CompareWith` compara contagens **e nomes**; diferença ou zero teste ⇒ exit 1.

Evidências em `artifacts/validation/test-tooling/<rótulo>/` (git-ignorado).

| Projeto | Descobertos antes | Executados/aprovados antes | `vstest.console` VS antes | Descobertos depois | Executados/aprovados depois | `vstest.console` VS depois | Nomes diferentes |
|---|---:|---:|---:|---:|---:|---:|---:|
| Architecture | 30 | 30/30 | 30 | 30 | 30/30 | 30 | 0 |
| Unit | 107 | 107/107 | 107 | 107 | 107/107 | 107 | 0 |
| Integration | 141 | 141/141 | 141 | 141 | 141/141 | 141 | 0 |
| PostgreSql (Docker) | 5 | 5/5 | 5 | 5 | 5/5 | 5 | 0 |
| RabbitMq (Docker) | 19 | 19/19 | 19 | 19 | 19/19 | 19 | 0 |
| Elasticsearch (Docker) | 13 | 13/13 | 13 | 13 | 13/13 | 13 | 0 |
| Redis (Docker) | 23 | 23/23 | 23 | 23 | 23/23 | 23 | 0 |
| Functional | 191 | 191/191 | 191 | 191 | 191/191 | 191 | 0 |
| **Total** | **529** | **529/529** | 529 | **529** | **529/529** | 529 | 0 |

Falhados 0 e não executados/ignorados 0 em todas as execuções. Rodadas com contagens idênticas: `before` (6.0.4/4.20.72/3.1.5), `after-moq`, `after-coverlet`, `after-coverlet-rerun`, `runner4-experiment` (adaptador 4.0.1) e `final` (adaptador 3.1.5 confirmado no `bin` de cada projeto). Durações (só diagnóstico, rodada `before`): 1,5 s / 2,1 s / 18,8 s / 6,2 s / 31,5 s / 99,5 s / 59,4 s / 59,1 s.

## 6. Cobertura antes/depois e consumidor

- Comando equivalente nas duas versões: `dotnet test <proj> --no-build --collect "XPlat Code Coverage" --results-directory <limpo>` em Unit, Integration e Functional. O coletor é configurado só com os padrões (nenhum `.runsettings`). Saída: um `coverage.cobertura.xml` por projeto em `<results>/<guid>/`; o logger TRX copia um duplicado para `<…>/In/<máquina>/`, que o consumidor ignora.
- Formato: Cobertura, `version="1.9"` nos dois casos; um `<source>` por relatório (raiz comum `Src/` ou `Src/Core/`). **Mudança no 10.x:** `filename` e `<source>` passaram a usar `/`. O consumidor resolve cada arquivo para um caminho relativo ao repositório, então a comparação não depende disso.
- Consolidação: por módulo (assembly). Uma linha conta como coberta se qualquer relatório a atingiu; para branches, vale o melhor `coberto/total` da linha; um método conta como coberto se qualquer linha dele foi atingida.
- Exclusões (só código gerado, aplicadas no consumidor, não no coletor): `*/Migrations/*` (migrações, designers e snapshots do EF) e `*.Designer.cs` (designers de `.resx`). Os números sem filtro também são publicados.

Resultado consolidado. A rodada com 4.20.72 + 6.0.4 é idêntica à `before`; com 10.1.0, `after-coverlet` e `final` são idênticas.

| Módulo | Fontes medidas/todas | Linha 6.0.4 | Linha 10.1.0 | Branch 6.0.4 | Branch 10.1.0 | Método (ambos) | Linha sem filtro |
|---|---:|---:|---:|---:|---:|---:|---:|
| Template.Domain | 5/5 | 100 (36/36) | 100 (36/36) | — (0) | — (0) | 100 (20/20) | 100 |
| Template.Application | 41/41 | 94,24 (409/434) | 94,24 | 92,21 (71/77) | 89,61 (69/77) | 87,88 (145/165) | 94,24 |
| Template.Infrastructure.Caching | 4/4 | 96,55 (112/116) | 96,55 | 93,75 (15/16) | 93,75 | 100 (21/21) | 96,55 |
| Template.Infrastructure.Identity | 14/17 | 90,29 (279/309) | 90,29 | 81,94 (59/72) | 80,56 (58/72) | 88,10 (37/42) | 51,57 |
| Template.Infrastructure.Messaging | 11/11 | 59,50 (354/595) | 59,50 | 67,81 (99/146) | 66,43 (93/140) | 85,00 (68/80) | 59,50 |
| Template.Infrastructure.Persistence | 9/14 | 66,85 (121/181) | 66,85 | 59,09 (26/44) | 56,82 (25/44) | 70,00 (21/30) | 31,59 |
| Template.Infrastructure.Resources | 2/4 | 100 (14/14) | 100 | 100 (6/6) | 100 | 100 (5/5) | 18,92 |
| Template.Infrastructure.Search | 5/5 | 96,99 (258/266) | 96,99 | 91,18 (124/136) | 87,50 (119/136) | 100 (43/43) | 96,99 |
| Template.WebApi | 32/32 | 68,33 (1221/1787) | 68,33 | 52,13 (355/681) | 51,40 (350/681) | 84,39 (146/173) | 68,33 |

Por relatório (`lines-valid`, igual nas duas versões): Unit 470, Integration 1 830, Functional 4 711. Nenhum assembly, fonte, linha ou método sumiu.

**Diferença de branch explicada.** Uma comparação linha a linha encontrou exatamente 15 linhas, todas com construções que o changelog do coverlet corrige ou reconta:

- `Errors ??= []` em `BaseResult.cs:27`;
- `is { Count: > 0 }` / `is { Length: > 0 }` em `ErrorCategory.cs:53`, nos `ServiceRegistration.cs` de Identity/Persistence e em `AuthenticatedUserService.cs:18,23`;
- `settings.Port is > 0 and <= 65535` em `RabbitMqSettings.cs:46`, onde os pontos de branch caíram de 24 para 18;
- `!settings.Enabled || …` em `PersonSearchSettings.cs:38` e nos settings de logging/health check;
- lambdas `() => _client.IndexAsync(…)` em `ElasticsearchPersonSearchIndex.cs:80,104,148,166` e `LoggingExtensions.cs:78`.

Não houve mudança de código de produção ou de teste que explique a variação; ela vem da instrumentação.

**Consumidor:** `scripts/coverage-report.ps1` lê os relatórios, rejeita o que não for Cobertura válido (contadores na raiz, `<source>`, pacotes e linhas não vazios) e grava `coverage-summary.json` e `coverage-summary.md` ao lado da entrada. Ele consumiu com sucesso as saídas do 6.0.4 (`before`, `after-moq`) e do 10.1.0 (`after-coverlet`, `final`, `coverage-gate`). Não existe CI no repositório e nenhuma integração externa foi criada nem é afirmada.

## 7. Decisão sobre o runner xUnit 4.0

- **Compatibilidade oficial:** o nuspec da 4.0.1 e o README do repositório oficial declaram suporte a xUnit v1/v2/v3 e a .NET 8+. As notas da 4.0.0/4.0.1 tratam só de recursos do v3 4.0 e correções, sem quebras declaradas.
- **Experimento real** (rodada `runner4-experiment`, somente `Directory.Packages.props` alterado e depois revertido):
  - build com 0 avisos;
  - grafo de produção sem diferenças;
  - adaptador carregado `v4.0.1+0f7d528729`;
  - descoberta e execução idênticas nos 8 projetos (529/529), inclusive as suítes Docker;
  - `vstest.console.exe` do VS 2022 também descobriu 529.
- **Não verificado:** a descoberta e a execução no **Test Explorer** do Visual Studio. O IDE está instalado, mas o Test Explorer não foi exercitado nesta execução. Pelo handoff, isso não pode ser inferido do CLI nem do `vstest.console`.
- **Decisão: manter 3.1.5**, que é suportada, foi verificada nos mesmos caminhos e não tem pendência de IDE. A 4.0.1 foi publicada em 2026-10-09 (três dias antes) e não traz benefício específico para xUnit v2. Para adotá-la, basta mudar a versão central, fazer um **rebuild** (veja abaixo) e validar o Test Explorer manualmente.
- **Achado do rollback:** o adaptador copia as DLLs para o `bin` com `PreserveNewest`. Depois de voltar de 4.0.1 para 3.1.5, um build incremental **manteve as DLLs 4.0.1** e uma rodada de evidências carregou o adaptador 4.0.1. Essa rodada foi descartada e guardada como `final-stale-adapter-4.0.1`. A correção foi `dotnet build TemplateCleanArchitecture.App.slnf --no-restore --no-incremental …`; depois dela, o adaptador 3.1.5 foi confirmado em todos os projetos. O script de evidências agora registra a versão do adaptador presente em cada `bin`.

## 8. Política de limites por camada

Arquivo versionado `Tests/test-coverage-policy.json`, aplicado por `scripts/coverage-report.ps1`. O gate executa isso no passo `07b-coverage` de `run-phase-validation.ps1`, a partir de uma pasta limpa (`artifacts/validation/test-tooling/coverage-gate`).

- **Projetos medidos:** Unit, Integration e Functional (os três com coletor), consolidados por módulo. As suítes opt-in não entram (dependem de Docker).
- **O que é aplicado:** linha e branch mínimos **por módulo**, sem média entre módulos ou camadas, e o número mínimo de fontes medidas (detecta assemblies ou fontes que sumirem). Método é só reportado. A ausência de um módulo falha o gate.
- **Ratchet inicial:** `floor(medido − 1)` sobre a medição do coverlet 10.1.0, com dados de linha e método idênticos em quatro rodadas. A folga de 1 ponto cobre variação de tempo nos caminhos de reconexão e consumo do messaging, sem permitir regressão real. Não existia limite anterior, então nenhum foi rebaixado. A meta de longo prazo não é aplicada: ao subir a cobertura, eleve os números (nunca baixe sem justificativa e aprovação).

| Módulo | Camada | Linha mín. | Branch mín. | Fontes mín. |
|---|---|---:|---:|---:|
| Template.Domain | Domain | 99 | — (sem branches) | 5 |
| Template.Application | Application | 93 | 88 | 41 |
| Template.Infrastructure.Caching | Infrastructure | 95 | 92 | 4 |
| Template.Infrastructure.Identity | Infrastructure | 89 | 79 | 14 |
| Template.Infrastructure.Messaging | Infrastructure | 58 | 65 | 11 |
| Template.Infrastructure.Persistence | Infrastructure | 65 | 55 | 9 |
| Template.Infrastructure.Resources | Infrastructure | 99 | 99 | 2 |
| Template.Infrastructure.Search | Infrastructure | 95 | 86 | 5 |
| Template.WebApi | Presentation | 67 | 50 | 32 |

**Prova negativa** (sem tocar em produção, só arquivos de política temporários em `artifacts/validation/phase-19/`):

1. Application com linha 100 e Messaging com `minimumSources` 12: **exit 1**, com `fail line 94.24% < 100%` e `fail sources 11 < 12`.
2. Entrada com só o relatório da suíte Unit: **exit 1**, com `module missing from coverage` para os módulos de infraestrutura e WebApi.

## 9. Grafos de dependências (produção × teste)

`scripts/package-graph-snapshot.ps1` roda `dotnet list package --include-transitive --format json` por projeto do `.slnf` e grava `production.txt` e `tests.txt` normalizados e ordenados em `artifacts/validation/package-graph/<rótulo>/`.

| Comparação | Produção | Testes |
|---|---|---|
| `after-moq` × `before` | 0 diferenças | Moq 4.20.72 → 4.21.0 e Castle.Core 5.1.1 → 5.2.1 (Unit, Redis) |
| `after-coverlet` × `after-moq` | 0 diferenças | coverlet.collector 6.0.4 → 10.1.0 (Unit, Integration, Functional) |
| `runner4-experiment` × `after-coverlet` | 0 diferenças | runner 3.1.5 → 4.0.1 (8 projetos) |
| `final` × `before` | **0 diferenças (246 entradas)** | só Moq, Castle.Core e coverlet (14 linhas) |

- Nenhum pacote Moq, coverlet, xunit, Test SDK, TestPlatform ou Castle aparece no grafo de produção.
- No grafo de teste, as famílias atualizadas e o ferramental de teste têm uma única versão: `Microsoft.NET.Test.Sdk`, `Microsoft.TestPlatform.*` e `Microsoft.CodeCoverage` 18.10.1; xunit 2.9.3; runner 3.1.5; Moq 4.21.0; coverlet 10.1.0.
- Versões diferentes entre projetos de teste já existiam antes da fase e não foram alteradas: `Microsoft.IdentityModel.*` 8.14.0/8.23.0, `System.Diagnostics.EventLog`, `Microsoft.Extensions.DependencyModel`. Elas vêm de dependências transitivas de produção e do Testcontainers, não do ferramental de teste.
- `PrivateAssets=all` está mantido no runner e no coletor. O coletor do projeto funcional recebeu os mesmos `PrivateAssets` e `IncludeAssets` dos outros projetos; como o projeto não é empacotável nem referenciado, o grafo resolvido não mudou.

## 10. Comandos executados (sequenciais) e resultados

| Etapa | Comando / script | Resultado |
|---|---|---|
| Baseline | `dotnet restore TemplateCleanArchitecture.App.slnf --verbosity minimal --disable-parallel` | exit 0 |
| Baseline | `dotnet build TemplateCleanArchitecture.App.slnf --no-restore --verbosity minimal --maxcpucount:1 /p:UseSharedCompilation=false` | exit 0, 0 avisos |
| Baseline | `package-graph-snapshot.ps1 -Label before` | 246 produção / 469 teste |
| Baseline | `dotnet list … package --vulnerable --include-transitive` | exit 0, nenhum vulnerável |
| Baseline | `test-tooling-evidence.ps1 -Label before -Coverage -VsTestConsolePath <VS 2022>` | exit 0, 529/529 |
| Baseline | `coverage-report.ps1 -NoEnforce` (`before`, `after-moq`) | exit 0, relatórios válidos |
| Moq | restore, build, grafo, vulnerabilidades, evidências `after-moq -Coverage -CompareWith before` | todos exit 0, 0 avisos, 529/529, 0 diferenças de nome |
| coverlet | restore, build, grafo, vulnerabilidades, evidências `after-coverlet -Coverage -CompareWith after-moq`, relatório | todos exit 0, 0 avisos, 529/529 |
| Runner 4.0.1 | restore, build, grafo, evidências `runner4-experiment` | exit 0, 529/529, adaptador 4.0.1 |
| Reversão | restore, build, **rebuild `--no-incremental`**, grafo `final -CompareWith before` | exit 0, 0 avisos, produção sem diferenças |
| Final | `test-tooling-evidence.ps1 -Label final -Coverage -CompareWith before -VsTestConsolePath …` | exit 0, adaptador 3.1.5 nos 8 projetos, 529/529 |
| Final | `coverage-report.ps1` (política versionada) | exit 0, 9/9 módulos aprovados |
| Final | provas negativas (política alterada; módulos ausentes) | exit 1 e exit 1 (esperado) |
| Final | `capture-api-baseline.ps1` + comparação com `docs/refactoring/baseline-phase-6` | `openapi-v1.json` e `openapi-v2.json` idênticos (normalizando CRLF). `health.json` e `product-v*-get.json` diferem só em `totalDuration` e no `createdDateTime` do seed, valores de runtime. |
| Gate | `powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1` (2026-10-12 18:34–18:41) | todos os passos com exit 0, detalhados abaixo |

Passos do gate:

| Passo | Resultado |
|---|---|
| 01 restore | exit 0 |
| 02 build WebApi | 0 avisos, 0 erros |
| 03 Architecture | 30/30 |
| 04 Unit | 107/107 |
| 05 Integration | 141/141 |
| 06 PostgreSQL | 5/5 |
| 06b RabbitMQ | 19/19 |
| 06c Elasticsearch | 13/13 |
| 06f Redis | 23/23 |
| 07 Functional | 191/191 |
| **07b coverage** | 107/141/191 descobertos = executados, adaptador 3.1.5, `Coverage report: OK` |
| 08 vulnerabilidades | 17 projetos sem pacote vulnerável |
| 09 `docker compose config -q` | exit 0 |
| 10 OpenAPI | v1/v2 idênticos ao baseline |
| 11 Graphify | 4 008 nós, 8 529 arestas, 236 comunidades |
| 12 `git diff --check` | exit 0 |

Uma primeira execução do gate foi interrompida de propósito no passo 04, depois da descoberta do adaptador obsoleto (seção 7). Ela não conta como resultado. Os passos de ensaio opcionais (`-ComposeBaseline`, `-RedisRehearsal`, etc.) não foram executados: a fase não toca Compose nem serviços.

Graphify: regenerado pelo gate (`graphify-out/` é git-ignorado). Não era necessário, porque a fase só alterou ferramental e scripts, sem mudança estrutural de código-fonte.

## 11. Gates indisponíveis, não verificados ou não executados

- **Test Explorer do Visual Studio:** não verificado, nem com 3.1.5 nem com 4.0.1. Por isso o runner foi mantido.
- **CI externo:** não existe no repositório. A cobertura é consumida pelo consumidor local verificado e pelo gate.
- **VSIX:** fora de escopo (Fase 20), não construído.
- **Ensaios de Compose e serviços (Fases 14–18):** não reexecutados, pois nada nesses caminhos mudou.

## 12. Pontos de rollback (independentes)

1. **Microsoft.NET.Test.Sdk:** nada a reverter (18.10.1 inalterado).
2. **Moq:** `Directory.Packages.props` → `Version="4.20.72"`. Depois, restore + build e conferir com `test-tooling-evidence.ps1 -CompareWith before`.
3. **coverlet:** `Directory.Packages.props` → `Version="6.0.4"`. O alinhamento de `PrivateAssets` em `Tests/Template.Functional.Tests/Template.Functional.Tests.csproj` pode ficar. Rodar o rebuild `--no-incremental`, porque downgrade de pacote copiado com `PreserveNewest` pode deixar DLLs novas no `bin`. A política continua válida para linhas e fontes; os mínimos de branch foram medidos com 10.1.0 e permaneceriam atendidos (os valores do 6.0.4 são maiores ou iguais).
4. **Runner xUnit:** sem mudança. Para testar 4.x, mudar a versão central e fazer rebuild; para voltar, fazer o mesmo.
5. **Limites e consumo:** remover o passo `07b-coverage` de `run-phase-validation.ps1`. `Tests/test-coverage-policy.json` e os scripts `coverage-report.ps1`, `test-tooling-evidence.ps1` e `package-graph-snapshot.ps1` são aditivos e podem ser removidos sem efeito em código.

Os rótulos de evidência `before`, `after-moq`, `after-coverlet`, `runner4-experiment` e `final` ficam em `artifacts/validation/test-tooling/` e `artifacts/validation/package-graph/` como estado anterior reproduzível.

## 13. Riscos residuais e limitações

- A cobertura vem só das suítes determinísticas: os adaptadores reais de Redis, RabbitMQ e Elasticsearch não entram nos números. O Messaging (59,5% de linha) depende de caminhos de broker real exercitados apenas pela suíte opt-in.
- A folga de 1 ponto foi definida por julgamento; a determinística observada foi de 4 rodadas em uma máquina. Se o passo `07b` oscilar em outro ambiente, revisar com evidência e não rebaixar silenciosamente.
- O passo `07b` repete a execução de Unit, Integration e Functional (cerca de 1,5 min a mais no gate).
- A consolidação de branches por linha usa o melhor par `coberto/total`. É uma aproximação: o Cobertura não identifica qual condição foi coberta em cada relatório.
- O runner 4.x pode virar requisito futuro (pacote do VSTest com descontinuação anunciada). A migração recomendada continua sendo uma fase própria, avaliando também o xUnit v3 e o Microsoft Testing Platform.

## 14. Confirmações

- Nenhum código de produção, pacote de produção, rota, contrato OpenAPI, comportamento de saúde, log, cache, mensageria, persistência ou busca mudou. O grafo de produção resolvido é idêntico. Nenhuma instrumentação de cobertura entrou no startup da aplicação.
- Nenhum dado de serviço, volume Docker, fila, índice, banco ou sistema externo foi alterado. Só containers descartáveis do Testcontainers foram usados.
- xUnit, SDK .NET, target frameworks, Testcontainers, imagens e VSIX não foram alterados. Nenhuma migração EF foi criada.
- **Fase 20 não iniciada**, e nenhum trabalho da Fase 21 foi feito.

## 15. Arquivos alterados

- `Directory.Packages.props`: Moq 4.21.0, coverlet.collector 10.1.0.
- `Tests/Template.Functional.Tests/Template.Functional.Tests.csproj`: `PrivateAssets`/`IncludeAssets` do coletor.
- `Tests/test-coverage-policy.json` (novo): política de limites por módulo.
- `scripts/test-tooling-evidence.ps1` (novo): descoberta e execução por projeto, cobertura opcional, comparação.
- `scripts/coverage-report.ps1` (novo): consumidor, validador e aplicador de limites.
- `scripts/package-graph-snapshot.ps1` (novo): grafos de produção e teste.
- `scripts/run-phase-validation.ps1`: passo `07b-coverage`.
- `AGENTS.md`: comandos e estado verificados.
- `docs/refactoring/phase-19-report.md`: este relatório.
