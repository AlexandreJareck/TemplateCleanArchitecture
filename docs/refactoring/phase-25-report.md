# Relatório da Fase 25 — Dados mestres de organização e compras

Especificação: `docs/refactoring/handoff-phase-25.md` (executada via `docs/refactoring/execute-phase-25.md`) em 2026-10-18. Branch `refactor/fase-25-master-data`, a partir de `main` em `d414aba`.

## 1. Decisão de release

**Ready.** Todos os gates obrigatórios passaram contra o workspace final em 2026-10-18 (§10). A primeira execução do gate (11:23–11:42) falhou só em `07b-coverage` (Domain 92,0% < 99%; Application 89,1%/70,2% < 94%/88%): os testes novos cobriam as regras, mas não todos os caminhos de rejeição. Em vez de rebaixar limites, foram acrescentados testes de borda de domínio e de caminhos dos handlers, o código gerado por `GeneratedRegex` foi substituído por validação explícita e duas checagens inalcançáveis foram removidas; a reexecução completa (11:53–12:10) passou em todos os passos e o ratchet de cobertura foi elevado (§11).

Entregue: departamentos, centros de custo e configurações da organização (ORG-001..006), fornecedores com contatos, endereços, categorias, ciclo de vida `Draft → Active ↔ Suspended → Inactive → Active` e histórico (SUP-001..006), categorias, unidades de medida e itens de catálogo (CAT-001..004), consulta de usuários para atribuição de gestor/responsável (ORG-006), enriquecimento opcional por BrasilAPI (CNPJ e CEP) e o contrato PTAX do Banco Central (SP-5) atrás de portas da Application, com servidor fake, resiliência limitada, logs seguros, snapshot aceito e entrada manual sempre disponível (INT-001/002). Nenhuma rota, corpo ou status existente de v1/v2 mudou; o delta OpenAPI é só aditivo.

## 2. Pré-condições e ponto de partida

- Fases 23 e 24 concluídas e mergeadas (`b283f30`, `ec187c9`, `d414aba`); decisões D23-1..18, B23-1..11 e D24-1..10 em `docs/product/decision-log.md`. Organização única por instalação (D23-1, ADR-0006) confirmada; nomes, identificadores, campos obrigatórios e permissões seguem o blueprint (`domain-model.md`, `business-rules.md`, `permissions.md`); retenção: dados mestres nunca são apagados (ORG-005).
- `git status --short` limpo no início; HEAD `d414aba`; sem trabalho concorrente.
- Docker Desktop estava parado e foi iniciado localmente para as suítes que exigem contêineres.
- Baseline antes de qualquer edição (`artifacts/validation/phase25-baseline/`): build 0 erros; arquitetura 53/53; unit 188/188; integração 146/146; funcional 288/288; OpenAPI v1/v2 idênticos a `docs/refactoring/baseline-phase-24` (só fim de linha).
- Provedores externos: nenhuma chamada a BrasilAPI ou BCB em produção; só páginas de documentação foram consultadas (§6).

## 3. Decisões

As decisões D23-*/B23-*/D24-* foram seguidas. As escolhas de implementação estão em `docs/product/decision-log.md` (§ Phase 25 implementation decisions):

| ID | Resumo |
|---|---|
| D25-1 | Métodos de comportamento retornam `DomainError` (código, tipo, regra, campo) em vez de exceção para rejeições esperadas. |
| D25-2 | Códigos normalizados em maiúsculas (unicidade case-insensitive pelo índice comum); formatos por tipo. |
| D25-3 | SUP-001 aceita o **CNPJ alfanumérico** (IN RFB 2.229/2024, desde julho de 2026), com DV numérico por módulo 11 (ASCII − 48). |
| D25-4 | Contatos, endereços e categorias substituídos como coleção (`PUT`) sob o mesmo token de versão; fornecedor não-rascunho nunca perde os dados de SUP-002. |
| D25-5 | País e CNPJ editáveis só em `Draft`. |
| D25-6 | Motivos com 10–500 caracteres. |
| D25-7 | Desativação de dados mestres exige motivo (ORG-003 estendido ao catálogo); `reactivate` cobre `Suspended/Inactive → Active`. |
| D25-8 | `AllowedDecimals` da unidade fixo após a criação. |
| D25-9 | Adaptadores no novo projeto `Template.Infrastructure.Integrations`, desabilitados em todos os perfis versionados; resiliência própria (timeout 5 s, ≤ 2 retries transitórios, sem retry em 429, circuit breaker); único pacote novo `Microsoft.Extensions.Http` 10.0.13; log padrão do HttpClient removido. |
| D25-10 | Consulta devolve proposta consultiva; aceite traz provedor (allow-list) e `retrievedAt` (≤ 24 h, não futuro) e passa pelas invariantes; sem cache de proposta. |
| D25-11 | Dados de contato do provedor (e-mails, telefones, sócios) nunca são mapeados. |
| D25-12 | SP-5: uma requisição PTAX para `[data − 7, data]`, último boletim `Fechamento`, venda ≤ 6 casas; sem boletim → `NotFound`. Sem endpoint HTTP nesta fase. |
| D25-13 | ORG-004 via porta `IOrganizationSettingsLock`; adaptador da Fase 25 responde "sem dotações" até a Fase 27. |
| D25-14 | `GET /api/v2/users` para ORG-006; administração de usuários/papéis adiada. |
| D25-15 | Ações de auditoria por transição; chaves de detalhe novas só com códigos, IDs e nomes de campo; evento de integração na Fase 28 (Outbox). |
| D25-16 | Unit of work traduz só os índices únicos de chave de negócio em `UniqueConstraintViolationException`. |
| D25-17 | Enums de compras documentados como string no OpenAPI (filtro restrito por namespace); `ErrorCategory.Unavailable` (503) acrescentado; `ErrorCode` intacto. |
| D25-18 | Tabelas com prefixo de módulo em PascalCase (convenção da Fase 24); Domain de um módulo referencia outro só por ID + `MasterDataReference`. |

## 4. Modelo de domínio e regras

| Agregado | Tabela(s) | Invariantes e transições |
|---|---|---|
| `Department` | `OrganizationDepartments` | código único/imutável `^[A-Z0-9-]{2,20}$` (ORG-001); desativação com motivo e só sem centro de custo ativo (ORG-003); inativo rejeita novo responsável |
| `CostCenter` | `OrganizationCostCenters` | código único/imutável; departamento fixo na criação (ORG-002, FK `Restrict`); criação/reativação só em departamento ativo; inativo rejeita novo gestor e uso (`EnsureUsable`) |
| `OrganizationSettings` | `OrganizationSettings` | singleton semeado (BRL, `America/Sao_Paulo`, BRL/EUR/USD); moeda base e fuso travados após a primeira dotação (ORG-004); fuso IANA validado |
| `Supplier` (+ `SupplierContacts`, `SupplierAddresses`, `SupplierCategories`, `SupplierStatusChanges`, snapshot de enriquecimento em colunas `Enrichment*`) | `Suppliers` e filhas | CNPJ normalizado e validado estruturalmente, único por país (SUP-001); ativação exige razão social, CNPJ, contato com e-mail, endereço e categoria (SUP-002); só `Active` é elegível (SUP-003, `EnsureEligibleForNewCommitment`); transições não alteram outros dados (SUP-004); motivo em suspender/inativar/reativar (SUP-005); histórico com sequência única por fornecedor |
| `Category` | `CatalogCategories` | código único/imutável (CAT-001); desativação com motivo; novas referências exigem ativa (CAT-002) |
| `UnitOfMeasure` | `CatalogUnitsOfMeasure` | código único/imutável; `AllowedDecimals` 0–3 (CHECK no banco) limita quantidades (CAT-003) |
| `CatalogItem` | `CatalogItems` | código único/imutável; categoria e unidade ativas em referências novas; reativação exige ambas ativas (CAT-002); preço de referência opcional, consultivo, > 0, ≤ 4 casas (CAT-004) |

Nenhum dado mestre referenciado pode ser apagado: não existe endpoint `DELETE` e as FKs são `ON DELETE RESTRICT` (ORG-005). Toda mudança grava `AuditRecord` no mesmo `SaveChanges` (AUD-001), sem CNPJ, nomes, contatos ou endereços nos detalhes (AUD-003).

### Matriz regra → teste

| Regra | Testes |
|---|---|
| ORG-001 | U `OrganizationTests.T_ORG_001_a_*` (normalização, formatos, imutabilidade); H `T_ORG_001_c_DuplicateCode_*`, `T_ORG_001_b_UniqueIndexRace_*`; P `T_ORG_001_b_CodesAreUniqueCaseInsensitively_EvenUnderConcurrentCreation`; F `T_ORG_001_c_DepartmentLifecycle_*`, `T_ORG_001_c_InvalidCodes_*`, `T_ORG_001_c_DuplicateCode_*` |
| ORG-002 | U `T_ORG_002_a_CostCenterBelongsToItsDepartmentForever`; P FK `Restrict` em `Migration_CreatesMasterDataTables_*` |
| ORG-003 | U `T_ORG_003_a_*` (4); H `T_ORG_003_b_*` (2), `DeactivateWithoutReason_IsReasonRequired400`; F `T_ORG_003_CostCenterRules_OverHttp` |
| ORG-004 | U `T_ORG_004_a_*`, `Settings_Validation`; H `T_ORG_004_a_Settings_LockAfterTheFirstAllocation`, `Settings_RejectUnknownTimeZones_AndAcceptIana`; F `OrganizationSettings_ReadAndReplace_WithConcurrency` |
| ORG-005 | P `T_ORG_005_a_ReferencedMasterDataCannotBeHardDeleted` (violação 23503); F `T_ORG_005_NoHardDeleteEndpointExists` (6 rotas, 405); A `MasterData_Controllers_Expose_No_Hard_Delete` |
| ORG-006 | H `T_ORG_006_a_HeadAndManager_MustBeActiveUsers`; F `T_ORG_006_a_ManagerMustBeAnActiveUser_AndTheLookupExposesSafeFieldsOnly` |
| SUP-001 | U `T_SUP_001_a_*` (CNPJ numérico e alfanumérico, inválidos, estrangeiros, criação); H `T_SUP_001_*` (3), `UpdateTaxId_OfDraft_ChecksUniqueness`; P `T_SUP_001_b_TaxIdIsUniquePerCountry_ThroughTheHandler`, `UnitOfWork_TranslatesUniqueViolations_WithTheIndexName`; F `T_SUP_001_DuplicateAndInvalidTaxIds_AndNormalization` |
| SUP-002 | U `T_SUP_002_a_*` (cada elemento faltante, contato sem e-mail, ativação), `T_SUP_002_ActiveSupplier_CannotLoseItsRequiredData`; H `T_SUP_002_*` (2); F `T_SUP_002_DraftToActive_*` |
| SUP-003 | U `T_SUP_003_a_OnlyActiveSuppliersAreEligible` (4 estados); F `isEligibleForNewCommitments` em `T_SUP_002_DraftToActive_*`. Os consumidores (SRC-003/016, PUR-003) chegam nas Fases 29/32 |
| SUP-004 | U `T_SUP_004_StatusChangesNeverTouchOtherData`; P `T_SUP_004_StatusChange_AndItsAudit_CommitTogether_WithHistoryRow`. Invalidação de aprovações (APR-012) é da Fase 31 |
| SUP-005 | U `T_SUP_005_*`, `Lifecycle_FollowsTheWorkflowTable`, `DraftCannotBeSuspendedOrInactivated`; H `T_SUP_005_*`; F `reason-required` em `T_SUP_002_DraftToActive_*` |
| SUP-006 | F `T_SUP_006_a_*`, `MasterDataPermissionMatrixTests` (10 papéis × 17 operações), `StatusChange_NeedsTheDedicatedPermission_*` |
| CAT-001 | U `T_CAT_001_*`; H `T_CAT_001_DuplicateCodes_AreRejected_PerKind`; P índices únicos em `Migration_CreatesMasterDataTables_*` |
| CAT-002 | U `T_CAT_002_a_*` (3); H `T_CAT_002_b_*`, `T_CAT_002_DeactivateAndReactivateItem_WithVersioning`; F `T_CAT_CatalogItemFlow_*` |
| CAT-003 | U `T_CAT_003_a_*` (10 casos); H `Units_AllowedDecimalsAreValidated`; P CHECK em `Migration_CreatesMasterDataTables_*` |
| CAT-004 | U `T_CAT_004_ReferencePrice_*`; H `CreateItem_ParsesMoneyStrings_AndAudits`, `CreateItem_RejectsNonDecimalAmounts`; P decimal exato `numeric(18,4)` em `Filters_TranslateToSql_*`; F dinheiro como string |
| INT-001 | I `EnrichmentAdapterTests` (sucesso, 404, 400, 429 sem retry, 500/503/408 com 3 tentativas, falha transitória seguida de sucesso, timeout, corpos malformados/divergentes, host inacessível, cancelamento, circuito aberto/meio-aberto, logs, PTAX, registro desabilitado/habilitado, validação de configuração); H `T_INT_001_*` (3); F `T_INT_001_DisabledOrFailingProvider_Is503_AndNeverBlocksManualEntry` |
| INT-002 | U `T_INT_002_EnrichmentSnapshot_RecordsProvenance`; H `T_INT_002_a_*`, `T_INT_002_StaleOrFutureProposals_AreRejected`; F `T_INT_002_a_ProposalIsAdvisory_AndTheAcceptedSnapshotIsPersisted` |
| SP-5 / CALC-004 (contrato) | H `SP5_Ptax_*` (3); I `Ptax_*` (contrato OData, esquema, janela vazia) |
| AUD-001/003 | H auditoria em cada comando (ações, estados, motivo, regras, correlação, sem PII); P mudança + auditoria no mesmo commit, versão obsoleta sem escrita; F trilha do fornecedor via `audit-records` sem e-mail/endereço |
| SP-1 | H `ConcurrentWriter_Wins_*`, `Update_RequiresTheCurrentVersion`; P `SP1_SupplierChildChange_RacesAgainstAStatusChange_AndExactlyOneWins`, `StatusChange_WithStaleVersion_Is412_*`; F 412/428 com `version` |
| ADR-0001/0007 | A `Domain_Modules_Reference_Each_Other_Only_Through_The_Shared_Kernel`, `External_Provider_Code_Lives_In_Infrastructure_Integrations`, `Infrastructure_Integrations_Implements_Application_Owned_Ports_Without_Leaking_Transport`, `Provider_Clients_Never_Log_Request_Urls`, com mutações controladas em `Phase25_Controlled_Mutations_Are_Detected` |

U = domínio sem mocks; H = handler com fakes das portas; I = integração (InMemory/servidor fake); P = PostgreSQL 16.15 real; F = HTTP funcional; A = arquitetura.

## 5. Endpoints novos (v2, aditivos)

Todos com `[StableProblemCodes]`, `[NoStoreSecurityHeaders]`, permissão explícita, paginação `PagedResult` (`page`, `pageSize` 1–100, `sort` com allow-list e desempate por `id`), `ETag` forte em toda representação versionada, `If-Match` obrigatório em `PUT` e transições (428/412), `Idempotency-Key` obrigatório em criações e transições, cancelamento propagado.

| Rota | Métodos | Permissão | Falhas (`code`) |
|---|---|---|---|
| `/api/v2/departments`, `/{id}`, `/{id}/deactivate`, `/{id}/reactivate` | GET, POST, GET, PUT, POST, POST | `organization.read` / `organization.manage` | `validation` (ORG-001/006), `organization.code-duplicate`, `organization.inactive`, `organization.department-has-active-cost-centers`, `reason-required`, `invalid-transition`, `not-found`, concorrência, idempotência |
| `/api/v2/cost-centers` (mesma forma; filtros `departmentId`, `managerUserId`) | idem | idem | idem |
| `/api/v2/organization-settings` | GET, PUT | `organization.read` / `organization.settings.manage` | `validation`, `organization.settings-locked`, concorrência |
| `/api/v2/suppliers` (filtros `q`, `status` múltiplo, `country`, `taxId`, `categoryId`), `/{id}` | GET, POST, GET, PUT | `suppliers.read` / `suppliers.manage` | `supplier.tax-id-invalid`, `supplier.tax-id-duplicate`, `invalid-transition`, concorrência, idempotência |
| `/api/v2/suppliers/{id}/contacts`, `/addresses`, `/categories` | PUT | `suppliers.manage` | `validation`, `not-found`, `catalog.inactive`, `supplier.activation-incomplete`, concorrência |
| `/api/v2/suppliers/{id}/enrichment-acceptances` | POST | `suppliers.manage` | `validation`, concorrência, idempotência |
| `/api/v2/suppliers/{id}/activate`, `/suspend`, `/inactivate`, `/reactivate` | POST | `suppliers.status.change` | `supplier.activation-incomplete`, `reason-required`, `invalid-transition` (`currentState`), concorrência, idempotência |
| `/api/v2/categories`, `/api/v2/units-of-measure` (mesma forma de departamentos) | GET, POST, GET, PUT, POST, POST | `catalog.read` / `catalog.manage` | `validation`, `catalog.code-duplicate`, `reason-required`, `invalid-transition` |
| `/api/v2/catalog-items` (filtros `kind`, `categoryId`, `unitOfMeasureId`) | idem | idem | + `catalog.inactive` |
| `/api/v2/enrichment/company-registrations/{taxId}` | GET | `suppliers.manage` | `supplier.tax-id-invalid`, `not-found`, `integration.provider-rejected` (422), `integration.provider-unavailable` (503) |
| `/api/v2/enrichment/postal-addresses/{postalCode}` | GET | `suppliers.manage` | `validation`, `not-found`, `integration.provider-rejected`, `integration.provider-unavailable` |
| `/api/v2/users` | GET | `access.users.manage` | `validation` |

Representações: dinheiro `{ "amount": "1234.5600", "currency": "BRL" }` (string), enums como string, instantes UTC, `version` igual ao `ETag`. A lista de fornecedores é um resumo sem contatos/endereços; o detalhe traz contatos, endereços, categorias, histórico de status, proveniência do enriquecimento e `missingForActivation`.

## 6. Integrações externas

### Pesquisa (2026-10-18, só documentação; nenhuma chamada às APIs)

| Provedor | Fonte consultada | O que foi confirmado | Limitação |
|---|---|---|---|
| BrasilAPI | README do repositório oficial `BrasilAPI/BrasilAPI` (branch `main`) e o handler `pages/api/cnpj/v1/[cnpj].js` | projeto em beta; Termos de Uso "ainda em elaboração"; sem limite numérico publicado; pede volume semelhante ao de uma pessoa e proíbe varredura automatizada; o handler de CNPJ v1 repassa 400/404 do serviço de origem e marca indisponibilidade com `Cache-Control: no-store` | a documentação OpenAPI (`/docs`) é uma SPA que não renderiza para leitura automatizada; os nomes de campo de `cnpj/v1` e `cep/v2` seguem o contrato público conhecido do projeto e ficam fixados pelos testes com servidor fake |
| Banco Central (PTAX) | PDF oficial "Taxas de Câmbio – todos os boletins diários" (portal de dados abertos do BCB) e a descrição do conjunto de dados | recurso `CotacaoMoedaPeriodo(moeda, dataInicial, dataFinalCotacao)` e `CotacaoMoedaDia`, datas `'MM-DD-AAAA'`, `$format=json`; o período devolve paridades e cotações de compra/venda; o boletim de fechamento é a média dos boletins do dia | a página `olinda.bcb.gov.br/.../documentacao` é um template que não renderizou; os nomes `cotacaoCompra`, `cotacaoVenda`, `dataHoraCotacao`, `tipoBoletim` seguem a descrição do serviço e ficam fixados por testes de contrato; a classificação de fechamento aceita `Fechamento` e `Fechamento PTAX` |

Um smoke opcional contra os provedores reais não foi executado (sem autorização); quando autorizado, basta habilitar a seção correspondente em um perfil local, sem commit.

### Adaptadores

- Portas na Application (`ISupplierRegistryLookup`, `IPostalCodeLookup`, `IExchangeRateProvider`) com DTOs neutros e resultados explícitos `Found`, `NotFound`, `Unavailable`, `Rejected`, `Invalid` e motivo seguro (`timeout`, `rate-limited`, `server-error`, `unreachable`, `circuit-open`, `schema-mismatch`, `disabled`).
- `ProviderHttpExecutor`: GET idempotente, timeout por tentativa de 5 s, até 2 retries só para timeout/rede/408/5xx com espera linear, 429 sem retry, circuit breaker por provedor (5 falhas consecutivas, 30 s, um teste meio-aberto), cancelamento do chamador propagado e neutro para o circuito. Logs: provedor, operação, classificação, status e tentativas; nunca URL, CNPJ, CEP ou conteúdo.
- Validação/normalização antes de apresentar: CNPJ devolvido igual ao consultado, nomes ≤ 200 caracteres sem espaços repetidos, CEP com 8 dígitos igual ao consultado, UF com 2 letras, taxas positivas com ≤ 6 casas, datas dentro da janela pedida; qualquer divergência → `Invalid` (503 na borda).
- Entrada manual nunca depende do provedor: com o provedor desabilitado, lento ou com erro, `POST /suppliers` e todo o ciclo de vida continuam funcionando (teste funcional).

## 7. Esquema e migração

Migração aditiva `20261018135425_Phase25MasterData` (banco da aplicação; Identity sem mudança), gerada com `dotnet-ef` 10.0.13 instalado só no scratchpad. Revisão do SQL: apenas `CREATE TABLE`, `CREATE INDEX` e uma `CHECK` em tabelas novas; nenhuma tabela existente é alterada; sem backfill. FKs para dados mestres referenciados são `RESTRICT`; filhas do fornecedor são `CASCADE` (só removidas junto com o agregado, o que a aplicação nunca faz). Índices únicos: `IX_OrganizationDepartments_Code`, `IX_OrganizationCostCenters_Code`, `IX_CatalogCategories_Code`, `IX_CatalogUnitsOfMeasure_Code`, `IX_CatalogItems_Code`, `IX_Suppliers_Country_TaxId`, `IX_SupplierStatusChanges_SupplierId_Sequence`; índices de filtro em status, ativo, departamento, gestor, categoria e unidade. Designer e snapshot gerados pela ferramenta; nenhuma migração aplicada foi reescrita. O singleton `OrganizationSettings` é semeado por `InitializeApplicationDatabaseAsync` quando ausente.

Evidências: `Migration_CreatesMasterDataTables_*` (tipos, índices, FKs `RESTRICT`, CHECK) e `Migration_RollsBackToPhase24_AndReappliesIdempotently` (Down até a Fase 24 remove só as tabelas novas e mantém `AuditRecords`, `AccessSessionFamilies` e `Products`; Up duas vezes sem pendências; seed idempotente) em PostgreSQL 16.15 real; Compose isolado e rehearsal PostgreSQL 16 no gate (§10).

## 8. Compatibilidade da API (diff OpenAPI exato)

Comparação de `docs/refactoring/baseline-phase-24` com a captura da Fase 25 (`docs/refactoring/baseline-phase-25`, novo baseline do `compose-baseline.ps1`):

| Documento | Diferença | Classificação |
|---|---|---|
| v1 | nenhuma (byte a byte após normalizar fim de linha) | — |
| v2 | +34 caminhos (§5), +43 schemas (views, requests, `PagedResult` de cada view, `CatalogItemKind` e `SupplierAddressType` como enums string), +9 tags | Aditiva |
| v2 | caminhos, operações e schemas existentes (incluindo `ErrorCode`) | Idênticos |

Fora do OpenAPI: `ErrorCategory` ganhou `Unavailable` (não serializado). Respostas legadas não mudaram.

## 9. Configuração (só nomes de chave)

Nenhum segredo novo. Seções novas, não secretas, em `appsettings.json`: `BrasilApiSettings` e `BcbPtaxSettings` (`Enabled` = `false`, `BaseUrl`, `Timeout`, `MaxRetries`, `RetryDelay`, `CircuitBreakerFailureThreshold`, `CircuitBreakerBreakDuration`), validadas na inicialização quando habilitadas, com mensagens que só nomeiam a seção. Documentado em `docs/configuration-secrets.md`.

## 10. Tabela de gates

Execução sequencial de `run-phase-validation.ps1 -ComposeBaseline -PostgresRehearsal` (2026-10-18 11:53–12:10, America/Sao_Paulo), logs em `artifacts/validation/` (git-ignored). Switches de Redis, RabbitMQ, Redis Insight e Elastic rehearsal não foram usados: nenhuma dessas integrações mudou (as suítes reais de RabbitMQ, Elasticsearch e Redis rodam sempre no gate).

| Gate | Resultado |
|---|---|
| `00-git-status` / `01-restore` / `02-build-webapi` | exit 0; build 0 avisos, 0 erros |
| `03-test-architecture` | 59/59 |
| `04-test-unit` | 427/427 |
| `05-test-integration` | 189/189 |
| `06-test-postgresql` | 25/25 (PostgreSQL 16.15 descartável) |
| `06b-test-rabbitmq` | 19/19 |
| `06c-test-elasticsearch` | 13/13 |
| `06f-test-redis` | 23/23 |
| `06e-compose-baseline` | 103/103 (`p14-baseline-20261018115800`; 4 migrações da aplicação aplicadas, nenhuma na segunda inicialização; OpenAPI v1/v2 iguais a `baseline-phase-25`) |
| `06i-postgres16-patch-rehearsal` | 69/69 (`20261018120101`; backup 2,8 s, restore 2,7 s, volta 3,9 s, já com as tabelas da Fase 25) |
| `06j-compose-postgres-rollback` | 103/103 (`p16-rollback-20261018120249`) |
| `07-test-functional` | 322/322 |
| `07b-coverage` | descobertos = executados = aprovados (427/189/322, adapter 3.1.5); política OK com os mínimos elevados |
| `08-vulnerable-packages` | nenhum pacote vulnerável |
| `09-docker-compose-config` | exit 0 (placeholders de `.env.example`) |
| `10-capture-openapi` | exit 0; igual a `docs/refactoring/baseline-phase-25` |
| `11-graphify` | 7,270 nós, 17,600 arestas, 351 comunidades (`graphify-out/` git-ignored) |
| `12-git-diff-check` | exit 0 |
| `13-secret-scan` | OK (5 arquivos de settings, 8 de Compose/env, impressão digital da chave comprometida ausente em 4,504 arquivos) |

## 11. Testes adicionados

| Projeto | Antes → depois | Novos testes |
|---|---|---|
| Arquitetura | 53 → 59 | módulos de Domain só por ID/kernel compartilhado, código de provedor só em Integrations (sem `HttpClient` em Core/Persistence; `Microsoft.Extensions.Http` só lá), portas sem tipos de transporte, clientes sem log de URL (`RemoveAllLoggers` nos dois clientes tipados), nenhum `[HttpDelete]` nos controllers de dados mestres, mutações controladas de cada regra; contagem de controllers convencionais 3 → 12 |
| Unit | 188 → 427 | domínio (organização, configurações, catálogo, fornecedor, CNPJ numérico/alfanumérico, bordas de cada guarda) e handlers com fakes (criação, duplicidade e corrida, 404/428/412 por comando, no-op sem commit/auditoria, conflito no commit, rejeições de domínio, transições auditadas, consultas e paginação, enriquecimento, PTAX SP-5, validadores) |
| Integração | 146 → 189 | repositórios de dados mestres no InMemory (agregado com filhas, filtros, ordenação com desempate, seed do singleton) e adaptadores BrasilAPI/BCB contra servidor fake de loopback (todos os desfechos do INT-001, circuito, cancelamento, logs, contrato OData, registro, validação de configuração) |
| PostgreSQL 16.15 | 15 → 25 | forma da migração (índices únicos, FKs `RESTRICT`, CHECK, tipos), Down até a Fase 24 e Up idempotente, códigos únicos sob criação concorrente, tradução de violação de unicidade, CNPJ único por país, exclusão bloqueada (23503), corrida filha × status no mesmo agregado, versão obsoleta sem escrita, status + auditoria + histórico no mesmo commit, filtros em SQL com acentos e decimal exato |
| Funcional | 288 → 322 | ciclo de vida de departamento com ETag/If-Match/Idempotency-Key, códigos inválidos e duplicados, replay idempotente, regras de centro de custo, ORG-006 e consulta de usuários, configurações, fluxo de catálogo (dinheiro como string, 422 em referência inativa, filtros), ausência de DELETE, 404 com código em pt-BR, fluxo completo do fornecedor com trilha de auditoria sem PII, CNPJ duplicado/inválido/alfanumérico, visibilidade SUP-006, permissão de status, 412 em fornecedor, 503 do provedor sem bloquear entrada manual, proposta e aceite do enriquecimento, matriz de permissões (10 papéis × 17 operações) e 401 anônimo |

Cobertura (coverlet 10.1.0, merge de unit + integração + funcional): Domain 99,92% linhas; Application 97,76% linhas / 91,47% ramos; Integrations 95,35% / 84,26%; demais módulos sem regressão. `Tests/test-coverage-policy.json`: módulo `Template.Infrastructure.Integrations` acrescentado (94/83, 9 fontes), Application elevado para 96/90 e todos os `minimumSources` elevados ao medido; nenhum limite foi rebaixado.

Os testes nunca usam CNPJ real além do exemplo oficial da Receita (`12.ABC.345/01DE-35`) e do clássico `11.222.333/0001-81`; os demais são gerados com dígitos verificadores corretos.

## 12. Auditorias

| Auditoria | Resultado |
|---|---|
| `package-graph-snapshot.ps1 -Label phase25 -CompareWith phase24` | 267 produção / 471 teste; 21 diferenças de produção (todas do novo projeto `Template.Infrastructure.Integrations`: `Microsoft.Extensions.Http` 10.0.13 direto e dependências da mesma família 10.0.13 já presentes no repositório) e 2 de teste (`Microsoft.Extensions.Http` transitivo nos projetos funcional e Elasticsearch); exit 1 por desenho, mudança esperada e revisada |
| `license-inventory.ps1 -Label phase25` | 155 pacotes (eram 154; +`Microsoft.Extensions.Http`, MIT), 0 fora da política (exceções aceitas inalteradas: MediatR, VS Container Tools Targets) |
| `dotnet list ... --vulnerable --include-transitive` | nenhum |
| `dotnet list ... --deprecated --include-transitive` | só a família xunit 2.9.3 (adiada, só teste) |
| `dotnet-ef` | 10.0.13 instalado com `--tool-path` no scratchpad da sessão; nada adicionado ao repositório |

## 13. Rollback

- Código: reverter o merge da Fase 25. Os endpoints novos e o projeto de integrações desaparecem; o legado e a Fase 24 ficam intactos (o `UnitOfWork` volta a não traduzir violações de chave de negócio, que só ocorrem nas tabelas novas).
- Banco da aplicação: as onze tabelas são novas e independentes. Para voltar o esquema, aplicar o `Down` (`dotnet ef database update 20261018085240_Phase24AccessAuditIdempotency`) **somente com autorização**, porque descarta os dados mestres (ensaiado em PostgreSQL descartável). Sem rollback de esquema, o código anterior ignora as tabelas.
- Configuração: `BrasilApiSettings`/`BcbPtaxSettings` são ignoradas pelo código anterior; desabilitar um provedor é só `Enabled=false`, sem deploy de código.
- Contrato: `compose-baseline.ps1` volta a usar `baseline-phase-24` junto com o código.

## 14. Riscos residuais

- Os nomes de campo de BrasilAPI e PTAX não puderam ser lidos na documentação renderizada (SPA/template); estão fixados por testes com servidor fake e um smoke real autorizado deve confirmá-los antes de habilitar os provedores. Os Termos de Uso da BrasilAPI ainda estão em elaboração pelo projeto.
- BrasilAPI pode ainda não aceitar CNPJ alfanumérico; a consulta então devolve `400/404` do provedor (422/404 na borda) e a entrada manual segue disponível.
- A proveniência do aceite (`provider`, `retrievedAt`) é declarada pelo cliente a partir da proposta; o servidor limita idade e provedor, mas não guarda a proposta (não há regra aprovada de cache). Um token de proposta assinado pode ser avaliado depois.
- O circuit breaker é por processo; com várias instâncias cada uma abre o seu.
- ORG-004 ainda não tem fonte de dotações (Fase 27 substitui o adaptador); hoje a moeda base e o fuso podem mudar livremente com `organization.settings.manage`.
- O evento `procurement.supplier.status-changed` e a invalidação de aprovações (APR-012) dependem das Fases 28 e 31; até lá a suspensão só afeta novos compromissos (SUP-003) quando os consumidores existirem.
- A busca `q` usa `upper(...) LIKE '%…%'` sem índice trigram; adequada ao volume da Fase 33 (1.000 fornecedores, 5.000 itens) mas sem garantia de desempenho além disso.
- `title`/`detail` dos Problem Details seguem em inglês (como na Fase 24); a localização pt-BR fica para a SPA. Os códigos estáveis independem do idioma (teste com `Accept-Language: pt-BR`).
- A administração de usuários e papéis (criar/desativar usuários, atribuir papéis) continua fora da API (D25-14).

## 15. Arquivos alterados

| Área | Arquivos |
|---|---|
| Domain | `Common/` (`DomainError`, `MasterDataRules`, `MasterDataReference`, `CurrencyCodes`, `DecimalPrecision`); `Organization/` (`Department`, `CostCenter`, `OrganizationSettings`, `OrganizationErrors`); `Suppliers/` (`Supplier`, `SupplierParts`, `SupplierErrors`, `TaxIdentifier`); `Catalog/` (`Category`, `UnitOfMeasure`, `CatalogItem`, `CatalogErrors`); `Auditing/AuditRecord.cs` (chaves de detalhe) |
| Application | `Interfaces/{Organization,Suppliers,Catalog,Integrations}`; `Features/{Organization,Suppliers,Catalog}`; `Features/Access/Queries/ListUsersQuery.cs`; `Services/MasterData` (`MasterDataWriter`, `MasterDataQueries`); `Services/Integrations/PtaxRateResolver.cs`; `Common/Results/DomainErrorMapping.cs`, `ProblemCodes` (acréscimos); `Common/Concurrency/UniqueConstraintViolationException.cs`; `IAccountDirectory` (+2 membros); `ErrorCategory.Unavailable`; `LogEvents` 10000–10003; registro |
| Persistence | `Configurations/{Organization,Catalog,Supplier}Configurations.cs`; repositórios de organização, catálogo e fornecedores; `MasterDataQueryExtensions`; `UnitOfWork` (allow-list de chaves de negócio); seed do singleton; registro; migração `20261018135425_Phase25MasterData` (+ designer e snapshot gerados) |
| Identity | `IdentityAccountDirectory` (`IsActiveUserAsync`, `SearchUsersAsync`) |
| Integrations (novo) | `Template.Infrastructure.Integrations.csproj`, `ProviderSettings`, `Http/` (`ProviderHttpExecutor`, `ProviderCircuitBreaker`, `ProviderHttpExecutorFactory`, `ProviderJson`), `BrasilApi/BrasilApiClient`, `Bcb/BcbPtaxClient`, `DisabledEnrichmentProviders`, `ServiceRegistration` |
| WebApi | `Controllers/v2/` (`Departments`, `CostCenters`, `OrganizationSettings`, `Suppliers`, `Categories`, `UnitsOfMeasure`, `CatalogItems`, `Enrichment`, `Users`, `MasterDataControllerBase`, `MasterDataRequests`); `ApiResultMapper` (503); `StringEnumSchemaFilter` + `SwaggerExtensions`; `Program.cs`; `appsettings.json`; `Template.WebApi.csproj` |
| Solução e pacotes | `TemplateCleanArchitecture.sln`, `TemplateCleanArchitecture.App.slnf`, `Directory.Packages.props` (`Microsoft.Extensions.Http` 10.0.13) |
| Testes | arquitetura (5 regras + mutações; contagem de controllers), unit (`DomainTests/{Organization,Suppliers,Catalog}`, `ApplicationTests/Features/MasterData`, fakes de `IAccountDirectory`), integração (`Data/MasterDataPersistenceAdapterTests`, `Integrations/EnrichmentAdapterTests`), PostgreSQL (`MasterDataPostgreSqlTests`, fake de `IAccountDirectory`), funcional (`ApiEndpoints/v2/{OrganizationCatalog,Supplier}FunctionalTests`, `MasterDataPermissionMatrixTests`, `MasterDataTestSupport`) |
| Scripts e docs | `scripts/compose-baseline.ps1` (baseline da Fase 25); `docs/refactoring/baseline-phase-25/` (OpenAPI v1/v2); `docs/product/decision-log.md` (D25-1..18, nota no SP-5); `docs/configuration-secrets.md`; `AGENTS.md`; `Tests/test-coverage-policy.json`; este relatório |

## 16. Confirmações

Nenhum provedor de produção foi chamado; os testes usam só o servidor fake de loopback e o adaptador desabilitado. Nenhum segredo, token, CNPJ real ou dado pessoal real foi escrito em arquivo commitado, log ou evidência (CNPJs de teste são sintéticos ou o exemplo oficial da Receita). A ferramenta `dotnet-ef` ficou no scratchpad da sessão. Nenhuma migração aplicada foi reescrita. Nenhum dado ou recurso foi apagado; os recursos Docker criados pelos rehearsals ficam retidos conforme a política do repositório. Branch, commit, push e PR seguem a autorização permanente do dono para trabalho de fase.
