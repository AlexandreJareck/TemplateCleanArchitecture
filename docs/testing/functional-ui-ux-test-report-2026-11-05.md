# Functional and UI/UX test report — 2026-11-05

## Executive summary

The local Docker stack remained healthy during exploratory testing. The Angular application was tested through the browser with six seeded personas, on desktop and at a 390 × 844 mobile viewport. Authentication, permission-based navigation, master-data lists, requisitions, budget, sourcing, approvals, purchasing, receiving, reports, themes, and responsive navigation were exercised.

No browser console errors were recorded. The strongest parts of the current experience are the consistent visual language, role-aware navigation, responsive requisition cards, clear empty states, keyboard skip link, theme switching, and healthy loading of seeded data.

Nine actionable findings were recorded: three high, five medium, and one low priority. The highest-risk findings are an inert supplier-details action for read-only users, an existing requisition whose cost center appears blank in the editor, and the absence of an audit UI/route for the seeded auditor persona.

## Environment and evidence

- Date: 2026-11-05 (`America/Sao_Paulo`).
- Application: `http://localhost:4200`.
- Browser: Codex in-app browser.
- Desktop viewport: default application viewport.
- Mobile viewport: 390 × 844.
- Stack: all 13 Compose services running; infrastructure services with health checks reported healthy.
- Seed data: enabled.
- Browser console: no warnings or errors after the tested navigation.
- Test style: exploratory, black-box browser navigation with targeted source inspection after reproduction.
- Safety: no business record was created, edited, submitted, cancelled, deactivated, or deleted. Form drawers were cancelled after validation checks.

### Personas exercised

| Persona | Main coverage |
|---|---|
| `demo.admin` | Dashboard, departments, supplier list, master-data create/edit drawers |
| `demo.requester` | Own requisitions, requisition detail, draft editor, mobile requisition cards |
| `demo.buyer` | Sourcing queue and purchase-order queue |
| `demo.budget` | Budget allocations and allocation detail |
| `demo.manager` | Approval inbox |
| `demo.auditor` | Auditor navigation and report request form |
| `demo.receiver` | Receiving queue, theme switching, mobile navigation drawer |

## Coverage result

| Area | Result | Notes |
|---|---|---|
| Login/logout | Pass | Multiple persona switches completed successfully. Expired-session message was shown on the initial login page. |
| Permission-aware navigation | Pass with gap | Menu options changed correctly by persona, but the auditor has no audit-record route or menu item. |
| Dashboard | Pass | Clear greeting, role-based shortcuts, consistent desktop layout. |
| Master-data lists | Pass with issues | Search and empty state worked. Create/edit drawers opened and cancelled correctly. Client validation is incomplete. |
| Suppliers | Partial | Data loaded and search rendered; `Detalhes` was inert for a read-only user. Raw English status was displayed. |
| Requisitions | Partial | List, responsive cards, detail, totals, lines and editor loaded. Dates/timeline are raw, and the edit form lost the visible cost-center selection. |
| Budget | Pass with UX issues | Allocation list and detail loaded with correct totals. Timestamps and editable decimal display are not localized. |
| Sourcing | Pass for empty state | No submitted seeded requisition existed, so RFQ detail/award flow could not be exercised. |
| Approvals | Pass for empty state | Inbox loaded and escalation filter was accessible; no seeded pending approval existed. |
| Purchase orders | Pass for empty state | Queue and filters loaded; no seeded order existed. |
| Receiving | Pass for empty state | Queue loaded on desktop/mobile; no seeded issued order existed. |
| Reports | Partial | Type-dependent fields changed correctly. Invalid submission exposed raw server validation and technical identifiers. |
| Responsive layout | Pass | Mobile header, drawer, list-to-card transformation and no horizontal overflow at 390 × 844. |
| Theme switching | Pass | System → light → dark → system cycle worked and was restored after testing. |
| Accessibility smoke test | Pass with observations | Skip link, headings, labels, buttons, menu semantics and mobile drawer controls were exposed to the accessibility tree. |

## Findings

### F-01 — Supplier `Detalhes` button does nothing for read-only users

> **Resolution (2026-11-05): Fixed.** Details, files, tests and evidence in [Remediation results](#remediation-results-2026-11-05) → F-01. The original evidence below is kept unchanged.

- Priority: **High (P1)**
- Type: functional / permissions / UX
- Affected page: `/suppliers`
- Observed with: `demo.admin` and a read-oriented supplier view

Steps:

1. Log in with a persona that has `suppliers.read` but not `suppliers.manage`.
2. Open **Fornecedores**.
3. Click **Detalhes** for any supplier.

Actual result: focus moves to the button, but no drawer, route, feedback, or console error appears.

Expected result: a read-only supplier detail must open. Mutating controls should be hidden or disabled according to permissions.

Likely cause: the details button is always rendered and calls `open(item.id)`, but the entire `<app-form-drawer>` is inside `@if (canManage)`. The loaded selection therefore has no rendered destination.

Likely files:

- `Client/Template.WebApp/src/app/presentation/features/suppliers.page.html`
- `Client/Template.WebApp/src/app/presentation/features/suppliers.page.ts`

Acceptance criteria:

- `suppliers.read` can open all non-sensitive supplier detail fields.
- Manage/status actions remain permission-gated.
- Add a component test for read-only and managing personas.

### F-02 — Existing draft opens with a blank cost center in the editor

> **Resolution (2026-11-05): Fixed.** Details, files, tests and evidence in [Remediation results](#remediation-results-2026-11-05) → F-02. The original evidence below is kept unchanged.

- Priority: **High (P1)**
- Type: functional / data integrity risk
- Affected page: `/requisitions/:id/edit`
- Reproduced with: `REQ-2026-000001`

Steps:

1. Log in as `demo.requester`.
2. Open **Minhas requisições**.
3. Open `REQ-2026-000001` and click **Editar**.

Actual result: the detail shows `TEC-INFRA — Infraestrutura e Cloud`, but the editor's **Centro de custo** select displays `—`. The **Salvar** button remains available.

Expected result: the existing cost center must be selected when the edit form opens. Saving without an intentional change must preserve the current association.

Source evidence: `apply()` writes `requisition.costCenter.id` into `headerModel`, while lookup options are loaded asynchronously. The signal-form/native-select integration does not visibly reconcile the selected value when options arrive.

Likely files:

- `Client/Template.WebApp/src/app/presentation/features/requisition-editor.page.ts`
- `Client/Template.WebApp/src/app/presentation/features/requisition-editor.page.html`

Acceptance criteria:

- Edit mode displays the exact persisted cost center after both possible load orders.
- A no-change save preserves the cost-center ID.
- Add tests for “entity first/options later” and “options first/entity later”.

### F-03 — Auditor has no audit-record screen or navigation entry

> **Resolution (2026-11-05): Fixed.** Details, files, tests and evidence in [Remediation results](#remediation-results-2026-11-05) → F-03. The original evidence below is kept unchanged.

- Priority: **High (P1)**
- Type: missing functional surface
- Affected persona: `demo.auditor`

Steps:

1. Log in as `demo.auditor`.
2. Inspect the dashboard and sidebar.

Actual result: the auditor sees requisitions, sourcing, orders, budget, master data and reports, but there is no audit-trail entry. Inspection of frontend routes/navigation found no audit route.

Expected result: the seeded Auditor persona should have a discoverable, permission-gated audit-record list/detail experience backed by the existing v2 audit API.

Likely files:

- `Client/Template.WebApp/src/app/presentation/routes.ts`
- `Client/Template.WebApp/src/app/presentation/shell/navigation.ts`
- New audit page/application adapter files, reusing the generated API contract where available

Acceptance criteria:

- Add an `audit.read`-guarded route and navigation item.
- Support paging and the product-required filters without exposing sensitive detail fields.
- Show timestamps in the active locale and actor display information where allowed.

### F-04 — Report validation exposes raw English server text, field names and trace ID

> **Resolution (2026-11-05): Fixed.** Details, files, tests and evidence in [Remediation results](#remediation-results-2026-11-05) → F-04. The original evidence below is kept unchanged.

- Priority: **Medium (P2)**
- Type: validation / localization / error UX
- Affected page: `/reports`

Steps:

1. Log in as `demo.auditor`.
2. Keep **Requisição de compra (Pdf)** selected.
3. Leave **Requisição (ID)** empty and click **Gerar relatório**.

Actual result:

`Revise os campos informados validation One or more validation errors occurred. requisitionId: This parameter is required for RequisitionPdf. Trace: ...`

The submit button is enabled even though the required report parameter is missing.

Expected result: prevent the request client-side, identify the field in Portuguese, focus it, and show a concise localized message. A correlation/trace ID may be available under optional technical details but should not dominate the message.

Likely files:

- `Client/Template.WebApp/src/app/presentation/features/reports.page.ts`
- `Client/Template.WebApp/src/app/presentation/features/reports.page.html`
- `Client/Template.WebApp/src/app/presentation/shared/procurement-ui.problem-feedback.html`
- `Client/Template.WebApp/public/i18n/pt-BR.json`

Acceptance criteria:

- Dynamic validation rules follow the chosen report type.
- Submit is disabled or submission is blocked while required parameters are invalid.
- API field keys and English validation text are mapped to localized, human-facing copy.

### F-05 — Supplier status is shown as raw English enum text

> **Resolution (2026-11-05): Fixed.** Details, files, tests and evidence in [Remediation results](#remediation-results-2026-11-05) → F-05. The original evidence below is kept unchanged.

- Priority: **Medium (P2)**
- Type: localization / consistency
- Affected page: `/suppliers`

Actual result: all seeded suppliers show status `Active` while the surrounding UI is Brazilian Portuguese. Supplier detail/status actions also use raw domain status values in places.

Expected result: use the shared localized status badge/copy, for example `Ativo`.

Likely files:

- `Client/Template.WebApp/src/app/presentation/features/suppliers.page.html`
- `Client/Template.WebApp/src/app/presentation/shared/procurement-ui.ts`
- `Client/Template.WebApp/public/i18n/pt-BR.json`

### F-06 — Dates, instants and actor identifiers are not presented for humans

> **Resolution (2026-11-05): Fixed** (frontend remediation plus the additive `actorName` API follow-up). Details, files, tests and evidence in [Remediation results](#remediation-results-2026-11-05) → F-06. The original evidence below is kept unchanged.

- Priority: **Medium (P2)**
- Type: localization / auditability / UX
- Affected pages:
  - `/requisitions/mine`
  - `/requisitions/:id`
  - `/budget/allocations/:id`

Observed examples:

- Required date: `2026-12-17` instead of a pt-BR display such as `17/12/2026`.
- Timeline instant: `2026-11-02T16:55:03.417612Z`.
- Timeline actor: raw GUID `d0000000-0000-0000-0000-000000000002`.
- Budget amount history also shows a raw UTC instant.
- Editable allocation amount is rendered as `1800000.00`, unlike the localized display values around it.

Expected result: local dates and instants should use consistent localized formatters and the configured business time zone. Actor display name should be shown when permitted, with technical ID relegated to optional details. Decimal editors should clearly communicate the accepted input format.

Likely files:

- `Client/Template.WebApp/src/app/presentation/features/requisitions.page.html`
- `Client/Template.WebApp/src/app/presentation/features/requisition-detail.page.html`
- `Client/Template.WebApp/src/app/presentation/features/budget-allocation-detail.page.html`
- A shared local-date/instant presentation utility or pipe

### F-07 — Report filters require users to paste raw entity IDs

> **Resolution (2026-11-05): Fixed.** Details, files, tests and evidence in [Remediation results](#remediation-results-2026-11-05) → F-07. The original evidence below is kept unchanged.

- Priority: **Medium (P2)**
- Type: usability / error prevention
- Affected page: `/reports`

Actual result:

- Requisition PDF asks for **Requisição (ID)**.
- Quote comparison asks for an RFQ ID.
- Budget consumption asks for **Centro de custo (ID, opcional)**.
- Supplier quotes asks for a supplier ID.
- Currency is a free-text field even though the application uses a controlled currency set.

Expected result: searchable entity selectors should show business identifiers/names while submitting stable IDs. Currency should be a controlled select.

Likely files:

- `Client/Template.WebApp/src/app/presentation/features/reports.page.ts`
- `Client/Template.WebApp/src/app/presentation/features/reports.page.html`
- Existing master-data/requisition/RFQ readers can be reused rather than duplicating API logic.

### F-08 — Master-data create form enables save for a syntactically invalid code

> **Resolution (2026-11-05): Fixed.** Details, files, tests and evidence in [Remediation results](#remediation-results-2026-11-05) → F-08. The original evidence below is kept unchanged.

- Priority: **Medium (P2)**
- Type: client validation / error prevention
- Affected page: `/organization/departments`

Steps:

1. Click **Cadastrar**.
2. Enter code `x` and name `A`.
3. Move focus out of the name field.

Actual result: **Salvar** becomes enabled. The domain code rule requires an uppercase code matching a pattern with a minimum length of two characters.

Expected result: mirror stable syntactic domain rules client-side, show an inline localized message, and keep submit disabled. The API/domain remains authoritative.

Likely files:

- `Client/Template.WebApp/src/app/presentation/features/master-data.page.ts`
- `Client/Template.WebApp/src/app/presentation/features/master-data.page.html`
- Shared validation/i18n resources

### F-09 — Requisition attachment help text is stale

> **Resolution (2026-11-05): Fixed.** Details, files, tests and evidence in [Remediation results](#remediation-results-2026-11-05) → F-09. The original evidence below is kept unchanged.

- Priority: **Low (P3)**
- Type: content / product consistency
- Affected page: `/requisitions/:id/edit`

Actual result: the UI says, `Somente metadados nesta fase; o envio do arquivo chega na Fase 30.` The repository is already beyond Phase 30 and contains the file-storage/report implementation.

Expected result: either integrate the current stored-file flow into requisition attachments or replace the roadmap wording with accurate present-tense behavior and limitations.

Likely file:

- `Client/Template.WebApp/public/i18n/pt-BR.json` (`requisition.attachmentsNotice`)

## Positive UI/UX observations

- Visual hierarchy is strong and consistent across dark and light themes.
- Permission-based navigation changes correctly between the exercised personas.
- Desktop tables transform into readable cards on mobile without horizontal overflow.
- The 390 × 844 mobile header and navigation drawer work and expose accessible names.
- Empty states are concise and appear in sourcing, approvals, orders and receiving.
- The skip link, page headings, form labels, status filter and pagination controls are exposed to the accessibility tree.
- Monetary display values use Brazilian currency formatting consistently.
- No browser console warnings or errors appeared during the complete navigation pass.

## Gaps not fully testable with the seeded state

The default seed intentionally stops before transactional workflows. Consequently, the following require a separate end-to-end scenario or a deterministic test fixture:

- requisition submit/withdraw/cancel/copy and reservation effects;
- RFQ create/invite/publish/quote/compare/award;
- multi-level approval decisions and durable workflow progress;
- purchase-order draft, issue, PDF and supplier notification;
- receipt posting, discrepancy handling and correction;
- successful report generation, polling, download, expiry and retry;
- file upload/read-back/malware-scan states.

## Implementation handoff

### Objective

Resolve F-01 through F-09 without weakening server-side validation, permission checks, concurrency handling, or the generated API contract. Keep the frontend in Brazilian Portuguese and preserve the existing visual system.

### Recommended sequence

1. **Protect data integrity:** fix F-02 and add regression tests for asynchronous lookup/entity load ordering.
2. **Restore blocked capabilities:** fix F-01 and implement F-03.
3. **Create shared presentation primitives:** localized date/instant/status/actor/error display for F-04, F-05 and F-06.
4. **Improve report inputs:** implement typed selectors and dynamic validation for F-04 and F-07.
5. **Mirror stable syntactic validation:** fix F-08 while retaining API/domain authority.
6. **Correct product copy or integrate files:** resolve F-09.

### Constraints

- Do not hand-edit generated API client files.
- Keep permission checks in both route/navigation and action rendering.
- Do not expose allow-listed audit detail fields beyond the current product rules.
- Do not replace server validation; client validation is for earlier feedback only.
- Preserve ETag/version/concurrency behavior on writes.
- Keep raw IDs available only where operationally useful, preferably under technical details.
- Add or update component/application tests for every fixed finding.

### Suggested verification

From `Client/Template.WebApp`:

```powershell
pnpm validate
```

Then rebuild and exercise the local stack:

```powershell
docker compose up -d --build webapp webapi worker workflow-functions
docker compose ps
```

Browser regression checklist:

1. Read-only supplier user can open details but cannot mutate them.
2. `REQ-2026-000001` edit form shows `TEC-INFRA` selected and a no-change save preserves it.
3. `demo.auditor` sees and can use the audit screen according to permissions.
4. Empty report submission is blocked locally with Portuguese field feedback and no raw API keys.
5. Supplier and workflow statuses are localized consistently.
6. Dates/instants display in pt-BR and timeline actors are human-readable.
7. Report selectors use business labels and submit correct IDs.
8. Invalid master-data code `x` is rejected before network submission.
9. Attachment copy matches current behavior.
10. Repeat desktop and 390 × 844 mobile smoke tests in both light and dark/system themes.

For transactional coverage, create an isolated deterministic scenario that advances one requisition through RFQ, approval, purchase order and receipt. Do not depend on manual mutation of the shared demo scenario for automated tests.

## Remediation results (2026-11-05)

Executed from `docs/testing/execute-functional-ui-ux-handoff.md`. The F-06 follow-up (see F-06 → *Actor names from the API*) later added one additive, nullable `actorName` field to three v2 representations; everything else below is frontend-only (`Client/Template.WebApp`): no C#, project, migration, HTTP contract or OpenAPI change (`api:drift`: 211 generated files match; the generated client was not edited). Server validation, permissions, audit allow-lists and ETag/`If-Match` handling are unchanged. Paths below are relative to `Client/Template.WebApp/`.

### Summary

| Finding | Status | Main change |
|---|---|---|
| F-01 | **Fixed** | Supplier detail drawer renders for `suppliers.read`; edit forms need `suppliers.manage`, lifecycle buttons `suppliers.status.change`. |
| F-02 | **Fixed** | `hlm-native-select` re-applies its value when projected options change; the editor always offers the persisted cost center. |
| F-03 | **Fixed** | New `/audit` screen (`audit.read` route guard, menu item, dashboard shortcut) on the existing v2 audit API. |
| F-04 | **Fixed** | Report parameters validated per type before any request; shared Problem Details feedback is localized and moves code/detail/trace into collapsed "Detalhes técnicos". |
| F-05 | **Fixed** | Supplier statuses via an entity-specific status group (`Ativo`, `Suspenso`…); missing workflow statuses added; unknown values fall back to the raw value instead of a key. |
| F-06 | **Fixed** | pt-BR business dates, instants in the organization time zone, human actor labels and a decimal editor hint/preview. Timelines, RFQ history and the audit screen name every actor from the API's `actorName` for any reader of the resource. |
| F-07 | **Fixed** | Searchable selectors for requisition, RFQ, cost center and supplier (business labels, stable IDs submitted); currency is a controlled list from organization settings. |
| F-08 | **Fixed** | Domain code syntax (ORG-001/CAT-001) mirrored per kind, inline localized message, Save disabled; the code is sent normalized. |
| F-09 | **Fixed** | Attachment copy now describes present behavior (metadata only, no file upload); sizes, statuses and media types are human-readable. |

### F-01 — Supplier `Detalhes` for read-only users

- Implementation: the drawer is no longer wrapped in `@if (canManage)`. Without `suppliers.manage` it shows a read-only summary (legal/trade name, country, CNPJ, primary contact and address as returned by the API, linked category count); edit, enrichment, contact/address/category forms stay behind `suppliers.manage`, and Activate/Suspend/Inactivate/Reactivate behind `suppliers.status.change`. The page now uses the shared `app-problem-feedback`. The **Detalhes** button names the supplier for assistive technology.
- Files: `src/app/presentation/features/suppliers.page.html`, `suppliers.page.ts`, `public/i18n/pt-BR.json`, `public/i18n/en.json`.
- Tests: `src/app/presentation/features/suppliers.page.spec.ts` (read-only persona, manager persona, status-only persona).
- Manual evidence: real stack, `demo.auditor` opened a supplier detail with zero inputs and no Save/Suspend buttons, desktop and 390 × 844 without horizontal overflow; `demo.buyer` still gets an editable form.
- Remaining risk: none known; the API remains the authority for every write.

### F-02 — Blank cost center in the draft editor

- Root cause: the generated `hlm-native-select` binds `[value]` once; a value written before its asynchronously loaded `<option>`s exist leaves the browser on the first option (`—`).
- Implementation: the primitive re-applies the model value whenever its projected options change (`contentChildren` + `afterRenderEffect`), recorded as a local change in `src/app/presentation/shared-ui/SPARTAN_PROVENANCE.md`. The editor's cost-center list also always contains the persisted cost center (even if inactive or outside the first lookup page), so the association is never shown as empty and a no-change save sends the same ID with the version it read.
- Files: `src/app/presentation/shared-ui/native-select/src/lib/hlm-native-select.ts`, `hlm-native-select.html`, `src/app/presentation/features/requisition-editor.page.ts`.
- Tests: `hlm-native-select.spec.ts` (fails on the previous primitive, passes now), `src/app/presentation/features/requisition-editor.page.spec.ts` (entity-first, options-first, inactive persisted cost center, no-change save preserves `costCenterId` and sends version `v7`).
- Manual evidence: real stack, `demo.requester`, `REQ-2026-000001` edit shows `TEC-INFRA — Infraestrutura e Cloud` selected on desktop and mobile; the no-change save was intercepted before reaching the API (no demo data changed) and its body carried the selected cost-center ID.
- Remaining risk: every other native select in the app now also self-heals; covered by the full unit and E2E suites.

### F-03 — Auditor audit screen

- Implementation: Application `AuditPort`/`AuditUseCases` (actor GUID and date-range checks), Infrastructure `AuditApiAdapter` on the generated `AuditRecordsService`, Presentation `/audit` page (route `permissionGuard` with `audit.read`, menu item **Auditoria** under Análises, dashboard shortcut). Filters: action, resource type, from/to business dates converted to UTC bounds in the organization time zone (`to` exclusive next day), sort, and technical ID filters (resource, actor) in a collapsed section. Rows show localized instant (seconds), action code, resource, localized transition, human actor, reason, and a collapsed details list with exactly the server's allow-listed `details`, rule IDs, resource/actor/correlation IDs. No write affordance (AUD-002, SOD-8). Responsive cards on mobile.
- Files: `src/app/application/auditing/audit.ts`, `src/app/infrastructure/api/adapters/audit-api-adapter.ts`, `src/app/presentation/features/audit.page.{ts,html,css}`, `src/app/presentation/routes.ts`, `src/app/presentation/shell/navigation.ts`, `src/app/presentation/features/dashboard.page.ts`, `src/app/presentation/state/ports.ts`, `src/app/bootstrap/app-providers.ts`, i18n files.
- Tests: `src/app/application/auditing/audit.spec.ts`, `src/app/infrastructure/api/adapters/audit-api-adapter.spec.ts`, `src/app/presentation/features/audit.page.spec.ts` (route/menu permission, listing, UTC filter bounds, malformed actor blocked before the API).
- Manual evidence: real stack, `demo.auditor` found **Auditoria** in the menu, listed 27 records, filtered by `security.session.created`, desktop and mobile without overflow, light and dark themes; `demo.requester` has no menu item and `/audit` shows **Acesso negado**.
- Remaining risk: action and resource type are free-text exact filters (the API offers no catalog endpoint); placeholders show the expected format.

### F-04 — Raw server validation in reports

- Implementation: `ReportUseCases.requiredParameters`/`validate` derive required fields from the catalog (`name` vs `name?`) plus the date order and 366-day window; submit is blocked, the first invalid field is focused and each field shows Portuguese copy. The shared `ProblemFeedback` now shows the localized problem kind, a localized message for known stable codes (`problems.codes.*`), and field problems by localized label (`problemFields.*` or page-supplied labels) with a localized category instead of English text; code, rule ID, server detail and trace ID are under a collapsed **Detalhes técnicos**. Master-data and supplier pages dropped their bespoke raw feedback blocks.
- Files: `src/app/application/reports/reports.ts`, `src/app/presentation/features/reports.page.{ts,html}`, `src/app/presentation/shared/procurement-ui.ts`, `procurement-ui.problem-feedback.html`, `master-data.page.*`, `suppliers.page.*`, i18n files.
- Tests: `src/app/application/reports/reports.spec.ts`, `src/app/presentation/features/reports.page.spec.ts`, `src/app/presentation/shared/display.spec.ts`; E2E `e2e/reports.spec.ts`, `e2e/master-data.spec.ts`, `e2e/requisitions.spec.ts`, `e2e/sourcing.spec.ts` updated to expect localized messages and collapsed technical details.
- Manual evidence: real stack, empty **Requisição de compra** request blocked with "Obrigatório para este tipo de relatório." and no request sent; no `requisitionId` or `Trace` text in the page.
- Remaining risk: 59 frequent stable codes have localized messages; other codes show the localized kind headline with the code under technical details.

### F-05 — Untranslated supplier status

- Implementation: `StatusBadge` accepts a `prefix` (translation group) so supplier statuses use the masculine `supplierStatuses` group; workflow statuses missing from `statuses` were added (Invalidated, Issued, PartiallyReceived, Completed, Posted, Available, Deleted, Waiting, Sent, NotRequested, Suspended, Inactive, Revoked); tones added; an unknown status renders its raw value instead of a translation key.
- Files: `src/app/presentation/shared/procurement-ui.ts`, `procurement-ui.status-badge.html`, `suppliers.page.html`, i18n files.
- Tests: `suppliers.page.spec.ts`, `display.spec.ts`.
- Manual evidence: supplier list shows **Ativo**; audit transitions show **Ativa**/**Revogada**.

### F-06 — Dates, instants, actors and decimal editors

- Implementation: Domain `date-display.ts` (business date as the same calendar day, instant in a time zone, local-day → UTC bound, file size); Presentation `LocalDatePipe`, `InstantPipe`, `FileSizePipe` and `app-actor`; `OrganizationContext` loads the organization time zone and currencies once per session (`organization.read`, all seeded roles) with `America/Sao_Paulo` as fallback. Applied to requisition list/detail/timeline, RFQ deadline/comparison/rates/timeline, sourcing queue, budget periods and amount history, purchase-order list/detail, receipts, approvals and reports (the previous per-page `instant()` helpers used the browser zone and were removed). `app-actor` shows "Nome (você)" for the current user, the directory name when the session may read the user directory (`access.users.manage`), otherwise "Outro usuário", with the ID only as a hover hint. The allocation amount editor explains the accepted format, validates it and previews the localized value (`R$ 1.800.000,00`).
- Files: `src/app/domain/date-display.ts`, `src/app/application/organization/organization-settings.ts`, `src/app/infrastructure/api/adapters/organization-api-adapter.ts`, `src/app/presentation/shared/display.ts`, `src/app/presentation/state/organization.context.ts`, `actor-directory.ts`, and the feature templates/components listed above.
- Tests: `src/app/domain/date-display.spec.ts`, `display.spec.ts`, `audit-api-adapter.spec.ts` (organization adapter), `e2e/sourcing.spec.ts` (deadline `04/03/2027`).
- Manual evidence: `REQ-2026-000001` shows needed-by `17/12/2026`, timeline `02/11/2026, 13:55 · Lucas Almeida (você)`; no raw ISO instant or GUID on requisition and budget pages; budget amount preview visible.
- Status rationale at the first remediation (**Partially fixed**): for sessions without `access.users.manage`, other people appeared as "Outro usuário" because no API exposed display names to them.
- Actor names from the API (follow-up, **Fixed**): `RequisitionTimelineEntryView`, `RfqHistoryEntryView` and `AuditRecordView` gained `actorName` (string, nullable, additive; OpenAPI v1 unchanged, v2 +3 optional properties, new baseline `docs/refactoring/baseline-f06-actor-names` used by `OpenApiContractTests`, `scripts/compose-baseline.ps1`, the CI comment and `orval.config.ts`). The handlers resolve every distinct actor ID of the page in one call to the new Application port `IUserDisplayNames` (implemented by `IdentityApproverDirectory`, registered in the WebApi and the WorkflowFunctions directory composition): display name, or user name when no name is set; never email, phone or claims. Names are added only after the existing read checks (requisition REQ-001/011 visibility, `sourcing.rfqs.manage`/`sourcing.quotes.read`, `audit.read`), so a 403/404 response carries no name and no lookup runs. `null` means a system action or an actor no longer in the directory. Frontend: generated client regenerated with `pnpm api:generate` (no hand edit), adapters map `actorName`, and `ActorDirectory.label(id, name)`/`app-actor [actorName]` prefer the API name; the current user still shows "Nome (você)", the `access.users.manage` directory remains a fallback for representations without a name, and the ID stays only in the hover hint.
- Follow-up files: `Src/Core/Template.Application/Interfaces/Access/IUserDisplayNames.cs`, `Features/Requisitions/RequisitionViews.cs`/`RequisitionQueries.cs`, `Features/Sourcing/SourcingViews.cs`/`QuoteUseCases.cs`, `Features/Auditing/Queries/ListAuditRecordsQuery.cs`, `Interfaces/Auditing/IAuditRecordReader.cs`, `Interfaces/Approvals/IApprovalPorts.cs` (`IApproverDirectory : IUserDisplayNames`), `Src/Infrastructure/Template.Infrastructure.Identity/ServiceRegistration.cs`; frontend `application/{auditing,procurement,sourcing}`, the three adapters, `presentation/state/actor-directory.ts`, `presentation/shared/display.ts` and the requisition, RFQ and audit templates.
- Follow-up tests: unit `ListAuditRecordsQueryHandlerTests` (one batched lookup, system/unknown actors unnamed, invalid request or empty page never reaches the directory), `RequisitionHandlerTests` and `SourcingHandlerEdgeCaseTests` (names from one lookup; a forbidden/unreadable request triggers no lookup); functional `ActorNameFunctionalTests` (requester and auditor read the requisition timeline with the actor's name and no email, an outsider gets 404 without names; an auditor reads the RFQ history with the buyer's name, the requester gets 403; the audit query names the actor for `audit.read` and is 403 without it; the readers hold no `access.users.manage`); `OpenApiContractTests` against the new baseline; Vitest `display.spec.ts` (API name preferred without the directory permission and without a directory call, blank name falls back, self still wins) and `audit-api-adapter.spec.ts`.
- Follow-up evidence: backend unit 839/839, functional 392/392, integration 268/268, Worker 5/5, WorkflowFunctions 26/26, PostgreSQL requisition/sourcing/access suites 35/35 (Docker; `T_NFR_PERF_4` timed out once while the browser suite ran in parallel and passed when rerun alone); frontend `pnpm validate` chain green (Prettier content check with `--end-of-line auto` on this CRLF checkout, lint, typecheck, Vitest 37 files 132/132, dependency-cruiser no violation, 5/5 mutations, API drift 211 files match, build) and mocked Playwright 21/21 (after clearing a stale `.angular` cache). The real Compose stack was not rebuilt for this follow-up.

### F-07 — Raw entity IDs in report filters

- Implementation: reusable `EntityPicker` (debounced API search, business labels, stable ID emitted, externally supplied IDs such as `?requisitionId=` resolved to their label). Requisition (`scope=visible`, number — cost center · status), RFQ (number — requisition · status), cost center (code — name) and supplier (legal name + CNPJ). Currency is a select of the organization's supported currencies with "Moeda base (BRL)" as the empty option.
- Files: `src/app/presentation/shared/entity-picker.ts`, `reports.page.{ts,html}`, `organization.context.ts`.
- Tests: `reports.page.spec.ts` (business label → stable ID, controlled currency list), `e2e/reports.spec.ts` (select by `REQ-2027-000001 — TEC-INFRA · Rascunho`, posted `requisitionId: 'r1'`).
- Manual evidence: real stack, `REQ-2026-000001` chosen by label, the intercepted POST carried its ID; budget consumption shows a currency select and `TEC-INFRA` in the cost-center selector; mobile without overflow.
- Remaining risk: selectors list the first 20 matches; narrowing is by search text.

### F-08 — Invalid master-data code

- Implementation: `MASTER_DATA_CODE_RULES` and `MasterDataUseCases.codeProblem` mirror `MasterDataRules` (organization `^[A-Z0-9-]{2,20}$`, category/unit `^[A-Z0-9-]{1,20}$`, catalog item `^[A-Z0-9][A-Z0-9._-]{0,39}$`) after the server's trim + upper-case normalization; Signal Forms `validate`/`maxLength` keep Save disabled; inline `role="alert"` message with `aria-invalid`/`aria-describedby` and a per-kind hint; the code is sent normalized.
- Files: `src/app/application/master-data/master-data.ts`, `src/app/presentation/features/master-data.page.{ts,html}`, `data-pages.css`, i18n files.
- Tests: `master-data.spec.ts` (all kinds), `master-data.page.spec.ts` (`x` rejected without a request, pattern violation, valid code normalized).
- Manual evidence: real stack, `demo.admin`, **Departamentos** → **Cadastrar**, code `x`, name `A`: "O código deve ter de 2 a 20 caracteres.", Save disabled, no POST observed.

### F-09 — Stale attachment copy

- Implementation: `requisition.attachmentsNotice` now states present behavior: only the attachment details (name, type, size) are recorded; the file itself is not uploaded or stored. Media types are shown as friendly labels, sizes as `2 KB`, and the `Pending` attachment status as "somente dados do anexo". Integrating the Phase 30 stored-file flow into requisition attachments was not done: the requisition attachment contract still carries metadata only (FIL-002), so it would need a backend/API change.
- Files: i18n files, `requisition-editor.page.{ts,html}`, `requisition-detail.page.{ts,html}`.
- Tests: `display.spec.ts` (file size), real-stack check of the notice on mobile.

### Additional fix found during verification

- The skip link (`href="#main-content"`) made the router run a late fragment navigation that could cancel the next in-app navigation (reproduced on the unchanged `HEAD` with instrumentation; it surfaced as a failure of the keyboard E2E test). The link now focuses and scrolls to `#main-content` without changing the URL (`src/app/presentation/shell/app-shell.{ts,html}`); the keyboard E2E passes.

### Commands and results

Run from `Client/Template.WebApp` with the pinned Node 24.22.0 and pnpm 12.14.0.

| Command | Result |
|---|---|
| `pnpm exec prettier --check --end-of-line auto .` (this checkout has `core.autocrlf=true`; plain `pnpm format:check` flags every untouched CRLF file) | All files formatted |
| `pnpm lint` | 0 problems |
| `pnpm typecheck` | Passed |
| `pnpm test` | 37 files, **131/131** passed |
| `pnpm architecture` | No violations (225 modules, 548 dependencies) |
| `pnpm architecture:mutations` | 5/5 mutations rejected |
| `pnpm api:drift` | 211 generated OpenAPI files match |
| `pnpm build` | Succeeded, no budget warnings |
| `pnpm audit:licenses` | 458 records approved |
| `pnpm audit:security` | No high/critical (2 low, 3 moderate, pre-existing) |
| `pnpm e2e` (mocked, run on port 4300 because the Compose `webapp` holds 4200) | **21/21** passed, axe checks included |
| `docker compose up -d --build webapp webapi worker workflow-functions`, then `docker compose up -d --build webapp` | All running; webapp, worker, workflow-functions healthy; WebApi `/health/ready` Healthy; no new error lines in the four services' logs |
| Real-stack verification script (Playwright, non-mutating: intercepted writes) | 9/9 checks passed; browser console clean apart from the 401 of the anonymous session probe on `/login` and the intentionally aborted requests |

### Manual browser checklist

| # | Check | Result |
|---|---|---|
| 1 | Read-only supplier user opens details, cannot mutate | Pass (`demo.auditor`), desktop and mobile |
| 2 | `REQ-2026-000001` shows `TEC-INFRA`; no-change save keeps the ID | Pass (save intercepted) |
| 3 | `demo.auditor` discovers and uses the audit screen | Pass; denied for `demo.requester` |
| 4 | Incomplete report blocked locally, localized, no API keys | Pass |
| 5 | Supplier and workflow statuses localized | Pass |
| 6 | Dates/instants pt-BR in the business time zone | Pass |
| 7 | Timeline actors human-readable when allowed | Pass for the current user and directory readers at the first remediation; the follow-up names every actor for any reader through the API `actorName` (automated evidence, see F-06) |
| 8 | Report selectors show business labels, submit stable IDs | Pass |
| 9 | Currency from a controlled list | Pass |
| 10 | Code `x` rejected before any request | Pass |
| 11 | Attachment copy matches behavior | Pass |
| 12 | Desktop and 390 × 844, light and dark | Pass, no horizontal overflow |

### Remaining risks and follow-up

- F-09: uploading real attachment bytes for requisitions remains a backend feature (FIL-002 metadata-only contract).
- The deterministic transactional scenario recommended above (requisition → RFQ → approval → PO → receipt) was not added here; it is covered by the existing real-stack SC-01 journey (`e2e-real`), which was not rerun.
- `pnpm validate`'s `format:check` fails on Windows checkouts with `core.autocrlf=true`; CI on Linux is unaffected.

## Post-remediation validation (2026-11-08)

### Scope and source identity

- Validation window: 2026-11-07 through 2026-11-08, `America/Sao_Paulo`; final container/browser evidence captured on 2026-11-08.
- Validated commit: `660438b9af8b87a7d0d30397076b81e003623296` (`docs: document the real-journey script and the line-ending setting`).
- Initial worktree: clean except for the user-provided, untracked `docs/testing/execute-functional-ui-ux-validation-handoff.md`; it was preserved unchanged.
- Remediation baseline: `e9cc87ffacc2ec661618545b66686bc2d2977907`; changes through `HEAD` affect the Angular client, Application contracts/views, Identity registration, functional/unit tests, generated OpenAPI client/baseline and release-journey tooling.
- Affected service mapping: `webapp` directly; `webapi` through Application and Identity; `worker` through Application; `workflow-functions` through Application and Identity. All four application images were therefore rebuilt.
- No production code or business data was changed during this pass. Authentication created the expected demo session/audit records. Temporary validation specs and toolchain shims were removed after execution; only this report and sanitized screenshots remain.

### Automated validation

The exact repository-pinned tools were used: Node `24.22.0`, pnpm `12.14.0` and .NET SDK `10.0.402`. The host initially had Node `24.22.1`, pnpm `11.25.0` and .NET SDK `10.0.401`; exact versions were installed in temporary directories rather than weakening `engines` or changing `global.json`.

| Command | Result |
|---|---|
| `pnpm install --frozen-lockfile` | Passed; lockfile policy verified, no lockfile change. |
| `pnpm validate` | Passed: Prettier, ESLint, typecheck, Vitest **37 files / 132 tests**, dependency-cruiser **225 modules / 548 dependencies**, **5/5** architecture mutations rejected, **211** generated OpenAPI files matched, production build succeeded without a budget warning. |
| `pnpm e2e` | First parallel run: **16 passed / 5 timed out** under six-worker resource contention. The same complete suite with `pnpm exec playwright test --workers=1`: **21/21 passed** in 1.9 minutes. The five failures all passed serially (approvals, purchasing, reports, requisitions and sourcing). Mock-fixture console warnings for three intentionally unmapped problem codes and one sourcing warning remain test-fixture noise; the real-stack console was clean. |
| `dotnet restore TemplateCleanArchitecture.App.slnf` | Passed for all projects. |
| `dotnet build TemplateCleanArchitecture.App.slnf --no-restore` | Passed, **0 errors / 1 warning**: existing nullable warning `CS8604` at `Tests/Template.WorkflowFunctions.Tests/HostBoundaryTests.cs:106`. |
| `dotnet test TemplateCleanArchitecture.App.slnf --no-build` | All completed projects passed, but the aggregate process left `Template.Integration.Tests` waiting after the other hosts exited and was interrupted after more than 20 quiet minutes. Counts completed in the aggregate: Unit 839, Functional/OpenAPI 392, Architecture 77, PostgreSQL 78, Redis 23, RabbitMQ 30, Azurite 6, Elasticsearch 13, Workflow Functions 26, Workflow Functions Integration 7, Worker 5, Performance 5. |
| `dotnet test Tests/Template.Integration.Tests/Template.Integration.Tests.csproj --no-build --verbosity normal --blame-hang-timeout 5m` | Isolated rerun passed **268/268** in 21.16 seconds with no hang dump. Across the aggregate plus isolated rerun, **1,769/1,769** tests passed and no test failed. The aggregate-host wait is recorded as a harness/concurrency risk, not hidden as a green aggregate exit. |

The functional suite includes the actor-name authorization cases and the approved OpenAPI v1/v2 baseline. The architecture suite passed. No migration was introduced by the remediation. The legacy VSIX was not run because no VSIX file or dependency changed.

### Docker rebuild and infrastructure evidence

`docker compose config --quiet` passed. Pinned third-party services were pulled by digest (`db`, Elasticsearch, Kibana, RabbitMQ, Redis, Redis Insight, both Azurite services and Workflow Monitor); the repository pins were preserved and no volume was removed or recreated.

| Service | Pre-rebuild image ID / created | Post-rebuild image ID / created | Result |
|---|---|---|---|
| `webapp` | `296897730889` / `2026-11-06T16:07:50Z` | `863da586f55c` / cached config timestamp `2026-11-06T16:07:50Z` | Rebuilt with `--pull`; manifest ID changed. |
| `webapi` | `fba2e6d540cd` / `2026-11-06T16:09:38Z` | `6502a442f72b` / `2026-11-08T19:43:40Z` | Rebuilt and recreated. |
| `worker` | `975f7d9d2e86` / `2026-11-06T16:10:14Z` | `5d0ff4a48086` / `2026-11-08T19:44:16Z` | Rebuilt and recreated; Chromium headless shell was freshly downloaded during the image build. |
| `workflow-functions` | `751dd7c1e0f0` / `2026-11-06T16:09:38Z` | `e68171deab2a` / cached config timestamp `2026-11-08T19:14:41Z` | Rebuilt and recreated; manifest ID changed. |

Commands: `docker compose pull ...`, `docker compose up -d` for pinned infrastructure, `docker compose build --pull webapp webapi worker workflow-functions`, then `docker compose up -d --no-deps webapp webapi worker workflow-functions`.

Final state:

- `webapp`, `worker` and `workflow-functions`: running and Docker `healthy`; `webapi`: running, with both HTTP health endpoints healthy (the Compose service has no container health check).
- PostgreSQL, Redis, RabbitMQ, Elasticsearch, Kibana, Redis Insight, file Azurite and workflow Azurite: running and Docker `healthy` where configured. Workflow Monitor has no Compose health check and returned HTTP 200.
- HTTP 200: WebApp `:4200`, WebApi `/health/live`, `/health/ready`, Swagger UI and OpenAPI v2, Worker liveness/readiness, Workflow Functions liveness/readiness, Workflow Monitor, Redis Insight, RabbitMQ management, Elasticsearch and Kibana.
- Anonymous Azurite REST list probes returned HTTP 404 as expected without a signed storage request; both Azurite health checks passed and WebApi file-storage readiness was healthy.
- Error-level scan of the rebuilt service logs (`[ERR]`, `[FTL]`, `fail:` and unhandled exceptions): no matches. The broader scan showed two harmless Azure Functions first-use blob-not-found probes and no persistent reconnect loop, migration failure, asset 404 or HTTP 5xx.

### Browser and UI/UX evidence

The containerized application at `http://localhost:4200` was exercised with `demo.auditor`, `demo.requester` and `demo.admin`. The password was read inside the process from the git-ignored `.env`, was never printed, and was cleared from the process environment afterward. The focused real-stack run performed no supplier, requisition, report or master-data write.

- Focused real-stack Playwright: final **4/4 passed** plus a separate F-01 mobile check **1/1 passed**. Assertions covered permissions/direct routes, labels versus stable IDs, no POST for invalid forms, console errors, HTTP 5xx, axe, keyboard focus, focus restoration, three theme preferences, 1365×768 desktop and 390×844 mobile overflow.
- Independent in-app-browser check: rebuilt login loaded on desktop and 390×844, `scrollWidth = clientWidth = 390`, keyboard Tab focused **Usuário** with a visible focus ring, and the browser console contained no warnings or errors.
- Real-stack console errors: none. Real-stack HTTP 5xx responses: none. No unexpected duplicate write was observed; the invalid report and invalid master-data cases sent zero POST requests.
- Screenshots:
  - [`f01-supplier-readonly-desktop.png`](evidence/2026-11-08-post-remediation/f01-supplier-readonly-desktop.png)
  - [`f01-supplier-readonly-mobile.png`](evidence/2026-11-08-post-remediation/f01-supplier-readonly-mobile.png)
  - [`f02-f09-requisition-editor.png`](evidence/2026-11-08-post-remediation/f02-f09-requisition-editor.png)
  - [`f03-audit-mobile.png`](evidence/2026-11-08-post-remediation/f03-audit-mobile.png)
  - [`f04-f07-reports-desktop.png`](evidence/2026-11-08-post-remediation/f04-f07-reports-desktop.png)
  - [`f06-requisition-mobile.png`](evidence/2026-11-08-post-remediation/f06-requisition-mobile.png)
  - [`f08-master-data-validation.png`](evidence/2026-11-08-post-remediation/f08-master-data-validation.png)

### Final finding matrix

| Finding | Final status | Persona / route | Final evidence |
|---|---|---|---|
| F-01 — Supplier details | **Partially passed** | `demo.auditor`, `/suppliers` | The action opens, the read-only summary is present, and Save/status/input controls are absent. Status is localized. However, the primary-contact value has `scrollWidth 255 px` in a `171 px` grid cell and visibly overlaps the address on desktop and 390×844; see both F-01 screenshots. |
| F-02 — Requisition cost center | **Passed** | `demo.requester`, `REQ-2026-000001` detail → edit | Selected option is `TEC-INFRA — Infraestrutura e Cloud`, its stable value is non-empty, and no save was performed. Unit/component coverage still verifies that a no-change save preserves the ID/version. |
| F-03 — Audit UI | **Passed** | `demo.auditor`, `/audit`; `demo.requester`, direct `/audit` | Auditor menu/route listed real records and human actor names; requester had no menu item and direct routing showed **Acesso negado**. Mobile cards had no horizontal page overflow. |
| F-04 — Report validation | **Passed** | `demo.auditor`, `/reports` | Empty requisition report blocked locally with **Obrigatório para este tipo de relatório.** and the summary prompt; zero report POSTs; no raw `requisitionId` or trace ID in the primary message. |
| F-05 — Supplier status | **Passed** | `demo.auditor`, `/suppliers` | **Ativo** displayed; raw `Active` absent; badge retained visible meaning. |
| F-06 — Human-readable values | **Passed** | `demo.requester`, `REQ-2026-000001` | `17/12/2026`, localized BRL values and `Lucas Almeida (você)` appeared; no raw ISO instant in rendered main content. Desktop/mobile presentation passed without page-level horizontal overflow. |
| F-07 — Report selectors | **Passed** | `demo.auditor`, `/reports` | Requisition selected by `REQ-2026-000001` plus `TEC-INFRA`; UUID absent from option label. Budget report used controlled currency options including BRL and a business-label cost-center picker. |
| F-08 — Master-data validation | **Passed** | `demo.admin`, `/organization/departments` | Code `x` produced **O código deve ter de 2 a 20 caracteres.**, Save stayed disabled and zero department POSTs were sent. Drawer Escape restored focus to **Cadastrar**. |
| F-09 — Attachment copy | **Passed** | `demo.requester`, requisition editor | Copy explicitly says only name/type/size metadata are recorded and the file is not uploaded or stored; obsolete phase language is absent. |

### New regressions, residual risks and next actions

1. **New UI regression — supplier read-only field overlap (blocks full F-01 pass).** The contact value overflows its grid cell on desktop and overlaps the address; the same overlap is visible at 390×844 because the drawer retains two summary columns. Smallest action: add `min-width: 0` and safe wrapping such as `overflow-wrap: anywhere` to summary cells/values, and collapse the read-only summary to one column at the mobile breakpoint. Re-run the two F-01 screenshots, overflow measurement, axe and focus restoration.
2. **Intermittent accessibility signal — primary-button contrast.** One real-stack axe run after Audit data loaded reported `color-contrast: .bg-primary` (the enabled **Pesquisar** button); the next two equivalent runs reported no violation. Smallest action: add a deterministic light/system-theme axe case after the button is enabled, record computed foreground/background and ensure at least 4.5:1 by adjusting the light primary token or button foreground. Treat this as unresolved flakiness until that deterministic check is green.
3. **Aggregate .NET test-host wait.** Every suite passed, and `Template.Integration.Tests` passed 268/268 immediately in isolation, but the all-project command did not exit after parallel Testcontainers hosts completed. Smallest action: reproduce once with a binary log/diagnostic trace or run the solution filter with bounded project parallelism in CI; do not infer a product failure from this run.
4. F-09's underlying FIL-002 limitation is unchanged by design: requisition attachments remain metadata-only. Implementing stored bytes is a separate backend/API feature, not part of this validation.

Overall release-validation result: application build, backend contracts, container health and F-02–F-09 are green. F-01 remains partially passed because of the reproducible responsive text overlap, and the intermittent primary-button contrast signal remains a cross-cutting accessibility risk.

## Follow-up remediation and validation (2026-11-08)

This section executes `docs/testing/functional-ui-ux-follow-up-remediation-handoff.md` for the three items left open above (R-01 supplier summary overlap, R-02 intermittent primary-button contrast, R-03 aggregate .NET test-host wait). Earlier sections are unchanged.

### Scope and source identity

- Window: 2026-11-08 18:40 – 2026-11-09 00:10 `America/Sao_Paulo`.
- Validated revision: `bb2c15325c0accc42ea9c969a7a0b24a5cf8cc06` (`main`) plus the working-tree changes listed below. The uncommitted 2026-11-08 post-remediation section and its evidence, and the three untracked handoff documents, were preserved unchanged.
- Toolchain: Node `24.22.0` and pnpm `12.14.0` from temporary directories (the host PATH has Node 22.20 and no pnpm), .NET SDK `10.0.402` from `C:\Users\<user>\.dotnet` (the default `dotnet` is 10.0.401). No `engines`, lockfile or `global.json` change.
- Business data: the only writes were the expected login session/audit records and one requisition PDF report job (`demo.auditor`, `REQ-2026-000001`) to exercise the rebuilt Worker renderer. No supplier, requisition, master-data or seed record was changed; no volume was removed or recreated.

| File | Change | Service |
|---|---|---|
| `Client/Template.WebApp/src/app/presentation/features/data-pages.css` | `.readonly-summary` cells get `min-width: 0` and values `overflow-wrap: anywhere`; the summary collapses to one column at the existing `48rem` breakpoint. The shared `.summary-grid` used by eight other pages is unchanged. | `webapp` |
| `Client/Template.WebApp/src/app/presentation/features/suppliers.page.html` | The read-only `<dl>` gets the `readonly-summary` class (one line). | `webapp` |
| `Client/Template.WebApp/src/app/presentation/features/suppliers.page.spec.ts` | Vitest: long contact values render in the wrapping read-only summary. | test |
| `Client/Template.WebApp/e2e/master-data.spec.ts` | Playwright desktop 1365×768 (4 columns) and mobile 390×844 (1 column): long e-mail/phone/address wrap, per-cell and per-value `scrollWidth ≤ clientWidth`, no overlapping cells, no page/dialog overflow, no mutation controls, dialog axe clean, Escape restores focus to **Detalhes**. | test |
| `Client/Template.WebApp/e2e/audit.spec.ts` (new) | Deterministic contrast regression for **Pesquisar** in light, dark, system/light-host and system/dark-host preferences; waits for enable and the end of the button transition; asserts opaque background, opacity 1, ratio ≥ 4.5:1, full-page axe with no exclusion, hover ≥ 4.5:1 and a visible `:focus-visible` ring. | test |
| `Src/Infrastructure/Template.Infrastructure.Reporting/Pdf/PlaywrightPdfRenderer.cs` | Every Playwright call of a render is bounded (`NewPageAsync`, `RouteAsync`, context close, driver creation); the context is closed with a 5 s bound in `finally`. | `worker` |
| `Tests/Template.Functional.Tests/ApiEndpoints/v2/SourcingFunctionalTests.cs` | `T_SRC_013_b` sends the comparison's `closeLocalDate` instead of the UTC date (evening time-zone defect found by the final run). | test |
| `Tests/Template.Integration.Tests/Reporting/ReportRenderingAdapterTests.cs` | `T_RPT_005_l` simulates the crash with a bounded close and asserts recovery (transient failure at most once, then a valid render on a connected browser). | test |
| `docs/testing/functional-ui-ux-test-report-2026-11-05.md`, `docs/testing/evidence/2026-11-08-follow-up/` | This section and sanitized evidence. | docs |

`webapi` and `workflow-functions` were not rebuilt: neither references the changed client or `Template.Infrastructure.Reporting` (only `Template.Worker` and the integration tests do).

### R-01 — supplier read-only summary

Root cause: the summary grid cells kept the default `min-width: auto`, so the longest unbroken value (the contact e-mail) set the column's minimum width and painted over the neighbouring address; at 390×844 the drawer still used two columns.

Real stack, `demo.auditor`, `/suppliers` → keyboard **Detalhes** on the seeded supplier (`after-measurements.json`, `before-measurements.json`):

| Viewport | Before | After |
|---|---|---|
| 1365×768 | 4 × 170.75 px; **Contato principal** value `scrollWidth 255` in a `171 px` cell | 4 × 170.75 px; every value `scrollWidth = clientWidth = 171`; `overflow-wrap: anywhere` |
| 390×844 | 2 × 164.5 px; value `255` in `165 px` | 1 × 341 px; every value `341 = 341`, cells stacked without overlap |

In both runs and both viewports: page and dialog horizontal overflow 0, zero input/select/textarea/submit controls, status **Ativo**, focused dialog axe clean, Escape closed the drawer and restored focus to **Detalhes**, no console error and no HTTP 5xx. Mutation check of the mocked regression: without the class it fails with 176 px overflow and two mobile columns.

Screenshots: [before desktop](evidence/2026-11-08-follow-up/before-f01-supplier-readonly-desktop.png), [before mobile](evidence/2026-11-08-follow-up/before-f01-supplier-readonly-mobile.png), [after desktop](evidence/2026-11-08-follow-up/after-f01-supplier-readonly-desktop.png), [after mobile](evidence/2026-11-08-follow-up/after-f01-supplier-readonly-mobile.png).

### R-02 — primary-button contrast

Root cause: timing, not tokens. `hlmBtn` animates every property for 150 ms (`transition-all`) and the disabled state is `opacity: 0.5`. While the Audit search loads, **Pesquisar** is disabled; an axe scan taken in the first frames after it is enabled measures the half-faded blend. No theme token was changed and no rule or selector was excluded.

Real stack, `demo.auditor`, `/audit`, three repetitions per preference with in-app navigation (`after-measurements.json`; computed colors converted to sRGB by the browser):

| Preference / host | Settled enabled ratio | Hover | Immediately after enable | Settled full-page axe |
|---|---|---|---|---|
| Light | **6.13:1** (`oklch(0.5 0.19 268)` on `oklch(0.99 0 0)`) | 4.99:1 | opacity 0.5 → 2.24:1 (transient) | 0 violations in 3/3 |
| Dark | **7.00:1** (`oklch(0.17 0.03 268)` on `oklch(0.7 0.14 268)`) | 5.93:1 | opacity 0.5 → 2.67:1 (transient) | 0 in 3/3 |
| System, light host | **6.13:1**, `data-theme=light` | 4.99:1 | 0.5–0.52 → 2.24–2.30:1 | 0 in 3/3 |
| System, dark host | **7.00:1**, `data-theme=dark` | 5.93:1 | 0.5 → 2.67:1 | 0 in 3/3 |

Across both the before and after real-stack runs, the immediate (mid-transition) `color-contrast` scan reported a violation in 3 of 18 samples and the settled scan in 0 of 24, which reproduces the earlier intermittent signal and confirms its cause. The disabled state itself (opacity 0.5) is an inactive control, exempt from WCAG 1.4.3, and its label remains legible. The new deterministic Playwright regression (`e2e/audit.spec.ts`, four preferences) waits for `getAnimations().length === 0` and `opacity === 1` before measuring; it passed in the full mocked suite.

### R-03 — aggregate .NET test-host wait

Reproduction and diagnostics (summaries in `evidence/2026-11-08-follow-up/r03/`):

| Run | Command | Exit / duration | Result |
|---|---|---|---|
| Default 1 | `dotnet test TemplateCleanArchitecture.App.slnf --no-build --blame-hang-timeout 15m --blame-hang-dump-type none --diag … --logger trx` | not captured (wrapper bug, fixed) / 18.5 min | 1,769/1,769 passed; no host lingered. |
| Sequential | same + `-m:1` | 1 / 30.3 min | 1,768/1,769: every project passed except NFR-PERF-5 (Outbox drain 85.9 s against the 60 s budget; passed in every other run). |
| Default 2 | same as Default 1 | 1 / 35.7 min | **Hang reproduced**: `Template.Integration.Tests` stopped at 261 of 268 and the 15 min blame-hang collector fired on `T_RPT_005_l_BrowserCrash_Timeout_AndLaunchFailure_AreTransient_AndTheRendererRecovers`. Two wall-clock Functional tests also failed under the 13-project load (`EleventhRefreshFromTheSameAddress_Is429` took 3 min 28 s against a 60 s rate-limit window; `Refresh_ReplayInsideGrace_Is409` took 50 s against the 10 s grace window); both pass sequentially. |

Isolation: `T_RPT_005_l` alone in a loop under CPU load hung at iteration 9 (minidump) and again at iteration 5 (full dump). The full dump's only pending async chain was `ReportRenderingAdapterTests.T_RPT_005_l` → `PlaywrightPdfRenderer.RenderAsync` → `BrowserContext.NewPageAsync` → `Connection.InnerSendMessageToServerAsync` → `StdIOTransport.GetResponseAsync` (`r03/hang-dump-async-chain.txt`): when Chromium closes while a page is being created, the Playwright driver never answers, and `NewPageAsync` (also `RouteAsync`, the context dispose and `Playwright.CreateAsync`) had no bound. This is a product defect, not only a test one: in the Worker the same race would hold a render slot forever instead of failing the attempt as transient (RPT-005).

Fix: bound every renderer call with `RenderTimeout`/`LaunchTimeout` and close the context with a 5 s bound. After the fix the test's own `Browser.CloseAsync()` (its crash simulator) was the next unbounded call: once bounded, it failed visibly at 30 s in 1 of 2 iterations, because Chromium sometimes does not finish closing on request. The test now bounds the close at 10 s and asserts the renderer's recovery (the attempt that meets the crashed browser may fail as transient once; the retry renders a valid PDF on a connected, relaunched browser). Result: 40/40 iterations under the same load, 5 s–96 s, no hang dump (`r03/stress-fixed2-iterations.txt`). The rebuilt Worker rendered a real requisition PDF in 6.5 s (HTTP 200, `application/pdf`, `%PDF-`, 44,043 bytes; `after-worker-pdf.json`).

Final aggregates with the fix (same command; TRX, console and VSTest diagnostics kept outside the repository):

| Run | Command | Exit / duration | Result |
|---|---|---|---|
| Final `-m:1` | aggregate + `-m:1`, 60 min watchdog | killed by the watchdog at 60 min (exit 1) | No hang dump; 11 projects completed (Unit 839, Integration 268 incl. `T_RPT_005_l`, Architecture 77, PostgreSQL 78, RabbitMQ 30, Elasticsearch 13, Redis 23, Azurite 6, Worker 5, WorkflowFunctions 26, WorkflowFunctions.Integration 7, all passed). Functional 387/392 took 42 min instead of under 2 min: four wall-clock tests failed on a slowed host (`T_PUR_008_T_PUR_010` ran 19 min and got a numeric Problem Details `status` once its access token had expired; the two rate-limit tests and the refresh-grace test missed their windows) plus `T_SRC_013_b` (below). Performance was still running when the watchdog fired. |
| Final default | aggregate, 45 min watchdog | killed by the watchdog at 45 min (exit 1) | 11 projects completed; 1 failure: `T_NFR_PERF_6_a` 10,000-row workbook 34.9 s against 30 s under the 13-project parallel load (7.7 s in the Phase 33 run and passed in every other run). Functional had not finished when the watchdog fired (no hang-dump trigger, no TRX). |
| Functional alone | `dotnet test Tests/Template.Functional.Tests/… --no-build -m:1` at 23:41 | 1 / 1.8 min | 391/392: **`T_SRC_013_b` is a time-of-day test defect**, independent of this change: it sent the UTC date (`2026-11-09`) as the manual rate date, while CALC-004 compares it with the close date in the organization time zone (`2026-11-08` in `America/Sao_Paulo`), so it fails every evening from 21:00 local. Fixed in the test (it now sends the `closeLocalDate` the comparison returns); no production change. |
| **Final sequential gate** | every project one at a time per `AGENTS.md`: `dotnet test Tests/<project>/<project>.csproj --no-build --verbosity minimal --maxcpucount:1 --blame-hang-timeout 15m` (23:45–00:03) | **0 for all 13 / 18.4 min** | **1,769/1,769 passed**: Architecture 77, Unit 839, Integration 268, Functional 392 (run at 23:47, inside the window where `T_SRC_013_b` had failed), PostgreSQL 78, RabbitMQ 30, Elasticsearch 13, Redis 23, Azurite 6, Worker 5, WorkflowFunctions 26, WorkflowFunctions.Integration 7, Performance 5. No hang, no lingering host (`r03/final-sequential-per-project-summary.txt`). |

The renderer wait (R-03) did not recur in any run after the fix. Running all 13 projects at once (or through one `dotnet test` of the solution filter) on this workstation still slows the host enough to break wall-clock budgets in Functional and Performance; those are load effects, visible as assertion failures rather than waits, and the supported gate remains the sequential per-project run.

### Automated results

| Command | Result |
|---|---|
| `pnpm install --frozen-lockfile` | Passed; no lockfile change. |
| `pnpm validate` | Passed in 38.7 s: Prettier, ESLint, typecheck, Vitest **37 files / 133 tests**, dependency-cruiser **225 modules / 548 dependencies**, **5/5** mutations rejected, **211** generated files matched, production build without a budget warning (the CSS stays under the 4 kB `anyComponentStyle` budget). |
| `pnpm exec playwright test --workers=1` | **27/27** passed in 1.8 min (21 previous + 2 F-01 + 4 contrast). Run against a stopped Compose `webapp` because both bind `127.0.0.1:4200`. |
| `pnpm audit:licenses` | 458 packages OK. |
| `pnpm audit:security` | Exit 0; 2 low and 3 moderate advisories, unchanged and below the high/critical policy. |
| `dotnet restore TemplateCleanArchitecture.App.slnf` | Passed. |
| `dotnet build TemplateCleanArchitecture.App.slnf --no-restore` | Passed, 0 errors; only the existing `CS8604` at `Tests/Template.WorkflowFunctions.Tests/HostBoundaryTests.cs:106`. |
| `dotnet test Tests/Template.Integration.Tests/… --no-build` (isolated) | 268/268 in 42.5 s before the fix. |
| `T_RPT_005_l` stress (6 CPU-bound jobs, 2 min blame-hang, full dump) | Before: hang at iterations 9 and 5. After: 40/40. |
| Per-project sequential gate (13 projects) | **1,769/1,769** passed in 18.4 min; aggregate runs in R-03. |

### Docker rebuild and health

`docker compose config --quiet` passed.

| Service | Pre-rebuild image ID / created | Post-rebuild image ID / created | Command |
|---|---|---|---|
| `webapp` | `863da586f55c` / `2026-11-06T16:07:50Z` | `56bc1cddef33` / `2026-11-08T23:44:08Z` | `docker compose build --pull webapp`, `docker compose up -d --no-deps webapp` |
| `worker` | `5d0ff4a48086` / `2026-11-08T19:44:16Z` | `05b0a1ac002c` / `2026-11-09T00:54:16Z` | `docker compose build --pull worker`, `docker compose up -d --no-deps worker` |

Health after the rebuilds: `webapp`, `worker`, `workflow-functions`, PostgreSQL, Redis, RabbitMQ, Elasticsearch, Kibana, Redis Insight and both Azurite services `healthy`; `webapi` running with `/health/live`, `/health/ready` (database, cache, Elasticsearch) and `/health` HTTP 200; WebApp `:4200` HTTP 200. Error scan of the last 30 minutes of `webapi`, `worker`, `workflow-functions` and `webapp` logs (`ERR`/`FTL`/`fail:`/unhandled exceptions/5xx/asset 404): no match. Volumes untouched.

### Real-stack retest of F-01 to F-09

Containerized application at `http://localhost:4200`, personas `demo.auditor`, `demo.requester` and `demo.admin` (password read from `.env` inside the process, never printed, removed from the environment afterwards). Every check recorded console errors, HTTP 5xx and non-GET API requests other than session calls (`after-smoke.json`): none in any run.

| Finding | Final status | Evidence |
|---|---|---|
| F-01 — Supplier details | **Passed** | R-01 above: wraps in 4 desktop columns and 1 mobile column, no overflow or overlap, read-only, **Ativo**, axe clean, keyboard open and Escape focus restoration. |
| F-02 — Requisition cost center | **Passed** | `demo.requester`, `REQ-2026-000001` → **Editar**: `TEC-INFRA — Infraestrutura e Cloud` selected with a non-empty stable value; nothing saved. |
| F-03 — Audit UI | **Passed** | Auditor menu → **Auditoria** lists records with human actor names, no raw ISO instant, full-page axe clean; requester has no menu entry and direct `/audit` lands on `/forbidden` (**Acesso negado**) on desktop and mobile. |
| F-04 — Report validation | **Passed** | Empty requisition report blocked locally with **Obrigatório para este tipo de relatório.**; zero report POSTs in that check; axe clean. |
| F-05 — Supplier status | **Passed** | **Ativo** shown; raw `Active` absent from the list. |
| F-06 — Human-readable values | **Passed** | Requisition detail shows `dd/mm/yyyy` dates and `(você)`, no raw ISO instant; page overflow 0 on desktop and 390×844. |
| F-07 — Report selectors | **Passed** | Requisition options are business labels (`REQ-2026-000001 — TEC-INFRA · Rascunho`), no UUID; four localized report types. |
| F-08 — Master-data validation | **Passed** | `demo.admin`, **Departamentos** → **Cadastrar**, code `x`: **O código deve ter de 2 a 20 caracteres.**, **Salvar** disabled, zero POSTs, Escape restores focus to **Cadastrar**. |
| F-09 — Attachment copy | **Passed** | Editor notice: only name/type/size metadata are recorded; the file is not uploaded or stored. |

Also verified: login and logout (**Sair** returns to the login form), main navigation through the menu, and the mocked suite's keyboard, focus and axe cases.

Screenshots (new; earlier evidence untouched): [F-01 desktop](evidence/2026-11-08-follow-up/after-f01-supplier-readonly-desktop.png), [F-01 mobile](evidence/2026-11-08-follow-up/after-f01-supplier-readonly-mobile.png), [F-02/F-09 editor](evidence/2026-11-08-follow-up/after-f02-f09-requisition-editor.png), [F-04/F-07 reports](evidence/2026-11-08-follow-up/after-f04-f07-reports-desktop.png), [F-06 desktop](evidence/2026-11-08-follow-up/after-f06-requisition-desktop.png), [F-06 mobile](evidence/2026-11-08-follow-up/after-f06-requisition-mobile.png), [F-08](evidence/2026-11-08-follow-up/after-f08-master-data-validation.png).

### Remaining blockers, risks and next actions

- No blocker remains for R-01, R-02 or R-03; F-01 to F-09 pass on the rebuilt containers.
- **Aggregate parallel load (risk).** A single `dotnet test` of the whole solution filter on this workstation exceeds wall-clock budgets: Functional rate-limit/refresh-grace windows and the expiring access token in a 19-minute purchasing test, `T_NFR_PERF_6_a` (34.9 s against 30 s) and earlier NFR-PERF-5 (85.9 s against 60 s). They pass in the sequential per-project gate and fail visibly (never hang). Next action: keep the per-project sequential command as the gate; optionally give the time-sensitive Functional classes their own xUnit collection or larger windows in a separate change.
- **Test time-zone defect fixed.** `T_SRC_013_b` failed every evening from 21:00 `America/Sao_Paulo` (UTC date ahead of the organization date); fixed in `Tests/Template.Functional.Tests/ApiEndpoints/v2/SourcingFunctionalTests.cs`. Other functional helpers that use `DateTime.UtcNow` dates (for example `validUntil` 60 days ahead) were not affected in the evening run.
- **Chromium close (risk).** Chromium sometimes does not exit when the driver asks it to close; the renderer and the test now bound that call, so it costs one transient attempt instead of a stuck slot.
- **Cosmetic follow-up (not in scope).** The supplier address renders `Avenida Paulista , 1000` (a space before the comma from template whitespace); pre-existing, not changed here.
- **Not rerun.** FIL-002 (attachment byte upload) remains out of product scope as before; the VSIX gate, the infrastructure rehearsals and `compose-baseline.ps1` were not run (no scope change); `webapi` and `workflow-functions` images were not rebuilt (unaffected).
