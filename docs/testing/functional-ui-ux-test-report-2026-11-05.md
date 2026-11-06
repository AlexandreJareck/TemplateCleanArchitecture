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
