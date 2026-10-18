# Relatório da Fase 24 — Identidade, autorização e fundações da API

Especificação: `docs/refactoring/handoff-phase-24.md` (executada via `docs/refactoring/execute-phase-24.md`) em 2026-10-18. Branch `refactor/fase-24-identity-api-foundations`, a partir de `main` em `ec187c9`.

## 1. Decisão de release

**Ready.** Todos os gates obrigatórios passaram contra o workspace final em 2026-10-18 (§10). A única falha da primeira execução do gate (`06e-compose-baseline`, 4 checagens de OpenAPI contra o baseline da Fase 6) era a mudança aditiva intencional; o novo baseline de contrato `docs/refactoring/baseline-phase-24` foi registrado (§7) e o `compose-baseline.ps1` reexecutado isoladamente com 103/103.

A WebApi passa a suportar uma SPA real com segurança: sessão de navegador com access token curto em memória e refresh token rotativo em cookie `HttpOnly`/`Secure`/`SameSite=Strict` (D23-11), detecção de reuso, CSRF double-submit vinculado à família por HMAC, verificação de `Origin`, logout, revoke-all, `GET /api/v2/account/me`, autorização por permissão (SEC-008), CORS explícito e validado, trilha de auditoria append-only e as convenções HTTP aprovadas (Problem Details com `code`, paginação, `If-Match`/`ETag`, `Idempotency-Key`, `202 Accepted`). Nenhuma rota, corpo ou status existente de v1/v2 mudou.

## 2. Pré-condições e ponto de partida

- Fases 22 e 23 concluídas e mergeadas (`5da4660`, `b283f30`, `ec187c9`); decisões D23-1..18 e B23-1..11 registradas em `docs/product/decision-log.md`. A decisão de transporte de sessão (D23-11) estava aprovada; nenhuma parada foi necessária.
- `git status --short` limpo no início; HEAD `ec187c9`; sem trabalho concorrente.
- Baseline antes de qualquer edição (`artifacts/validation/phase24-baseline/`): build 0 erros; arquitetura 47/47; unit 107/107; integração 142/142; funcional 193/193; OpenAPI v1/v2 idênticos a `docs/refactoring/baseline-phase-6` (só fim de linha).
- Comportamentos capturados e preservados: `authenticate`/`start` (v1 `BaseResult`, v2 `BaseResult` via `ApiResultMapper`), 401 com `WWW-Authenticate: Bearer` e Problem Details, 403 Problem Details, rotação do security stamp a cada sign-in legado, revogação por troca de senha.

## 3. Decisões

As decisões do dono (D23-*) e do blueprint (B23-*) foram seguidas. As escolhas de implementação estão em `docs/product/decision-log.md` (§ Phase 24 implementation decisions):

| ID | Resumo |
|---|---|
| D24-1 | Login v2 não rotaciona o security stamp; o legado continua rotacionando (e com isso revoga famílias v2 no próximo refresh). |
| D24-2 | Famílias de sessão, hashes de refresh, auditoria e idempotência ficam no banco da aplicação (atomicidade com a auditoria). |
| D24-3 | Chave HMAC do CSRF derivada por HKDF-SHA256 de `JwtSettings:Key`; nenhum segredo novo. |
| D24-4 | Access token de sessão carrega `sid`; revogação vale imediatamente para tokens dessa família. |
| D24-5 | Login responde 200 (troca de credencial). |
| D24-6 | Rate limit por filtro (`System.Threading.RateLimiting`, janela fixa) só nos endpoints de autenticação; partições com hash. |
| D24-7 | `code` só nos endpoints da Fase 24+; legado byte-compatível; `ErrorCode` 6–11 acrescentados. |
| D24-8 | SP-1 fechado: coluna explícita `Version` + índice único guardião traduzido para `ConcurrencyConflictException`. |
| D24-9 | Usuário seed padrão não recebe papel (senha de exemplo commitada). |
| D24-10 | Idempotência/ETag/202 como primitivas exercitadas por fixture de teste; primeiros endpoints reais na Fase 25. |

## 4. Endpoints novos (v2, aditivos)

| Método e rota | Autorização | Sucesso | Falhas (`code`) |
|---|---|---|---|
| `POST /api/v2/auth/sessions` | anônimo + rate limit (IP + usuário) + `Origin` se presente | 200 `{accessToken, tokenType, accessTokenExpiresAt, sessionExpiresAt}` + cookies | 400 `validation`, 401 `auth.invalid-credentials`, 403 `auth.csrf-invalid`, 429 `auth.rate-limited` |
| `POST /api/v2/auth/sessions/refresh` | cookie + CSRF + `Origin` obrigatório + rate limit (IP) | 200 com tokens rotacionados | 401 `auth.session-expired` / `auth.session-revoked`, 403 `auth.csrf-invalid`, 409 `auth.refresh-in-progress`, 429 |
| `DELETE /api/v2/auth/sessions/current` | cookie + CSRF + `Origin` (quando há cookie) | 204, cookies limpos; repetível | 403 `auth.csrf-invalid` |
| `DELETE /api/v2/auth/sessions` | Bearer | 204, todas as famílias do usuário revogadas | 401 `auth.session-expired` |
| `GET /api/v2/account/me` | Bearer | 200 `{userId, userName, displayName, roles, permissions}` | 401 `auth.session-expired` |
| `GET /api/v2/audit-records` | `audit.read` | 200 `{items, page, pageSize, totalItems, totalPages}` | 400 `validation`, 401, 403 `auth.forbidden` |

Cookies (iguais em todos os perfis): `__Secure-ph_refresh` (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/v2/auth/sessions`, sem `Domain`, expira no limite absoluto da família) e `XSRF-TOKEN` (legível pela SPA, `Secure`, `SameSite=Strict`, `Path=/`, mesma expiração), ecoado em `X-XSRF-TOKEN`. Respostas desses endpoints levam `Cache-Control: no-store`, `X-Content-Type-Options: nosniff` e `Referrer-Policy: no-referrer`. O OpenAPI descreve a permissão exigida e os `code` possíveis de cada operação nova.

## 5. Modelo de ameaças (STRIDE, sessão)

| Ameaça | Vetor | Controle | Prova |
|---|---|---|---|
| Spoofing | Força bruta de senha | Rate limit 10/min por IP + usuário (SEC-007); resposta idêntica para usuário inexistente e senha errada, com verificação de hash "isca" | `EleventhLoginForTheSameUserName_Is429...`, `Login_UnknownUserAndWrongPassword_AreIndistinguishable401` |
| Spoofing | Roubo do refresh token | Cookie `HttpOnly`/`Secure`/`SameSite=Strict`, path restrito; rotação a cada uso; reuso após a janela revoga a família inteira (SEC-003) | `ReplayingARotatedToken_RevokesTheWholeFamily`, `ReuseAfterGrace_RevokesFamily...` (PostgreSQL) |
| Tampering | CSRF em refresh/logout | Double-submit (`XSRF-TOKEN` = `X-XSRF-TOKEN`) + HMAC ligado à família + `Origin` permitido (SEC-005) | `Refresh_WithInvalidCsrf_Is403...` (4 variantes), `Refresh_FromForeignOrMissingOrigin_Is403` |
| Tampering | Login CSRF | `Origin`, quando presente, precisa ser permitido; `SameSite=Strict` | `Login_FromForeignOrigin_Is403...` |
| Repudiation | Negação de ações de sessão | `AuditRecord` append-only no mesmo commit (AUD-001/002): criação, refresh, reuso, revogação, CSRF, rate limit e 403 de permissão (amostrados) | `Login_StoresOnlyTheHash_AndAuditsInTheSameCommit`, `FailedAuditInsert_RollsBackTheTransition`, `SecurityEvents_AreAudited...` |
| Information disclosure | Vazamento de token/hash em banco, log, auditoria ou resposta | Só SHA-256 do refresh é gravado; stamp só como fingerprint; detalhes de auditoria por allow-list (AUD-003); logs só com IDs opacos; `/me` só com campos aprovados (SEC-009); corpo do login sem refresh/CSRF | `Login_StoresOnlyTheHash...`, `AuditRecordTests`, `Me_ReturnsOnlyApprovedFields...`, `Login_ReturnsOnlyTheAccessToken...` |
| Denial of service | Inundação de login/refresh e de auditoria | Rate limit; auditoria de eventos de borda amostrada (1 por partição/minuto, mapa limitado); famílias limitadas a 5 por usuário (SEC-010) | `EleventhRefresh..._AndIsAuditedOnce`, `SixthLogin_RevokesTheOldestFamily` |
| Elevation of privilege | Papel amplo contornando regras | Autorização só por permissão (SEC-008); Administrator sem permissão de decisão (SOD-6); papel legado `Admin` não concede permissão; regra de arquitetura proíbe `Roles =`/`RequireRole` em controllers | `PermissionMatrixFunctionalTests` (10 papéis), `Administrator_HoldsNoProcurementDecisionPermission`, `Controllers_Authorize_By_Permission_Never_By_Role` |
| Elevation of privilege | Token de família revogada ainda válido | Claim `sid` validada a cada requisição (D24-4); stamp alterado revoga famílias (SEC-006) | `Logout_RevokesFamily...KillsTheAccessTokenImmediately`, `RevokeAll...`, `PasswordChange_RevokesEverySession`, `LegacyAuthenticate_KeepsRotatingTheStamp...` |
| Race | Dois refresh simultâneos (duas abas) | Versão como token de concorrência + índice único `(FamilyId, Sequence)`; o perdedor recebe 409 determinístico sem revogar (SEC-004) | `ParallelRefresh_OfTheSameToken_HasExactlyOneWinner...`, `ConcurrentRotation_SecondCommit_IsAConcurrencyConflict...` (PostgreSQL, 3 execuções) |

## 6. Esquema e migração

Migração aditiva `20261018085240_Phase24AccessAuditIdempotency` (banco da aplicação; Identity sem mudança de esquema — papéis e permissões são dados em `Role`/`RoleClaims`), gerada com `dotnet-ef` 10.0.13 instalado só no scratchpad da sessão:

| Tabela | Colunas-chave | Índices |
|---|---|---|
| `AccessSessionFamilies` | `Id`, `UserId`, `SecurityStampFingerprint char(64)`, `CreatedAtUtc`, `LastRefreshedAtUtc`, `IdleExpiresAtUtc`, `AbsoluteExpiresAtUtc`, `RevokedAtUtc`, `RevocationReason varchar(40)`, `Version bigint` (token de concorrência) | `(UserId, RevokedAtUtc)` |
| `AccessRefreshTokens` | `Id`, `FamilyId` (FK cascade), `TokenHash char(64)`, `Sequence`, `IssuedAtUtc`, `ReplacedAtUtc` | único `TokenHash`; único `(FamilyId, Sequence)` |
| `AuditRecords` | `Id`, `OccurredAtUtc`, `ActorId`, `Action`, `ResourceType`, `ResourceId`, `FromState`, `ToState`, `RuleIds text[]`, `WaiverId`, `Reason`, `CorrelationId`, `CausationId`, `Details jsonb` | `OccurredAtUtc`, `(ResourceType, ResourceId)`, `ActorId` |
| `ApiIdempotencyRecords` | `Id`, `UserId`, `Key varchar(64)`, `Operation`, `RequestHash char(64)`, `StatusCode`, `ContentType`, `ResponseBody`, `Location`, `CreatedAtUtc`, `ExpiresAtUtc`, `CompletedAtUtc` | único `(UserId, Key)`; `ExpiresAtUtc` |

Revisão PostgreSQL: só `CREATE TABLE`/`CREATE INDEX` em tabelas novas; nenhuma tabela existente é alterada, sem backfill nem reescrita; o `Down` remove as quatro tabelas. Nenhuma migração aplicada foi reescrita; designer e snapshot foram gerados pela ferramenta. Aplicação verificada em PostgreSQL 16.15 real (`Migration_CreatesAccessAuditAndIdempotencyTables_WithExpectedTypes`, 0 pendentes) e no Compose isolado (`compose-baseline.ps1`, histórico de migrações igual aos arquivos, nenhuma migração na segunda inicialização) e no rehearsal PostgreSQL 16 (backup/restore com as tabelas novas, 69/69).

Seeds de Identity: os dez papéis de `docs/product/permissions.md` §3 são criados com suas permissões (claims `permission`) apenas quando o papel não existe, para nunca sobrescrever edições futuras. O papel legado `Admin` e o usuário padrão não mudam.

## 7. Compatibilidade da API (diff OpenAPI exato)

Comparação de `artifacts/validation/phase24-baseline/openapi` com `artifacts/validation/phase24-after/openapi`:

| Documento | Diferença | Classificação |
|---|---|---|
| v1 | schema `ErrorCode`: enum `[0..5]` → `[0..11]` | Aditiva (valores acrescentados, nenhum renumerado; nenhuma operação v1 retorna os novos valores) |
| v1 | caminhos, operações, demais schemas | Idênticos |
| v2 | +5 caminhos: `/api/v2/auth/sessions` (POST, DELETE), `/api/v2/auth/sessions/refresh`, `/api/v2/auth/sessions/current`, `/api/v2/account/me`, `/api/v2/audit-records` | Aditiva |
| v2 | +5 schemas: `AuditRecordView`, `AuditRecordViewPagedResult`, `CreateSessionRequest`, `CurrentUserDto`, `SessionTokenResponse`; +3 tags (`AuditRecords`, `AuthSessions`, `CurrentAccount`); `ErrorCode` como em v1 | Aditiva |
| v2 | operações existentes (`account/*`, `product/*`) | Idênticas |

Fora do OpenAPI: a política CORS "qualquer origem" foi substituída por origens explícitas (exigência do handoff). Clientes de navegador cross-origin não configurados deixam de receber cabeçalhos CORS; requisições não-browser e same-origin não mudam. Respostas legadas (corpos 400/401/403/404/500) não ganharam `code` (D24-7). As amostras `product-v*-get.json` e `health.json` diferem só no timestamp do seed e na duração medida.

## 8. Configuração (só nomes de chave)

Nenhum segredo novo. Chaves novas, não secretas, com valores da política v1 em `appsettings*.json`: `SessionSettings:AbsoluteLifetime`, `SessionSettings:IdleTimeout`, `SessionSettings:ReuseGraceWindow`, `SessionSettings:MaxActiveFamilies`, `SessionSettings:RateLimitPermitLimit`, `SessionSettings:RateLimitWindow`, `Cors:AllowedOrigins` (Development `https://localhost:4200`, Test `https://spa.template.test`, demais vazio), `Cors:AllowCredentials` (`false`), `Cors:RequireAllowedOrigins` (`false`; `true` faz a inicialização falhar com lista vazia). Ambas as seções são validadas na inicialização (`ValidateOnStart`), com mensagens que só nomeiam chaves. Documentado em `docs/configuration-secrets.md`.

## 9. Testes adicionados

| Projeto | Antes → depois | Novos testes |
|---|---|---|
| Arquitetura | 47 → 53 | autorização só por permissão (SEC-008-a), toda ação dos controllers novos declara permissão/`[Authorize]`/`[AllowAnonymous]` (NFR-SEC-3), porta de auditoria append-only (AUD-002-a), criptografia de sessão só em Infrastructure.Identity, CORS sem "any origin", mutações controladas de cada regra |
| Unit | 107 → 188 | `SessionFamily` (rotação, idle/absoluto, graça, reuso, stamp, revogação, versão), `AuditRecord` (allow-list, sem mutadores), primitivas (paginação, precondição de versão, `Result`, validação de `SessionSettings`), catálogo de permissões e matriz de papéis (SOD-5/6/7), handlers de login/refresh/logout/revoke-all/`me`/evento de borda com fakes |
| Integração (InMemory) | 142 → 146 | repositório de sessões, leitor de auditoria (filtro, ordenação, desempate por `id`, páginas), store de idempotência (início, replay, reuso, escopo por usuário, abandono, expiração 24 h) |
| PostgreSQL 16.15 | 6 → 15 | migração e tipos, só hash gravado + auditoria no mesmo commit, refresh paralelo com um vencedor e sem revogação, reuso após graça, versão obsoleta sem escrita, rotação concorrente → conflito, falha de auditoria desfaz a transição, sexto login revoga a família mais antiga, idempotência concorrente com um dono |
| Funcional | 193 → 288 | atributos dos cookies, corpo do login, 401 indistinguível, validação, `Origin`, `/me` (forma exata, papéis e permissões efetivas, token legado), rotação, 409 na graça, CSRF (4 variantes), `Origin` estrangeiro/ausente, logout imediato, revoke-all, troca de senha, interação com `authenticate` legado, auditoria sem segredos, matriz de permissões dos 10 papéis, 403 auditado e amostrado, paginação/validação da auditoria, reuso com graça zero, expiração idle/absoluta com relógio móvel, 429 com `Retry-After` sem afetar outros endpoints, CORS (preflight, origens estrangeiras, sem curinga), validadores, `SessionTokenService`, `If-Match`, idempotência (replay, 422, 400, escopo), 412/428 com `ETag`, 202 com `Location`/`Retry-After` |

Os testes nunca escrevem tokens, cookies ou senhas em mensagens de asserção (comparações booleanas onde o valor poderia ecoar).

## 10. Tabela de gates

Execução sequencial de `run-phase-validation.ps1 -ComposeBaseline -PostgresRehearsal` (2026-10-18 06:20–06:39, America/Sao_Paulo), logs em `artifacts/validation/` (git-ignored). Switches de Redis, RabbitMQ, Redis Insight e Elastic rehearsal não foram usados: nenhuma dessas integrações mudou (as suítes reais de RabbitMQ, Elasticsearch e Redis rodam sempre no gate).

| Gate | Resultado |
|---|---|
| `01-restore` / `02-build-webapi` | exit 0; build da solução 0 avisos, 0 erros |
| `03-test-architecture` | 53/53 |
| `04-test-unit` | 188/188 |
| `05-test-integration` | 146/146 |
| `06-test-postgresql` | 15/15 (e 15/15 em três execuções manuais seguidas, para a corrida de refresh) |
| `06b-test-rabbitmq` | 19/19 |
| `06c-test-elasticsearch` | 13/13 |
| `06f-test-redis` | 23/23 |
| `06e-compose-baseline` | 1ª execução 99/103 (só `openapi.v1/v2.*` contra o baseline da Fase 6); após o baseline da Fase 24, reexecução isolada **103/103** (`p14-baseline-20261018064007`) |
| `06i-postgres16-patch-rehearsal` | 69/69 (`20261018062929`; backup 2.2 s, restore 2.1 s, volta 3.6 s) |
| `06j-compose-postgres-rollback` | 103/103 (`p16-rollback-20261018063141`, já com o baseline da Fase 24) |
| `07-test-functional` | 288/288 |
| `07b-coverage` | descobertos = executados = aprovados (188/146/288, adapter 3.1.5); política de cobertura OK, sem rebaixar nenhum mínimo |
| `08-vulnerable-packages` | nenhum pacote vulnerável |
| `09-docker-compose-config` | exit 0 (placeholders de `.env.example`) |
| `10-capture-openapi` | exit 0; igual a `docs/refactoring/baseline-phase-24` |
| `11-graphify` | 5,659 nós, 11,974 arestas, 321 comunidades (`graphify-out/` git-ignored) |
| `12-git-diff-check` | exit 0 |
| `13-secret-scan` | OK |

## 11. Auditorias

| Auditoria | Resultado |
|---|---|
| `package-graph-snapshot.ps1 -Label phase24 -CompareWith phase22` | 246 produção / 469 teste; 0 diferenças de produção e 0 de teste (nenhum pacote novo: rate limiting, HKDF e HMAC vêm do framework) |
| `license-inventory.ps1 -Label phase24` | 0 pacotes fora da política (exceções aceitas inalteradas: MediatR, VS Container Tools Targets) |
| `dotnet list ... --vulnerable --include-transitive` | nenhum |
| `dotnet list ... --deprecated --include-transitive` | só a família xunit 2.9.3 (adiada, só teste) |
| `dotnet-ef` | 10.0.13 instalado com `--tool-path` no scratchpad da sessão para gerar a migração; nada adicionado ao repositório |

## 12. Rollback

- Código: reverter o merge da Fase 24. Os endpoints novos desaparecem; legado intacto.
- Banco da aplicação: as quatro tabelas são novas e independentes. Para voltar o esquema, aplicar o `Down` da migração (`dotnet ef database update 20261010151729_ProductPriceDecimal`) **somente com autorização**, porque descarta sessões e registros de auditoria. Sem rollback de esquema, o código anterior ignora as tabelas (não há dependência inversa).
- Identity: os papéis semeados são dados; o código anterior os ignora. Removê-los exige autorização (dados de política).
- Configuração: as seções `SessionSettings` e `Cors` são ignoradas pelo código anterior; o CORS anterior volta a "qualquer origem" junto com o código.

## 13. Riscos residuais

- `title`/`detail` dos Problem Details novos seguem em inglês, como o padrão existente; a localização pt-BR fica para a fase da SPA.
- Lockout do Identity continua desligado (comportamento legado); a proteção contra força bruta é o rate limit por processo. Com mais de uma instância da WebApi, o limite é por instância (sem store distribuído).
- Dois logins concorrentes do mesmo usuário podem deixar 6 famílias ativas momentaneamente; a próxima criação corrige (SEC-010 é aproximado sob concorrência).
- A revogação por troca de senha cruza dois bancos: se o commit da revogação explícita falhar depois da troca, o binding do stamp ainda revoga cada família no próximo refresh e o access token já falha.
- A rotação de `JwtSettings:Key` invalida também os vínculos CSRF: o refresh devolve 403 `auth.csrf-invalid` em vez de 401 até o novo login.
- Limpeza de famílias expiradas e de registros de idempotência vencidos ainda não existe (retenção definida em NFR; job previsto para o Worker).
- `Origin` é exigido em refresh/logout: clientes não-browser precisam enviá-lo.
- O `docker compose up` normal com `docker-compose.override.yml` (HTTPS) e o uso real pela SPA (cookies `Secure` no navegador) não foram exercitados; a SPA é da Fase 26.

## 14. Arquivos alterados

| Área | Arquivos |
|---|---|
| Domain | `Common/VersionedAggregateRoot.cs`; `Access/` (`SessionFamily`, `RefreshTokenRecord`, `SessionPolicy`, `SessionRefreshOutcome`, `SessionRevocationReason`); `Auditing/AuditRecord.cs` |
| Application | `Common/` (results, `ProblemCodes`, paging, concurrency, operations); `Security/` (`Permissions`, `SeededRoles`, `SecurityAuditActions`); `Interfaces/Access`, `Interfaces/Auditing`, `Interfaces/Idempotency`, `IRequestContext`; `Features/Access` (login, refresh, logout, revoke-all, evento de borda, `me`), `Features/Auditing`; `Services/Access`; `Settings/SessionSettings.cs`; `ErrorCategory`/`ErrorCode` (acréscimos), `IUnitOfWork.DiscardChanges`, `LogEvents` 9xxx, registro |
| Persistence | configurações de sessão e auditoria, `Idempotency/`, `SessionFamilyRepository`, `AuditRecordRepository`, `UnitOfWork` (tradução de conflito), registro, migração `20261018085240_Phase24AccessAuditIdempotency` (+ designer e snapshot gerados) |
| Identity | `Sessions/` (`SessionTokenService`, `IdentityAccountDirectory`, `JwtAccessTokenIssuer`), `Seeds/PermissionRoles.cs`, validação de `sid` em `SecurityStampTokenValidator`, revogação na troca de senha em `AccountServices`, registro |
| WebApi | `Controllers/v2/AuthSessionsController.cs`, `CurrentAccountController.cs`, `AuditRecordsController.cs`; `Infrastructure/Security/*`; `Infrastructure/Http` (`ApiResultMapper`, handler 401/403, idempotência, `HttpConventions`); `ApiConventionsOperationFilter`; `HttpRequestContext`; `ErrorHandlerMiddleware`; `Program.cs`; `appsettings*.json` (`Cors`, `SessionSettings`); removido `Infrastructure/Extensions/CorsExtensions.cs` |
| Testes | arquitetura (`ArchitectureRulesTests`), unit (`DomainTests/Access`, `DomainTests/Auditing`, `ApplicationTests/Common`, `ApplicationTests/Security`, `ApplicationTests/Features/Access`, `ErrorCategoryTests`), integração (`AccessPersistenceAdapterTests`), PostgreSQL (`AccessAuditPostgreSqlTests`), funcional (`Security/Session*`, `PermissionMatrixFunctionalTests`, `CorsAndSessionComponentTests`, `ApiEndpoints/v2/ApiConventionsFunctionalTests`, fixture com limite relaxado, `IdentityComponentTests` com escopo assíncrono) |
| Scripts e docs | `scripts/compose-baseline.ps1` (baseline de contrato da Fase 24); `docs/refactoring/baseline-phase-24/` (OpenAPI v1/v2); `docs/configuration-secrets.md`; `docs/product/decision-log.md` (D24-1..10, SP-1 fechado); `AGENTS.md`; este relatório |

## 15. Confirmações

Nenhum token, cookie, hash, senha ou valor de segredo foi escrito em arquivo commitado, log ou evidência; a ferramenta `dotnet-ef` ficou no scratchpad da sessão. Nenhuma migração aplicada foi reescrita. Nenhum dado ou recurso foi apagado; os recursos Docker criados pelos rehearsals ficam retidos conforme a política do repositório. Branch, commit, push e PR seguem a autorização permanente do dono para trabalho de fase.
