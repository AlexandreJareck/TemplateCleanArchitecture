# Relatório da Fase 22 — Remoção e rotação da chave JWT commitada

Especificação: `docs/refactoring/handoff-phase-22.md` (executada via `docs/refactoring/execute-phase-22.md`). Branch `refactor/fase-22-jwt-key-rotation`, a partir de `main` em `e490767` (Fase 21 mergeada no PR #18; handoff no PR #19).

## 1. Decisão de release

**Ready.** Todos os gates obrigatórios da matriz da Fase 21 foram reexecutados contra o workspace final em 2026-10-17 e passaram. Isso inclui G-SEC-SECRETS, que era o único bloqueador da Fase 21. Nenhum arquivo commitado carrega `JwtSettings:Key` nem outra credencial. Todo perfil recebe a chave de uma fonte não commitada, e a ausência dela derruba a inicialização nomeando só a chave. A chave antiga não é usada em lugar nenhum.

Ações que continuam com o dono, fora do repositório e sem bloquear o release:

- gerar a chave nova nos stores reais (§6), porque o dono preferiu não autorizar a escrita no `.env` e no user-secrets dele;
- a reescrita do histórico autorizada em D6 (§11), que é uma etapa separada.

## 2. Pré-condições e ponto de partida

- Fase 21 mergeada em `main` (PR #18, 2026-10-15 03:35 UTC). `git status --short` limpo no início, HEAD `e490767`. Não houve mudanças concorrentes. O worktree antigo `.claude/worktrees/fase-8-redis-adapter-f4fda8` não foi tocado.
- Ponto de partida verde, executado num worktree isolado `C:\p22s` no HEAD `e490767`, antes de qualquer edição:
  - restore exit 0;
  - build 0 avisos e 0 erros;
  - arquitetura 30/30;
  - funcionais 191/191;
  - `compose-baseline.ps1` 98/98 (`p14-baseline-p22start20261015010641`).
- As decisões de 2026-10-10 e as da Fase 21 continuam valendo: severidade 2B, licenças 3A, VSIX 4A, chave JWT como bloqueador.

## 3. Registro de decisões (dono, 2026-10-15)

| # | Decisão | Escolha |
|---|---|---|
| D1 | Chave do perfil Test | **(b)** nenhum valor commitado. `CustomWebApplicationFactory` gera uma chave aleatória por host (`RandomNumberGenerator`, 48 bytes), só em memória. |
| D2 | Fonte em Development | **(a)** `dotnet user-secrets` (o `UserSecretsId` já existia), com comando de configuração única documentado. Variável de ambiente continua sobrepondo. |
| D3 | Fonte no Compose | **Confirmado:** `JwtSettings__Key` vem do `.env` git-ignored, com placeholder `CHANGE_ME` no `.env.example`. |
| D4 | Transição de tokens | **Invalidar:** tokens assinados com a chave antiga passam a dar 401. Sem janela dual-key e sem código novo. |
| D5 | Outras credenciais commitadas | **(b) remover nesta fase.** Saíram as connection strings locais (PostgreSQL e Redis) e, pelo mesmo critério, `RabbitMqSettings:Password`. |
| D6 | Histórico do Git | **O dono autorizou reescrever.** Isso contradiz o "Do not rewrite Git history" de `execute-phase-22.md`. Por ser destrutivo e externo (force-push em `main`), fica como etapa separada, depois do merge, com confirmação final do plano concreto (§11). Nada foi reescrito nesta fase. |
| — | Stores reais do dono | O dono **não** autorizou escrever no `.env` nem no user-secrets reais. Os comandos estão em `docs/configuration-secrets.md`. |
| — | Redes Docker | Autorizada a remoção das redes vazias anteriores à Fase 21 (§13). |

## 4. Valores removidos (só nomes de chave)

| Arquivo | Removido |
|---|---|
| `Src/Presentation/Template.WebApi/appsettings.json` | `JwtSettings:Key`; `ConnectionStrings` (`DefaultConnection`, `IdentityConnection`, `RedisConnection`); `RabbitMqSettings:Password` |
| `appsettings.Development.json` | idem |
| `appsettings.Production.json` | idem |
| `appsettings.Test.json` | `JwtSettings:Key`; `RabbitMqSettings:Password` (o perfil Test não tinha connection strings) |

`Issuer`, `Audience`, `DurationInMinutes`, `RabbitMqSettings:UserName`/`HostName`/`Port` e todo o resto ficaram iguais. Claims, lifetime, algoritmo, issuer/audience, refresh, security stamp e o contrato 401/403 não mudaram.

## 5. Novas fontes de configuração

| Perfil | Fonte |
|---|---|
| Development | `dotnet user-secrets` (`JwtSettings:Key` e, contra serviços locais, as connection strings e `RabbitMqSettings:Password`) |
| Compose | `.env`: `docker-compose.yml` passa `JwtSettings__Key=${JwtSettings__Key}` à WebApi. As connection strings e a senha do RabbitMQ já vinham de `POSTGRES_*`, `REDIS_PASSWORD` e `RABBITMQ_DEFAULT_*`. |
| Production | variáveis de ambiente ou secret store da implantação (Production nunca lê user secrets) |
| Test | chave e senha sintética do broker aleatórias por host, em memória (`CustomWebApplicationFactory`) |
| Scripts | `compose-baseline.ps1`, `postgres16-patch-rehearsal.ps1`, `rabbitmq4-upgrade-rehearsal.ps1`, `elastic-upgrade-rehearsal.ps1` (8.x e 9.x), `redis-insight-upgrade-rehearsal.ps1`, `redis8-migration-rehearsal.ps1` e `capture-api-baseline.ps1` geram chave por execução com `RandomNumberGenerator` (48 bytes). Ela fica no ambiente do processo ou no env file da execução em `artifacts/validation` (git-ignored) e nunca é impressa. |

`run-phase-validation.ps1` passo `09` agora usa `docker compose --env-file .env.example config -q`, então nunca lê o `.env` real do usuário. O passo novo `13-secret-scan` roda o scan.

## 6. Procedimento de rotação

Detalhes em `docs/configuration-secrets.md`.

1. Gerar uma chave nova com gerador criptográfico, de pelo menos 32 bytes (os comandos documentados usam 48).
2. Gravar no store do perfil: `dotnet user-secrets set "JwtSettings:Key" ...` em Development, `JwtSettings__Key` no `.env` no Compose, secret store em Production.
3. Reiniciar a WebApi. Tokens antigos passam a dar 401 e os clientes autenticam de novo.
4. Nunca reutilizar a chave antiga.

O comando de Development foi verificado sem tocar no store real. Com `APPDATA` apontado para uma pasta temporária isolada:

- o `dotnet user-secrets set ... --project Src\Presentation\Template.WebApi` documentado gravou a chave (exit 0);
- a WebApi subiu em Development com essa chave;
- `authenticate` retornou 200 com token, a chamada autorizada 404 (id inexistente, sem escrita) e a anônima 401;
- o hash do `secrets.json` real ficou inalterado (`artifacts/validation/phase-22/user-secrets/summary.txt`).

## 7. Testes e verificação de segredo adicionados

- **`Tests/Template.Architecture.Tests/CommittedSecretsTests.cs`** (17 testes).
  - Falha quando qualquer `appsettings*.json` ou `launchSettings.json` em `Src`/`Tests` tem valor não vazio sob propriedade terminada em `Key`, `Password`, `Pwd`, `Secret` ou `Token`, ou connection string com senha.
  - Falha quando um `docker-compose*.yml` ou o `.env.example` atribui a `JwtSettings__Key` algo diferente de `${JwtSettings__Key}` ou `CHANGE_ME`.
  - Mutações sintéticas: 6 formas proibidas de JSON detectadas, 4 formas limpas aceitas, 5 linhas de Compose. Nenhum achado ecoa o valor.
- **`scripts/secret-scan.ps1`**: aplica a mesma regra estrutural. Também procura o valor comprometido, lido em memória de `622e808`, em todos os arquivos rastreados, nos não rastreados e não ignorados, e nas evidências (`-EvidencePath`). Imprime só caminhos e nomes de chave.
- **Funcionais** (`IdentityOptionsStartupTests`):
  - `SigningKeyAbsentFromEverySource_FailsStartup_NamingTheKeyOnly`;
  - `TestSigningKey_IsGeneratedPerHost_AndNeverCommitted`, que compara as chaves de dois hosts sem ecoar os valores.
- **Compose baseline**: 5 checks novos, em todos os perfis:
  - `jwt.key-from-environment`;
  - `jwt.no-committed-key`;
  - `integration.jwt-authorized-call` (404);
  - `integration.jwt-anonymous-call-401`;
  - `secrets.jwt-key-not-in-evidence`.
- **Ensaios PostgreSQL e RabbitMQ**: 1 check novo cada, `no-jwt-key-in-evidence`.

### Prova por mutação controlada (`artifacts/validation/phase22-mutation/summary.txt`)

Valores sintéticos foram injetados nos arquivos reais. Em cada caso o teste de arquitetura e o scan falharam, e os bytes originais foram restaurados com SHA-256 verificado.

| Mutação | Arquitetura | Scan | Valor ecoado | Restaurado |
|---|---|---|---|---|
| `JwtSettings:Key` em `appsettings.Production.json` | falhou (`appsettings.Production.json: JwtSettings:Key`) | falhou | não | sim |
| connection string com senha em `appsettings.json` | falhou (`ConnectionStrings:DefaultConnection`) | falhou | não | sim |
| literal em `docker-compose.yml` | falhou (`docker-compose.yml:26: JwtSettings__Key`) | falhou | não | sim |
| valor comprometido num arquivo-sonda de evidência | — | falhou (`compromised-value: …/probe.txt`) | não | sonda removida |

### Verificação negativa (`artifacts/validation/phase22-negative/summary.txt`)

A WebApi compilada foi iniciada em Production, com banco em memória e broker desligado:

- **sem chave em nenhuma fonte:** o processo encerrou com `OptionsValidationException: JwtSettings:Key is required.` e nunca registrou "Application started";
- **com chave curta sintética:** encerrou com `JwtSettings:Key must be at least 32 bytes for HMAC-SHA256.`, sem ecoar o valor.

Com banco relacional, as migrations rodam antes do `ValidateOnStart` (comportamento já existente). A falha acontece do mesmo jeito antes de qualquer requisição ser atendida.

## 8. Arquivos alterados

- Configuração: `Src/Presentation/Template.WebApi/appsettings{,.Development,.Production,.Test}.json`, `docker-compose.yml`, `.env.example`.
- Testes:
  - `Tests/Template.Architecture.Tests/CommittedSecretsTests.cs` (novo);
  - `Tests/Template.Functional.Tests/Common/CustomWebApplicationFactory.cs`;
  - `Tests/Template.Functional.Tests/Security/IdentityOptionsStartupTests.cs`.
- Scripts:
  - `scripts/secret-scan.ps1` (novo);
  - `scripts/run-phase-validation.ps1`, `compose-baseline.ps1`, `capture-api-baseline.ps1`;
  - os ensaios `postgres16-patch-rehearsal.ps1`, `rabbitmq4-upgrade-rehearsal.ps1`, `elastic-upgrade-rehearsal.ps1`, `redis-insight-upgrade-rehearsal.ps1`, `redis8-migration-rehearsal.ps1`.
- Documentação: `docs/configuration-secrets.md` (novo), `AGENTS.md`, este relatório.
- Código de produção: nenhum arquivo `.cs` de `Src` mudou. `JwtSettingsValidator` com `ValidateOnStart` já cobria o fail-fast.

## 9. Matriz de validação: execuções

- **1ª rodada** (2026-10-15 01:36): os passos `00`–`06b` passaram. O Docker Desktop reiniciou sozinho durante o `06c`, e o processo terminou sem resumo (`phase-22/gate-run1-interrupted-*`). Depois do reinício, 34 containers de ensaios anteriores com `restart: always` voltaram a subir. Foram só parados de novo, sem remoção (`phase-22/docker-restart-stop.log`).
- **2ª rodada, final** (2026-10-17 11:26–12:03): `run-phase-validation.ps1 -ComposeBaseline -RedisRehearsal -PostgresRehearsal -RabbitMqRehearsal -RedisInsightRehearsal -ElasticRehearsalWebApi8xPath C:\p22pub\webapi8x -ElasticRehearsalWebApi9xPath C:\p22pub\webapi9x`. Todos os passos deram exit 0, exceto `13-secret-scan`.
  - O `13` falhou porque o script tentava ler o próprio log, travado pelo `Tee-Object`.
  - Correção: leitura com `FileShare.ReadWrite`, e arquivo ilegível passou a ser achado em vez de exceção.
  - O passo foi reexecutado isoladamente: OK, 3.934 arquivos, incluindo o próprio log em escrita.
- As WebApi publicadas do ensaio Elastic saíram do zero:
  - 8.x de `git archive 31b1225` (como na Fase 21);
  - 9.x do workspace final;
  - ambas em `C:\p22pub`, recebendo a chave por variável de ambiente.

## 10. Tabela de gates

Ambiente:

- Windows 11, Docker Desktop (Engine 28.4.0);
- SDK `C:\Users\alexa\.dotnet\dotnet.exe` 10.0.402;
- logs em `artifacts/validation/` (git-ignored).

| ID | Gate | Obrig. | Comando / procedimento | Data | Resultado | Status |
|---|---|---|---|---|---|---|
| G-SDK | SDK fixado e restore | Sim | passo `01` | 2026-10-17 | exit 0 | Passed |
| G-BUILD | Build WebApi | Sim | passo `02` | 2026-10-17 | 0 avisos, 0 erros | Passed |
| G-ARCH | Arquitetura + regra de segredo commitado | Sim | passo `03` + mutação §7 | 2026-10-17 | 47/47; mutações detectadas | Passed |
| G-UNIT | Unitários | Sim | passo `04` | 2026-10-17 | 107/107 | Passed |
| G-INT | Integração geral | Sim | passo `05` | 2026-10-17 | 142/142 | Passed |
| G-PG | PostgreSQL 16.15 real | Sim | passo `06` | 2026-10-17 | 6/6 | Passed |
| G-RMQ | RabbitMQ 4.3.6 real | Sim | passo `06b` | 2026-10-17 | 19/19 | Passed |
| G-ES | Elasticsearch 9.5.5 real | Sim | passo `06c` | 2026-10-17 | 13/13 | Passed |
| G-REDIS | Redis 8.10.2 real | Sim | passo `06f` | 2026-10-17 | 23/23 | Passed |
| G-FUNC | Funcionais (JWT, Identity, startup, chave ausente) | Sim | passo `07` | 2026-10-17 | 193/193 | Passed |
| G-DISC | Descoberta = execução = aprovação | Sim | `test-tooling-evidence.ps1 -Label phase22-discovery` | 2026-10-17 | 47/107/142/6/19/13/23/193, adapter 3.1.5 | Passed |
| G-COV | Cobertura e limites | Sim | passo `07b` | 2026-10-17 | "Coverage report: OK" (sem mudança de limites) | Passed |
| G-COMPOSE | Compose limpo, full-stack, autenticação | Sim | passo `06e` | 2026-10-17 | 103/103 (`p14-baseline-20261017113518`) | Passed |
| G-R15 | Redis 8 ensaio + rollback | Sim | passos `06g`/`06h` | 2026-10-17 | 60/60 (`p15-*-20261017113814`), 103/103 (`p15-rollback-20261017114057`) | Passed |
| G-R16 | PostgreSQL backup/restore + rollback | Sim | passos `06i`/`06j` | 2026-10-17 | 69/69 (`20261017114344`), 103/103 (`p16-rollback-20261017114532`) | Passed |
| G-R17 | RabbitMQ ensaio, suíte 4.2.2, rollback | Sim | passos `06k`/`06l`/`06m` | 2026-10-17 | 82/82 (`20261017114811`, rollback 7,3 s), 19/19, 102/102 (`p17-rollback-20261017115204`) | Passed |
| G-R11 | Elastic/Kibana ensaio + rollback | Sim | passo `06d` | 2026-10-17 | 49/49 (`20261017113100`) | Passed |
| G-R18 | Redis Insight ensaio + rollback | Sim | passos `06n`/`06o` | 2026-10-17 | 67/67 (`p18-20261017115444`), 100/100 (`p18-rollback-20261017115539`) | Passed |
| G-NEG | WebApi sem chave falha nomeando só a chave | Sim | §7 verificação negativa | 2026-10-15 | `JwtSettings:Key is required.`; valor curto não ecoado | Passed |
| G-DEV | Fluxo D2 (user-secrets) | Sim | §6, `APPDATA` isolado | 2026-10-17 | token 200, autorizado 404, anônimo 401; store real inalterado | Passed |
| G-VSIX | VSIX (separado) | Separado | — | — | sem mudança no escopo do VSIX (decisão 1) | Skipped |
| G-VULN | Vulnerabilidades (política 2B) | Sim | passo `08` | 2026-10-17 | 0 achados | Passed |
| G-DEPR | Depreciados | Sim | `dotnet list … --deprecated --include-transitive` | 2026-10-17 | só a família xunit 2.9.3 (teste, adiado) | Passed |
| G-OUTD | Desatualizados | Sim | `dotnet list … --outdated` | 2026-10-17 | novos candidatos desde a Fase 21: FluentValidation.DependencyInjectionExtensions 12.2.0, MediatR 14.2.0, Microsoft.NET.Test.Sdk 18.11.0, Containers.Tools.Targets 1.23.0; xunit.runner.visualstudio 4.0.1 já adiado. Classificados como adiados (fase sem upgrades) | Passed |
| G-LIC | Licenças (política 3A) | Sim | `license-inventory.ps1 -Label phase22-final` | 2026-10-17 | 0 fora da política | Passed |
| G-PKG | Grafo de pacotes | Sim | `package-graph-snapshot.ps1 -Label phase22 -CompareWith phase21-after-patch` | 2026-10-17 | 0 diferenças (produção e teste) | Passed |
| G-IMG | Imagens/digests | Sim | `compose-baseline` (checks de imagem) + passo `09` | 2026-10-17 | nenhuma imagem mudou; todas fixadas por digest | Passed |
| G-CCFG | `docker compose config -q` com placeholders | Sim | passo `09` (`--env-file .env.example`) | 2026-10-17 | exit 0; `.env` real não lido | Passed |
| G-OPENAPI | OpenAPI v1/v2 exato | Sim | passo `10` + comparação com `baseline-phase-6` | 2026-10-17 | v1 e v2 idênticos (normalizando CRLF); v1 "This API version has been deprecated." | Passed |
| G-GRAPH | Graphify | Sim | passo `11` | 2026-10-17 | 4.131 nós, 8.708 arestas, 241 comunidades; direção coberta por G-ARCH | Passed |
| G-SEC-SECRETS | Segredos | Sim | passo `13` (`secret-scan.ps1 -EvidencePath artifacts\validation`) + mutação + varredura do diff | 2026-10-17 | OK; chave comprometida ausente de 3.934 arquivos | Passed |
| G-DOCS | Comandos documentados verificados | Sim | `AGENTS.md`, `docs/configuration-secrets.md` | 2026-10-17 | só comandos executados nesta fase; só nomes de chave | Passed |
| G-DIFF | `git diff --check` | Sim | passo `12` e reexecução antes do commit | 2026-10-17 | exit 0 (só avisos LF/CRLF) | Passed |

## 11. Riscos residuais e próximas ações

- **A chave antiga continua no histórico do Git** (commits até `e490767`, inclusive nos PRs mergeados). A rotação a torna inútil.
  - D6 autoriza a reescrita, mas ela não foi feita.
  - Plano proposto para uma etapa separada, depois do merge desta fase:
    1. instalar `git-filter-repo` (não está instalado);
    2. `--replace-text` só da chave, lida de arquivo temporário sem impressão;
    3. force-push de `main` e das branches;
    4. reclonar ou realinhar os worktrees.
  - O GitHub mantém refs de PR (`refs/pull/*`) e caches. A remoção completa exige pedido ao GitHub Support.
  - Exige confirmação final do dono.
- **Tokens emitidos antes da rotação ficam inválidos** (D4): os clientes precisam autenticar de novo.
- **Stores reais ainda sem a chave nova.** Até o dono rodar os comandos de `docs/configuration-secrets.md`, `dotnet run` local e `docker compose up` normal falham no startup com `JwtSettings:Key is required.`. O fail-fast é intencional.
- `dotnet run` local agora também exige as connection strings e `RabbitMqSettings:Password` no user-secrets (D5 b).
- **Cópias locais da chave antiga fora do Git:**
  - `C:\p22pub\webapi8x` e `C:\p22pub\src8` (build 8.x de `31b1225`; o ensaio sobrepõe a chave por variável de ambiente);
  - pastas `bin/obj` e publicações antigas das fases anteriores.
  - Recomenda-se apagar `C:\p22pub` depois da revisão.
- `docker compose up` normal com `docker-compose.override.yml` (HTTPS) continua não verificado, como na Fase 21.
- Dados reais do usuário não foram lidos nem migrados.
- O Docker Desktop reiniciou sozinho durante a 1ª rodada (§9). Com isso, containers de ensaio com `restart: always` voltam a subir a cada reinício do Docker.
- Os desatualizados de G-OUTD ficam para uma fase de upgrade autorizada.

## 12. Rollback

- Estado anterior: `e490767` (merge da Fase 21 mais o handoff).
- Para reverter, restaure num único lote os arquivos de configuração, testes e scripts da §8, sem comandos Git destrutivos.
- **Nunca restaure a chave antiga.** Se for preciso voltar, forneça uma chave recém-gerada pelas fontes não commitadas (§5). A suíte funcional e o `compose-baseline.ps1` provam o estado restaurado.

## 13. Recursos retidos e limpeza que exige autorização

- **Redes removidas com autorização:** 10 redes vazias anteriores à Fase 21 (`p11-net-20261011*`, `p16-net-20261012*`, `p17-net-20261012*`, `p18-probe-nw`; 0 endpoints cada), registradas em `phase-22/network-removal.log`. Restam 26 redes.
- **Retidos da Fase 22**, todos parados, nenhum removido, todos com dados sintéticos:
  - containers, volumes e redes de `p14-baseline-p22start20261015010641`, `p14-baseline-20261017113518`;
  - `p15-src/tgt-20261017113814`, `p15-rollback-20261017114057`;
  - `p16-*-20261017114344`, `p16-rollback-20261017114532`;
  - `p17-*-20261017114811`, `p17-rollback-20261017115204`;
  - `p18-20261017115444`, `p18-rollback-20261017115539`;
  - `p11-*-20261017113100`.
- **Fora do repositório:**
  - worktree `C:\p22s` (HEAD `e490767`, ponto de partida);
  - `C:\p22pub` (fontes 8.x e publicações 8.x/9.x);
  - `%TEMP%\p22-appdata-20261017121129` (user-secrets isolado com chave sintética).
- Os env files por execução em `artifacts/validation/**` contêm credenciais e chaves sintéticas (git-ignored).
- Toda limpeza só com autorização do dono.

## 14. Confirmações

- A API v1 não foi removida. Rotas, status codes e OpenAPI não mudaram (G-OPENAPI).
- Nenhum valor de chave, antigo ou novo, aparece em código, documentação, relatórios, commits, texto do PR, saída de teste ou chat. Os checks `secrets.jwt-key-not-in-evidence` e `no-jwt-key-in-evidence` e o passo `13` verificaram as evidências.
- Nenhum dado de usuário foi lido ou apagado. O `.env` real e o user-secrets real não foram lidos nem modificados (hash do `secrets.json` inalterado).
- Nenhum pacote ou imagem foi atualizado. Nenhuma funcionalidade foi adicionada. Nada da Fase 23 foi iniciado.
