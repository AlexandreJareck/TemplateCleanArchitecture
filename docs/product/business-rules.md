# Business-rule catalog

Every cross-module rule has a stable ID. Implementation code, API Problem Details (`ruleId` extension), audit records (`RuleIds`) and tests reference these IDs. IDs are never reused or renumbered; a retired rule is marked `Retired` with the deciding entry in [decision-log.md](decision-log.md).

## Classification

| Code | Class | Lives in | Example |
|---|---|---|---|
| INV | Invariant | Domain aggregate or value object | A posted receipt is never edited |
| POL | Configurable business policy | Versioned policy record read by an Application policy service | Competition threshold 5,000.00 BRL |
| AUTH | Authorization | Permission policy at the API boundary plus resource-ownership check in the use case | Only the requester edits a draft |
| WF | Workflow | Aggregate transition method plus Application orchestration (Worker/Durable only coordinate) | Rejection releases the reservation |
| CALC | Calculation | Shared `CalculationRules` in Domain | Rounding and totals |
| VAL | Input validation | FluentValidation validators in Application | Quantity has at most 3 decimals |

Column legend: **Policy v1** = seeded value of policy version 1 (configuration data, B23-10); **Exception** = the only allowed waiver (otherwise "none"); **Audit** = audit action written in the same transaction (AUD-001); **Failure** = stable Problem Details `code` and HTTP status ([api-guidelines.md](api-guidelines.md#5-problem-details-and-error-codes)); **Tests** = planned test IDs (level: U domain unit, H handler, P PostgreSQL, F functional HTTP, W Worker/RabbitMQ, D Durable, E browser E2E, A architecture). The owner module is the section heading.

## Policy versions (version 1 seeds)

Policies are stored as versioned records. A version is immutable once referenced by any instance (APR-017, POL-001). Each in-flight aggregate stores the `PolicyReference` it used.

| Policy | Values (v1) | Pinned by |
|---|---|---|
| `SessionPolicy` | access token 15 min; refresh idle 12 h; refresh absolute 7 days; rotation grace 10 s; max 5 active families per user; login rate limit 10 requests/min per client IP + user name | Session family at creation |
| `RequisitionPolicy` | max 100 lines; needed-by ≥ submission local date; expiry grace 30 days after needed-by | Requisition at submission |
| `SourcingPolicy` | competition threshold 5,000.00 BRL (estimated total in base currency, exclusive); min invitations above threshold 3, otherwise 1; min valid quotes above threshold 3; response window 24 h–30 days; max deadline extension 30 days per extension | RFQ at creation |
| `ApprovalPolicy` | L1 > 0.00 BRL → cost-center manager; L2 > 10,000.00 BRL → department head; L3 > 100,000.00 BRL → `FinanceApprover` group; step duration 120 h; reminder after 72 h (B23-11); escalation assignee `ProcurementManager` group; max escalations per step 1 | Approval instance at start |
| `ReceivingPolicy` | over-receipt tolerance 0 % (D23-18) | Receipt at posting |
| `FilePolicy` | max 10 MB per file; max 10 attachments per owner record; allowed media types `application/pdf`, `image/png`, `image/jpeg`, `text/plain`, `text/csv`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`; pending upload expiry 24 h | Upload |
| `ReportPolicy` | output expiry 7 days; max 3 attempts; max 50,000 rows per XLSX; max 500 PDF pages | Report job at request |

- **POL-001 (INV, all phases):** a policy version is immutable once referenced; a change creates a new version with `EffectiveFromUtc ≥ now`; in-flight instances keep their pinned version. Audit `policy.version.created`. Tests: `T-POL-001-a` (U) referenced version rejects mutation; `T-POL-001-b` (P) new version does not change an active approval instance.

## Waiver catalog

A waiver is a `WaiverDecision` record ([domain-model.md](domain-model.md#2-shared-kernel-domain)): rule ID, waiver type, actor, permission used, reason (10–500 characters since Phase 29, D29-3), UTC timestamp, scope, audit action `waiver.recorded`. There is no boolean bypass and no administrator override (B23-9).

| Waiver type | Waives | Permission | Default holder |
|---|---|---|---|
| `LowValueDirectPurchase` | SRC-005 competition when estimated total ≤ threshold, via `Direct` method (SRC-006) | `sourcing.waivers.low-value` | Buyer |
| `DirectPurchase` | SRC-005 competition above threshold, via `Direct` method (SRC-006) | `sourcing.waivers.direct-purchase` | ProcurementManager |
| `InsufficientQuotations` | SRC-017 minimum valid quotes at award | `sourcing.waivers.insufficient-quotations` | ProcurementManager |
| `NonLeadingSelection` | SRC-018 award to a quote that is not the best-ranked awardable quote | `sourcing.waivers.non-leading` | ProcurementManager |
| `ManualExchangeRate` | SRC-013 automatic PTAX rate | `sourcing.rates.manual` | ProcurementManager |
| `ShortClose` | PUR-009 full-receipt completion | `purchasing.orders.short-close` | ProcurementManager |

## Calculation rules (CALC)

Owner: shared kernel (Domain `CalculationRules`). Phase 27 introduces CALC-001..003; Phase 29 CALC-004, 005, 007; Phase 32 CALC-006, 008, 009.

| ID | Rule | Failure | Tests |
|---|---|---|---|
| CALC-001 | `Money` operations require equal currency. Monetary results are rounded to the currency minor units (BRL, USD, EUR: 2) with `MidpointRounding.AwayFromZero`, only at the boundaries named below. Unit prices keep up to 4 decimals; quantities up to 3 decimals; exchange rates up to 6 decimals. Comparison is exact decimal comparison after rounding. | `money.currency-mismatch` 422 (programming error surfaced as 500 if reached inside Domain) | `T-CALC-001-a` (U) BRL+USD throws; `-b` (U) 0.125 → 0.13, 0.135 → 0.14, −0.125 → −0.13; `-c` (U) 4-dp unit price preserved |
| CALC-002 | Line total = round(quantity × unit price). Requisition estimated total = Σ line totals. Quote and PO total = Σ line totals + freight − discount; `TaxesIncluded` is an explicit quoted amount already contained in prices and is informational (no tax engine). Totals are always recomputed by the server; client-sent totals are ignored. | — | `-a` (U) 3 × 33.3333 = 100.00; `-b` (U) freight/discount; `-c` (F) client total ignored |
| CALC-003 | Allocation available = amount − Σ amounts of reservations in `Reserved` or `Committed`. Never negative (BUD-005). | — | `-a` (U) mixed reservation states |
| CALC-004 | Normalization to base currency: normalized = round(amount × rate), rate = base units per foreign unit from an `ExchangeRateSnapshot`; same currency uses rate 1 without a snapshot. Rate source `BcbPtax` uses the PTAX selling rate for the RFQ close local date or the latest earlier published date (SP-5); `Manual` requires the `ManualExchangeRate` waiver. A missing rate makes the comparison `Incomplete`; it is never guessed. | `sourcing.rate-unavailable` (comparison flag, not an HTTP error) | `-a` (U) USD 1,000.00 × 5.4321 = 5,432.10 BRL; `-b` (H) unavailable rate → Incomplete; `-c` (P) snapshot persisted and reused |
| CALC-005 | Ranking of valid quotes: ascending normalized total; ties broken by fewer delivery days, then earlier `ReceivedAtUtc`, then supplier legal name (ordinal), then supplier ID (D29-5). Ranking is advisory; selection is a human decision. | — | `-a` (U) each tie-breaker level; `-b` (U) deterministic across input order |
| CALC-006 | PO lines, prices, freight, discount and total are copied from the awarded quote revision; PO total equals the award total exactly. | — | `-a` (U) |
| CALC-007 | Award amount in requisition currency: equal to awarded total when currencies match; otherwise round(round(awarded × rate quote→base) ÷ rate requisition→base) using the comparison snapshots (base→base = 1). | — | `-a` (U) BRL requisition, USD award; `-b` (U) EUR requisition, USD award |
| CALC-008 | Short-close release = round(Σ (ordered − accepted) × unit price) in PO currency, converted to requisition currency with the award rates (CALC-007), capped at the committed amount. | — | `-a` (U) |
| CALC-009 | Receipt cap per line = ordered × (1 + tolerance % ÷ 100), truncated to the unit's allowed decimals. | — | `-a` (U) 0 % and 5 % |

## Access (SEC)

Phase 24. Owner: Access module (Infrastructure.Identity implements; Application owns contracts).

| ID | Rule | Class | Policy v1 | Permission | Exception | Audit | Failure | Tests |
|---|---|---|---|---|---|---|---|---|
| SEC-001 | Login (user name + password) creates a session family and returns a short-lived access token in the body and a refresh token in a cookie (D23-11). Existing `authenticate`/`start` routes keep their current behavior. | WF | access 15 min | anonymous | none | `security.session.created` | `auth.invalid-credentials` 401 | `-a` (F) login sets cookie attributes; `-b` (F) legacy routes unchanged |
| SEC-002 | Refresh tokens are 256-bit random values, stored only as SHA-256 hashes, rotated on every use, expiring by idle and absolute limits. | INV | idle 12 h, absolute 7 d | cookie + CSRF | none | `security.session.refreshed` | `auth.session-expired` 401 | `-a` (P) hash only; `-b` (F) rotation; `-c` (F) idle/absolute expiry |
| SEC-003 | Presenting a rotated refresh token after the grace window revokes the whole family. | INV | grace 10 s | — | none | `security.session.reuse-detected` | `auth.session-revoked` 401 | `-a` (P) reuse revokes family; `-b` (F) |
| SEC-004 | A refresh racing a rotation within the grace window returns a deterministic `409` without revoking; the client retries once with the current cookie. | WF | grace 10 s | — | none | none | `auth.refresh-in-progress` 409 | `-a` (P) parallel refresh; `-b` (E) two tabs |
| SEC-005 | Cookie-authenticated endpoints (refresh, logout) require a valid CSRF token (double-submit, `XSRF-TOKEN` cookie + `X-XSRF-TOKEN` header bound to the session family) and an allowed `Origin`. | INV | — | — | none | `security.csrf.rejected` | `auth.csrf-invalid` 403 | `-a` (F) missing/invalid token; `-b` (F) foreign Origin |
| SEC-006 | Logout revokes the current family; revoke-all revokes every family of the user; password change and security-stamp change revoke every family. | WF | — | authenticated | none | `security.session.revoked` | — | `-a` (F) each path |
| SEC-007 | Authentication and refresh endpoints are rate-limited. | POL | 10/min per IP + user name | — | none | `security.rate-limited` | `auth.rate-limited` 429 + `Retry-After` | `-a` (F) 11th request 429; `-b` (F) other endpoints unaffected |
| SEC-008 | Endpoints authorize **permissions**; roles only group permissions. No role, including `Administrator`, bypasses a business rule (B23-9). | AUTH | — | per endpoint | none | `security.forbidden` (sampled) | `auth.forbidden` 403 | `-a` (A) controllers use permission policies only; `-b` (F) matrix in [permissions.md](permissions.md) |
| SEC-009 | `GET /api/v2/account/me` returns user ID, user name, display name, roles and effective permissions only. | AUTH | — | authenticated | none | none | 401 | `-a` (F) shape and no extra claims |
| SEC-010 | Max active session families per user; creating one more revokes the oldest. | POL | 5 | — | none | `security.session.revoked` | — | `-a` (P) |

## Audit (AUD)

Phase 24 creates the audit store; every later phase writes to it.

| ID | Rule | Class | Permission | Audit | Failure | Tests |
|---|---|---|---|---|---|---|
| AUD-001 | Every state transition, waiver, permission-protected change and security event writes an `AuditRecord` in the same transaction as the change. | INV | — | itself | rollback of the whole change | `-a` (P) transition + audit atomic; `-b` (H) each command emits audit |
| AUD-002 | Audit records are append-only; no API, use case or repository updates or deletes them. | INV | — | — | — | `-a` (A) no update/delete method on the audit port; `-b` (P) |
| AUD-003 | Audit `Details` contain allow-listed fields only: never passwords, tokens, cookies, hashes, attachment content, full addresses or free-text line descriptions. | INV | — | — | — | `-a` (H) redaction allow-list |
| AUD-004 | A resource timeline is readable by actors who may read the resource; the global audit query requires `audit.read`. | AUTH | `audit.read` | — | 403 | `-a` (F) |

## Organization (ORG)

Phase 25.

| ID | Rule | Class | Permission | Exception | Audit | Failure | Tests |
|---|---|---|---|---|---|---|---|
| ORG-001 | Department and cost-center codes are unique (case-insensitive), immutable and match `^[A-Z0-9-]{2,20}$`. | INV/VAL | `organization.manage` | none | `organization.department.created` / `organization.cost-center.created` | `organization.code-duplicate` 409; `validation` 400 | `-a` (U) immutability; `-b` (P) unique index; `-c` (F) |
| ORG-002 | A cost center belongs to exactly one department, fixed at creation. | INV | `organization.manage` | none | — | 400 | `-a` (U) |
| ORG-003 | Inactive departments and cost centers keep history and reject new requisitions, allocations and manager assignments; a department can be deactivated only when all its cost centers are inactive. | INV/WF | `organization.manage` | none | `organization.*.deactivated` (reason required) | `organization.inactive` 422; `organization.department-has-active-cost-centers` 409 | `-a` (U); `-b` (H) requisition on inactive cost center |
| ORG-004 | Base currency and time zone cannot change after the first budget allocation exists. | INV | `organization.settings.manage` | none | `organization.settings.changed` | `organization.settings-locked` 409 | `-a` (H) |
| ORG-005 | Referenced master data (department, cost center, supplier, catalog item, category, unit) is never hard-deleted; it is deactivated. | INV | — | none | — | `master-data.in-use` 409 | `-a` (P) delete blocked |
| ORG-006 | Cost-center manager and department head must be active users; the same user may manage several cost centers. | VAL | `organization.manage` | none | — | 400 | `-a` (H) |

## Suppliers (SUP)

Phase 25.

| ID | Rule | Class | Permission | Exception | Audit | Failure | Tests |
|---|---|---|---|---|---|---|---|
| SUP-001 | Tax identifier is normalized (digits only for BR); Brazilian CNPJ passes structural check-digit validation; normalized tax ID is unique per country. Structural validation is not government verification. | VAL/INV | `suppliers.manage` | none | `supplier.created` | `supplier.tax-id-invalid` 400; `supplier.tax-id-duplicate` 409 | `-a` (U) check digits; `-b` (P) uniqueness |
| SUP-002 | Activation (`Draft → Active`) requires legal name, tax ID, at least one contact with email, one address and one category. | WF | `suppliers.status.change` | none | `supplier.activated` | `supplier.activation-incomplete` 422 | `-a` (U) each missing field |
| SUP-003 | Only `Active` suppliers may be newly invited (SRC-003), awarded (SRC-016) or used to issue a PO (PUR-003) (D23-16). | INV | — | none | — | `supplier.not-eligible` 422 | `-a` (U) eligibility per status; `-b` (F) consumer endpoints |
| SUP-004 | Suspension or inactivation never changes historical snapshots (invitations, quotes, POs, receipts); issued POs may still be received. Suspension invalidates pending approvals that award the supplier (APR-012). | INV/WF | — | none | `supplier.suspended` | — | `-a` (P) snapshots unchanged; `-b` (H) approval invalidated |
| SUP-005 | Suspend, inactivate and reactivate require reason. | AUTH/WF | `suppliers.status.change` | none | `supplier.status-changed` | `reason-required` 400 | `-a` (F) |
| SUP-006 | Contacts and addresses are personal/commercial data: readable only with `suppliers.read`; never logged ([non-functional-requirements.md](non-functional-requirements.md#9-privacy-and-data-classification)). | AUTH | `suppliers.read` | none | — | 403 | `-a` (F) |

## Catalog (CAT)

Phase 25.

| ID | Rule | Class | Permission | Audit | Failure | Tests |
|---|---|---|---|---|---|---|
| CAT-001 | Catalog item, category and unit codes are unique and immutable. | INV | `catalog.manage` | `catalog.*.created` | `catalog.code-duplicate` 409 | `-a` (P) |
| CAT-002 | New catalog items reference an active category and an active unit; deactivation keeps history and blocks new requisition lines. | INV | `catalog.manage` | `catalog.*.deactivated` | `catalog.inactive` 422 | `-a` (U); `-b` (H) |
| CAT-003 | A unit's `AllowedDecimals` (0–3) bounds every quantity expressed in it. | VAL | — | — | 400 | `-a` (U) |
| CAT-004 | `ReferencePrice` is advisory; it pre-fills a draft line but is never an authoritative price. | WF | — | — | — | `-a` (H) |

## Integrations (INT)

Phases 25 and 28.

| ID | Rule | Class | Policy v1 | Failure | Tests |
|---|---|---|---|---|---|
| INT-001 | External providers (BrasilAPI, BCB PTAX) are optional: timeout 5 s, at most 2 retries for idempotent GETs on transient failures, circuit breaker; outage, 429, 5xx or schema mismatch return an explicit "unavailable" result and never block manual entry. | POL | timeouts/retries as stated | `integration.provider-unavailable` 503 on the enrichment endpoint only | `-a` (fake server) timeout, 429, 5xx, malformed, cancellation |
| INT-002 | Accepted enrichment is persisted as a snapshot with provider, retrieval instant and accepting user; nothing is read live afterwards. | INV | — | — | `-a` (H) |
| INT-003 | Integration events are delivered at least once; every consumer is idempotent through an Inbox record keyed by (consumer name, event ID). | INV | — | — | `-a` (W) duplicate delivery executes once |
| INT-004 | Integration events are written to the Outbox in the same transaction as the business change; no critical event is published directly after commit. | INV | — | — | `-a` (P) rollback leaves no outbox row; `-b` (W) crash after commit still publishes |

## Budgeting (BUD)

Phase 27 (BUD-009 in Phase 29, BUD-010/011 commit/release paths in Phases 31–32).

| ID | Rule | Class | Policy v1 | Permission | Exception | Audit | Failure | Tests |
|---|---|---|---|---|---|---|---|---|
| BUD-001 | Fiscal periods have unique codes and non-overlapping local-date ranges; transitions `Planned → Open → Closed`; `Closed` is final. | INV | — | `budget.periods.manage` | none | `budget.period.*` | `budget.period-overlap` 409; `invalid-transition` 409 | `-a` (U); `-b` (P) overlap |
| BUD-002 | An allocation is unique per (fiscal period, cost center, currency), uses a supported currency, amount > 0, and requires an active cost center. | INV/VAL | — | `budget.allocations.manage` | none | `budget.allocation.created` | `budget.allocation-duplicate` 409 | `-a` (P) unique index |
| BUD-003 | Allocation amount changes require reason; a decrease cannot make available budget negative; history is appended. | INV | — | `budget.allocations.manage` | none | `budget.allocation.amount-changed` | `budget.below-reserved` 422 | `-a` (U) |
| BUD-004 | A reservation uses the allocation of the requisition's cost center and currency in the `Open` period containing the submission local date. No cross-currency reservation (D23-17). | INV | — | (system, inside REQ-005) | none | — | `budget.no-open-allocation` 422 | `-a` (U); `-b` (H) closed period; `-c` (H) currency mismatch |
| BUD-005 | Reserve amount = requisition estimated total and must be ≤ available. No silent overspend; no overspend waiver in v1. | INV | — | — | none | `budget.reservation.created` | `budget.insufficient` 422 | `-a` (U) boundary equal/above; `-b` (F) |
| BUD-006 | Reservation changes on one allocation are serialized (row lock on the allocation in the reserving transaction); a competing submission observes the reduced availability and fails with BUD-005, never with an over-reservation. | INV | — | — | none | — | `budget.insufficient` 422 | `-a` (P) 20 parallel submissions on a near-empty allocation; `-b` (E) SC-08 |
| BUD-007 | Reserve, adjust, commit and release are idempotent per (requisition ID, operation, requisition revision, award ID when present); release happens exactly once and records a reason code (`Withdrawn`, `Cancelled`, `Rejected`, `Expired`, `ShortClosed`, `OrderCancelled`). | INV | — | — | none | `budget.reservation.released` | — | `-a` (U) double release no-op; `-b` (W) duplicate message |
| BUD-008 | A requisition owns at most one active (`Reserved` or `Committed`) reservation. | INV | — | — | none | — | `budget.reservation-exists` 409 | `-a` (P) partial unique index |
| BUD-009 | On award submission the reservation is adjusted to the award amount in requisition currency (CALC-007): an increase must fit available budget (BUD-005); a decrease returns the difference. | INV/WF | — | — | none | `budget.reservation.adjusted` | `budget.insufficient` 422 | `-a` (U) up/down; `-b` (P) concurrent increase |
| BUD-010 | Final approval changes the reservation to `Committed` (D23-4) in the same transaction as APR-013. | WF | — | — | none | `budget.reservation.committed` | — | `-a` (P) atomic with approval |
| BUD-011 | A period can be closed only when it has no `Reserved` reservation; `Committed` reservations remain as history. | WF | — | `budget.periods.manage` | none | `budget.period.closed` | `budget.period-has-open-reservations` 409 | `-a` (H) |
| BUD-012 | Budget availability is visible to `budget.read` holders; a requester sees only the availability of the cost center selected in their own draft. | AUTH | — | `budget.read` / own draft | none | — | 403 | `-a` (F) |

## Requisitions (REQ)

Phase 27 (REQ-009 scheduler in Phase 33; REQ-008 cancellation of RFQ/approval side effects extended in Phases 29 and 31).

| ID | Rule | Class | Policy v1 | Permission | Exception | Audit | Failure | Tests |
|---|---|---|---|---|---|---|---|---|
| REQ-001 | Only the requester edits or discards their `Draft`; a proxy needs `requisitions.edit-any` and is recorded as actor. | AUTH | — | `requisitions.create` / `requisitions.edit-any` | none | `requisition.draft.updated` | `auth.forbidden` 403 | `-a` (F) other user 403; `-b` (F) proxy allowed and audited |
| REQ-002 | Lines: 1–100 lines; quantity > 0 within unit decimals (CAT-003); estimated unit price > 0 with ≤ 4 decimals; line currency = requisition currency; description 3–500 chars. | VAL | max 100 lines | — | none | — | `validation` 400 | `-a` (H) each field; `-b` (F) |
| REQ-003 | Totals are server-calculated (CALC-002). | CALC | — | — | none | — | — | `-a` (F) |
| REQ-004 | Submission requires: at least one line, purpose, needed-by ≥ submission local date, active cost center, active catalog/category/unit references, active requester. | WF | needed-by rule | `requisitions.submit` (own) | none | — | `requisition.not-submittable` 422 with field details | `-a` (U) each guard |
| REQ-005 | Submission and budget reservation (BUD-004/005/006) are atomic; on failure the requisition stays `Draft` unchanged. | INV/WF | — | `requisitions.submit` | none | `requisition.submitted` + `budget.reservation.created` | `budget.*` 422 | `-a` (P) rollback; `-b` (F) |
| REQ-006 | Submitted content is immutable; each submission stores a snapshot with its revision. | INV | — | — | none | — | `requisition.immutable` 409 | `-a` (U) |
| REQ-007 | Withdrawal: requester only, only from `Submitted` while no RFQ is published (a draft RFQ is cancelled); releases the reservation (`Withdrawn`), increments revision, returns to `Draft`. | WF | — | `requisitions.submit` (own) | none | `requisition.withdrawn` | `invalid-transition` 409 | `-a` (U); `-b` (P) release once |
| REQ-008 | Cancellation with reason: requester for own requisition in `Draft`, `Submitted` or `InSourcing` before an award is submitted; `requisitions.cancel-any` holder in any non-terminal state before a PO is issued. Releases the reservation (`Cancelled`) exactly once, cancels any open RFQ and pending approval. After PO issue, use PUR-008. | WF/AUTH | — | `requisitions.cancel-own` / `requisitions.cancel-any` | none | `requisition.cancelled` | `invalid-transition` 409; `reason-required` 400 | `-a` (U) per state; `-b` (P) cascade; `-c` (E) SC-04 |
| REQ-009 | Expiry: a `Submitted` or `InSourcing` requisition with no pending award whose needed-by date + grace has passed becomes `Expired` (system actor) and releases its reservation (`Expired`). | WF/POL | grace 30 days | system | none | `requisition.expired` | — | `-a` (H) with fake clock; `-b` (W) scheduled trigger |
| REQ-010 | A never-submitted draft may be discarded (deleted) by its requester; anything ever submitted is never deleted. | INV | — | own | none | `requisition.draft.discarded` | `requisition.immutable` 409 | `-a` (P) |
| REQ-011 | Visibility: requester (own), cost-center manager (managed cost centers), department head (department), `requisitions.read-all` holders (buyers, procurement manager, budget controller, auditor). | AUTH | — | as stated | none | — | 404 for unreadable IDs (no existence leak) | `-a` (F) matrix |
| REQ-012 | `Rejected`, `Cancelled`, `Expired` and `Closed` are terminal; "copy as new draft" creates a new requisition referencing the source. | WF | — | `requisitions.create` | none | `requisition.copied` | `invalid-transition` 409 | `-a` (U) |

## Sourcing (SRC)

Phase 29.

| ID | Rule | Class | Policy v1 | Permission | Exception | Audit | Failure | Tests |
|---|---|---|---|---|---|---|---|---|
| SRC-001 | An RFQ is created only for a `Submitted` requisition with no other non-terminal RFQ; round number = previous rounds + 1. Creation moves the requisition to `InSourcing` when the RFQ is published. | INV | — | `sourcing.rfqs.manage` | none | `rfq.created` | `rfq.requisition-not-eligible` 422; `rfq.already-active` 409 | `-a` (U); `-b` (P) unique active RFQ |
| SRC-002 | RFQ lines are the requisition submission snapshot and are immutable. | INV | — | — | none | — | — | `-a` (U) |
| SRC-003 | Only `Active` suppliers may be invited; the invitation stores the supplier identity and status snapshot. | INV | — | `sourcing.rfqs.manage` | none | `rfq.supplier-invited` | `supplier.not-eligible` 422 | `-a` (U); `-b` (F) suspended supplier |
| SRC-004 | Publishing requires a deadline within the response window. | POL/VAL | 24 h – 30 days | `sourcing.rfqs.manage` | none | `rfq.published` | `rfq.deadline-invalid` 400 | `-a` (U) bounds with fake clock |
| SRC-005 | Competitive RFQs require the minimum invited eligible suppliers at publish: 3 when the estimated total in base currency exceeds the threshold, otherwise 1 (D23-14). A non-BRL estimate is converted per CALC-004 with the rate of the RFQ creation date; without a rate the threshold is treated as exceeded (conservative). | POL | threshold 5,000.00 BRL; 3 / 1 | — | `DirectPurchase` / `LowValueDirectPurchase` through SRC-006 | — | `rfq.insufficient-invitations` 422 | `-a` (U) 5,000.00 vs 5,000.01; `-b` (F) |
| SRC-006 | `Direct` RFQs have exactly one invited supplier and require a waiver at publish: `LowValueDirectPurchase` when the estimated total ≤ threshold, `DirectPurchase` otherwise. | POL/AUTH | threshold | waiver permission | the waiver itself (reason) | `waiver.recorded` | `waiver.permission-required` 403; `reason-required` 400 | `-a` (U); `-b` (F) buyer cannot use `DirectPurchase`; `-c` (E) SC-02 |
| SRC-007 | Invitations may be added or removed while `Draft`; after publish they may only be added (eligible suppliers, before the deadline). | WF | — | `sourcing.rfqs.manage` | none | `rfq.supplier-invited` / `rfq.supplier-removed` | `invalid-transition` 409 | `-a` (U) |
| SRC-008 | A quote is entered by a buyer for an invited supplier of a `Published` RFQ with `ReceivedAtUtc ≤ now` and `≤ deadline`; late quotes are rejected (B23-6). The quote records source, entered-by and entry time. | WF/VAL | — | `sourcing.quotes.enter` | none | `quote.recorded` | `quote.late` 422; `quote.supplier-not-invited` 422 | `-a` (U) deadline boundary; `-b` (F) |
| SRC-009 | Quote revisions are immutable; revising creates revision n+1 and supersedes n; withdrawal marks the current revision `Withdrawn`; history is kept. | INV | — | `sourcing.quotes.enter` | none | `quote.revised` / `quote.withdrawn` | `quote.immutable` 409 | `-a` (U) |
| SRC-010 | `ValidUntil` ≥ the RFQ deadline local date; a quote expired at award submission is not awardable. | VAL/INV | — | — | none | — | `validation` 400; `quote.expired` 422 | `-a` (U) |
| SRC-011 | Deadline extension before the deadline passes: later than current, at most +30 days per extension, reason required; invited suppliers are notified. | WF | max +30 days | `sourcing.rfqs.manage` | none | `rfq.deadline-extended` | `rfq.deadline-passed` 409 | `-a` (U) |
| SRC-012 | Closing: automatic at the deadline (scheduled trigger) or early by `sourcing.rfqs.close-early` with reason. Closing freezes current revisions and computes the immutable comparison. | WF | — | `sourcing.rfqs.close-early` | none | `rfq.closed` | `invalid-transition` 409 | `-a` (U); `-b` (W) scheduled close idempotent |
| SRC-013 | Cross-currency quotes are normalized per CALC-004 with a persisted rate snapshot; without a rate the comparison is `Incomplete` until a manual rate is recorded with the `ManualExchangeRate` waiver. | POL/CALC | PTAX selling rate | `sourcing.rates.manual` | `ManualExchangeRate` | `waiver.recorded` | — | `-a` (H) fake BCB down → Incomplete; `-b` (F) manual rate |
| SRC-014 | A quote is valid iff: current revision, received by the deadline, not expired at close, supplier `Active` at close, and covering every RFQ line with offered quantity ≥ requested (D23-15). Invalid quotes are listed with reasons; missing lines are never treated as zero. | CALC/INV | — | — | none | — | — | `-a` (U) each invalid reason |
| SRC-015 | Ranking per CALC-005; the UI shows rank and raw plus normalized totals. | CALC | — | — | none | — | — | see CALC-005 |
| SRC-016 | Award: one valid, non-expired quote for the whole RFQ; supplier `Active` at award submission; at most one award `PendingApproval` per RFQ. | INV | — | `sourcing.awards.submit` | none | `award.submitted` | `award.not-eligible` 422; `award.pending-exists` 409 | `-a` (U); `-b` (P) |
| SRC-017 | A competitive RFQ above the threshold with fewer valid quotes than required can be awarded only with an `InsufficientQuotations` waiver; with zero valid quotes no award is possible (cancel the RFQ and start a new round). | POL | min valid quotes 3 | `sourcing.waivers.insufficient-quotations` | `InsufficientQuotations` | `waiver.recorded` | `award.insufficient-quotations` 422 | `-a` (U) 2 valid quotes; `-b` (E) SC-03 |
| SRC-018 | Awarding a quote that is not the best-ranked quote among those currently awardable (SRC-016; for example the rank 1 supplier was suspended) requires a `NonLeadingSelection` waiver. | POL | — | `sourcing.waivers.non-leading` | `NonLeadingSelection` | `waiver.recorded` | `award.non-leading-requires-waiver` 422 | `-a` (U) |
| SRC-019 | Award submission atomically: records the award, adjusts the reservation (BUD-009), moves the requisition to `PendingApproval` and writes the Outbox event that starts approval (APR-001). | WF | — | `sourcing.awards.submit` | none | `award.submitted` | `budget.insufficient` 422 | `-a` (P) atomicity |
| SRC-020 | RFQ cancellation with reason before an award is approved; the requisition returns to `Submitted`; a pending approval is cancelled. | WF | — | `sourcing.rfqs.manage` | none | `rfq.cancelled` | `invalid-transition` 409 | `-a` (U); `-b` (P) |
| SRC-021 | Quote commercial details are visible only to `sourcing.quotes.read` holders, to assignees of the related approval instance and to auditors. Requesters see status and the awarded supplier only. | AUTH | — | `sourcing.quotes.read` | none | — | 403/404 | `-a` (F) |
| SRC-022 | Invitation, extension and reminder notifications are queued through the Outbox; notification failure never changes RFQ state. | WF | — | — | none | — | — | `-a` (W) |

## Approval (APR)

Phase 31 (APR-001 trigger from Phase 29 is stubbed by an Outbox event until Phase 31 consumes it).

| ID | Rule | Class | Policy v1 | Permission | Exception | Audit | Failure | Tests |
|---|---|---|---|---|---|---|---|---|
| APR-001 | Award submission starts one approval instance, pinned to the policy version effective at that instant, with a deterministic workflow instance ID derived from the award ID. Duplicate starts are no-ops. | WF/POL | — | system | none | `approval.started` | — | `-a` (D) duplicate start; `-b` (P) |
| APR-002 | Required levels: level L is required iff the award amount in base currency > its threshold; steps run in ascending order. | CALC/POL | L1 > 0; L2 > 10,000.00; L3 > 100,000.00 BRL | — | none | — | — | `-a` (U) 10,000.00 → L1; 10,000.01 → L1+L2; 100,000.01 → L1–L3 |
| APR-003 | Assignee resolution: L1 cost-center manager; L2 department head; L3 any `FinanceApprover` member. A missing or inactive assignee resolves to the escalation assignee group. | POL | as stated | — | none | `approval.step.assigned` | — | `-a` (H) each resolver and fallback |
| APR-004 | Only the current assignee (user, group member or delegate) holding `approvals.decide` decides a `Pending` step that is the current step. | AUTH/INV | — | `approvals.decide` | none | — | `approval.not-assignee` 403; `approval.step-not-current` 409 | `-a` (F) |
| APR-005 | The requester cannot give the final approval of their own requisition (D23-13). | INV | — | — | none | `approval.decision.denied` | `approval.self-approval` 403 | `-a` (U); `-b` (E) SC-09 |
| APR-006 | Resolution never assigns an ineligible actor: if the resolved user violates APR-005/007/008 the step goes to the escalation group; group members who violate them cannot decide. | WF | — | — | none | `approval.step.assigned` | — | `-a` (H) requester is cost-center manager |
| APR-007 | The same user cannot decide two levels of the same instance (B23-2). | INV | — | — | none | — | `approval.duplicate-approver` 403 | `-a` (U) |
| APR-008 | The buyer who submitted the award cannot give the final approval (B23-2). | INV | — | — | none | — | `approval.maker-checker` 403 | `-a` (U) |
| APR-009 | Rejection requires a reason; it rejects the instance and the award, sets the requisition `Rejected` and releases the reservation (`Rejected`) atomically. | WF | — | `approvals.decide` | none | `approval.rejected` | `reason-required` 400 | `-a` (P) release once; `-b` (E) SC-04 |
| APR-010 | Delegation by the assignee to an active user holding `approvals.decide` who is not themselves and does not violate APR-005/007/008; reason required; the due time is unchanged; the delegator stays recorded. | WF/AUTH | — | `approvals.delegate` | none | `approval.step.delegated` | `approval.invalid-delegate` 422 | `-a` (U) |
| APR-011 | Deadlines (calendar hours, B23-11): reminder after 72 h; at 120 h the step escalates to the escalation group with a new 120 h deadline; when an escalated step is overdue again the instance expires, the requisition becomes `Expired` and the reservation is released (`Expired`). | WF/POL | 72 h / 120 h / 1 escalation | system (durable timers) | none | `approval.step.reminded` / `escalated` / `approval.expired` | — | `-a` (D) timers; `-b` (E) SC-05 |
| APR-012 | Invalidation: supplier suspension/inactivation, award withdrawal or RFQ cancellation ends a pending instance as `Invalidated` (pending steps `Cancelled`); the requisition returns to `InSourcing` (or `Submitted` after RFQ cancellation) and the reservation stays `Reserved`. Requisition cancellation ends it as `Cancelled`. | WF | — | system | none | `approval.invalidated` | — | `-a` (H); `-b` (E) SC-06 |
| APR-013 | Final approval atomically: approves the instance and award, commits the reservation (BUD-010), sets the requisition `Approved` and writes the Outbox event that creates the PO draft (PUR-001). | WF | — | `approvals.decide` | none | `approval.approved` | — | `-a` (P) atomic |
| APR-014 | Repeating the same decision by the same actor on an already-decided step returns the current state (idempotent); any other decision on a decided step, or any decision on an ended instance, returns a conflict and changes nothing. | WF | — | — | none | — | `approval.step-already-decided` 409; `approval.instance-ended` 409 | `-a` (P) concurrent decisions (SC-08); `-b` (D) late event |
| APR-015 | Administrative intervention (`approvals.administer`): reassign a pending step or cancel an instance (requisition returns to `InSourcing`), reason required; an administrator can never approve on someone's behalf. | AUTH/WF | — | `approvals.administer` | none | `approval.administered` | `reason-required` 400 | `-a` (F) |
| APR-016 | Durable orchestration contains no business rule; activities call Application use cases; the database is the business source of truth; a reconciliation query reports instances whose durable status and database status diverge. | WF | — | — | none | — | — | `-a` (A) orchestrator references no Domain policy; `-b` (D) reconciliation |
| APR-017 | Approval policy versions are immutable once used (POL-001). | INV | — | `approvals.policies.manage` | none | `policy.version.created` | `policy.version-in-use` 409 | `-a` (U) |

## Purchasing (PUR)

Phase 32.

| ID | Rule | Class | Policy v1 | Permission | Exception | Audit | Failure | Tests |
|---|---|---|---|---|---|---|---|---|
| PUR-001 | Final approval creates exactly one PO in `Draft` per approved award (unique award ID), idempotently. | INV | — | system | none | `purchase-order.created` | — | `-a` (P) duplicate event; `-b` (W) |
| PUR-002 | The commercial snapshot (supplier identity, lines, prices, quantities, currency, freight, discount, taxes included, payment terms) is copied from the award (CALC-006) and immutable; only delivery address, promised delivery dates and notes are editable in `Draft`. | INV | — | `purchasing.orders.manage` | none | `purchase-order.updated` | `purchase-order.immutable` 409 | `-a` (U) |
| PUR-003 | Issue requires: PO `Draft`, supplier `Active`, approval `Approved`, reservation `Committed`, complete snapshot. | INV/WF | — | `purchasing.orders.issue` | none | `purchase-order.issued` | `supplier.not-eligible` 422; `purchase-order.not-issuable` 422 | `-a` (U) each guard; `-b` (E) SC-06 |
| PUR-004 | Totals per CALC-002/006. | CALC | — | — | none | — | — | `-a` (U) |
| PUR-005 | The PO number `PO-{yyyy}-{nnnnnn}` is allocated at issue from a per-year counter in the same transaction (organization time zone year); numbers are unique and gapless among committed issues (SP-2). | INV/CALC | — | — | none | — | — | `-a` (P) parallel issues |
| PUR-006 | Repeating issue on an issued PO returns the same PO and number (idempotent). | WF | — | `purchasing.orders.issue` | none | — | — | `-a` (F) |
| PUR-007 | Document generation and supplier notification run asynchronously after issue; failures are visible on the PO and retryable; they never unissue, renumber or duplicate the PO. The PO stores the document file ID and template version. | WF | — | `purchasing.orders.issue` (retry) | none | `purchase-order.document.*` | — | `-a` (W) failure then retry |
| PUR-008 | Cancellation with reason: a `Draft` PO cancel withdraws the award, sets the reservation back to `Reserved` and returns the requisition to `InSourcing` for re-award; an `Issued` PO with zero accepted quantity can be cancelled, releasing the commitment (`OrderCancelled`) and cancelling the requisition. Not allowed once any quantity is accepted (use PUR-010). | WF | — | `purchasing.orders.cancel` | none | `purchase-order.cancelled` | `purchase-order.has-receipts` 409 | `-a` (U) each path; `-b` (P) release once |
| PUR-009 | A PO becomes `Completed` when every line's accepted quantity ≥ ordered or the line is short-closed; the requisition becomes `Closed`. A correction that drops a line below ordered reopens the PO to `PartiallyReceived` (requisition back to `Ordered`). | WF | — | system | none | `purchase-order.completed` / `reopened` | — | `-a` (U); `-b` (E) SC-07 |
| PUR-010 | Short close of a `PartiallyReceived` PO marks the unreceived lines short-closed, releases the unreceived commitment (CALC-008, reason `ShortClosed`) and completes the PO. | POL/WF | — | `purchasing.orders.short-close` | `ShortClose` waiver | `waiver.recorded` | `invalid-transition` 409 | `-a` (U); `-b` (P) |
| PUR-011 | PO visibility: purchasing roles, receivers (issued POs, without prices unless they hold `purchasing.orders.read`), requester (own requisition's PO, without supplier contact data), auditors. | AUTH | — | `purchasing.orders.read` / `receiving.receipts.create` | none | — | 403/404 | `-a` (F) |

## Receiving (RCV)

Phase 32.

| ID | Rule | Class | Policy v1 | Permission | Exception | Audit | Failure | Tests |
|---|---|---|---|---|---|---|---|---|
| RCV-001 | A receipt references an `Issued` or `PartiallyReceived` PO. | INV | — | `receiving.receipts.create` | none | — | `receipt.order-not-receivable` 422 | `-a` (U) |
| RCV-002 | Per line: received > 0; accepted ≥ 0; rejected ≥ 0; accepted + rejected = received; unit decimals respected; at least one line. | VAL/INV | — | — | none | — | `validation` 400 | `-a` (U) |
| RCV-003 | Accepted quantity (prior receipts + corrections + this receipt) ≤ receipt cap (CALC-009). Over-receipt is never silent. | POL | tolerance 0 % | — | none | — | `receipt.over-receipt` 422 | `-a` (U) 10 ordered: 4 + 6 ok, +1 denied; `-b` (E) SC-07 |
| RCV-004 | Rejected quantity > 0 requires a discrepancy type (`Damaged`, `WrongItem`, `QualityIssue`, `Other`) and notes. | VAL | — | — | none | `receipt.discrepancy-recorded` | `validation` 400 | `-a` (H) |
| RCV-005 | The receiver is the authenticated actor; `ReceivedOn` is between the PO issue date and today (organization time zone). | VAL/AUTH | — | `receiving.receipts.create` | none | `receipt.posted` | `validation` 400 | `-a` (H) |
| RCV-006 | Posted receipts are immutable and never deleted. | INV | — | — | none | — | `receipt.immutable` 409 | `-a` (A) no update/delete endpoints; `-b` (U) |
| RCV-007 | Correction: a compensating record with a signed accepted-quantity delta, reason and actor; the resulting accepted total stays between 0 and the cap; PO completion is recomputed (PUR-009). | INV/WF | — | `receiving.receipts.correct` | none | `receipt.corrected` | `receipt.correction-out-of-range` 422 | `-a` (U); `-b` (E) SC-07 |
| RCV-008 | Services are received with the same rules using their unit (for example hours or `UN`). | WF | — | — | none | — | — | `-a` (U) |

## Files (FIL) and reports (RPT)

Phase 30 (FIL-002 metadata placeholder from Phase 27).

| ID | Rule | Class | Policy v1 | Permission | Exception | Audit | Failure | Tests |
|---|---|---|---|---|---|---|---|---|
| FIL-001 | File bytes live only in blob storage behind `IFileStorage`; PostgreSQL stores metadata; Domain never sees bytes or storage keys. | INV | — | — | none | — | — | `-a` (A) |
| FIL-002 | Uploads respect `FilePolicy`; names are sanitized; SHA-256 is computed server-side; content sniffing must match the declared media type. Until Phase 30, Phase 27 accepts attachment metadata only in `Pending` status (no bytes). | VAL/POL | `FilePolicy` | owner write permission | none | `file.uploaded` | `file.too-large` 413; `file.type-not-allowed` 415; `file.limit-reached` 422 | `-a` (H); `-b` (F) |
| FIL-003 | Downloads are authorized against the owner resource's read permission and streamed by the API; no permanent or public storage URL is returned. | AUTH/INV | — | owner read permission | none | `file.downloaded` (for `Confidential`) | 404 | `-a` (F) unauthorized → 404 |
| FIL-004 | Attachments of submitted or posted records are append-only (add allowed while the owner is non-terminal, removal not allowed). | INV | — | owner write permission | none | `file.attached` | `file.immutable` 409 | `-a` (U) |
| FIL-005 | `Pending` uploads older than 24 h and expired report outputs are selected for deletion by a scheduled job; attachments of business records follow the owner's retention. | POL | 24 h / 7 days | system | none | `file.expired` | — | `-a` (H) selection |
| FIL-006 | A malware-scan hook exists as a port with a no-op adapter; the product does not claim malware protection. | WF | — | — | none | — | — | `-a` (A) port present |
| RPT-001 | `POST` report request returns `202 Accepted` with job ID and status location; the same `Idempotency-Key` returns the same job; an identical active request (same user, type, parameters) returns the existing job. | WF | — | `reports.request` | none | `report.requested` | — | `-a` (F) |
| RPT-002 | Requesting a report also requires read permission on the underlying data (for example `budget.read` for budget reports). | AUTH | — | as stated | none | — | 403 | `-a` (F) |
| RPT-003 | Reports use the parameter snapshot, an `AsOfUtc` instant and immutable snapshots; history is never recomputed with today's supplier status, exchange rate, policy or master data. | INV | — | — | none | — | — | `-a` (P) as-of consistency |
| RPT-004 | Every report shows generation instant, as-of instant, time zone, filters, currency and policy versions where relevant. | VAL | — | — | none | — | — | `-a` (semantic PDF/XLSX assertions) |
| RPT-005 | At most 3 attempts; a job has at most one active output; failures expose a safe failure code only. | WF/POL | 3 attempts | system | none | `report.failed` / `report.ready` | — | `-a` (W) retry/duplicate |
| RPT-006 | Download only by the requester or `reports.read-all` holders while `Ready` and not expired. | AUTH | 7 days | `reports.request` / `reports.read-all` | none | — | 404 / `report.expired` 410 | `-a` (F) |
| RPT-007 | XLSX cells with user-controlled text starting with `=`, `+`, `-`, `@`, tab or carriage return are written as text with a leading apostrophe-equivalent escape; no formulas from user data. | INV | — | — | none | — | — | `-a` (U) injection payloads |
| RPT-008 | Templates are versioned server assets; API users cannot supply templates, HTML or scripts. | INV | — | — | none | — | — | `-a` (A) |
| RPT-009 | Report catalog: `RequisitionPdf` (Phase 30), `QuoteComparisonPdf` (30), `BudgetConsumptionXlsx` (30), `SupplierQuotesXlsx` (30), `PurchaseOrderPdf` (32). | WF | — | per type | none | — | `report.type-unknown` 400 | `-a` (F) |

## Scenario traceability

| Scenario ([product-vision.md](product-vision.md#7-success-scenarios)) | Rules exercised | Planned tests (minimum) | Phases |
|---|---|---|---|
| SC-01 Standard competitive purchase | REQ-004/005, BUD-004/005/009/010, SRC-001/003/005/012/014/015/016/019, APR-001/002/003/013, PUR-001/003/005/007/009, RCV-001/003, CALC-002/005 | `T-REQ-005-b`, `T-SRC-005-b`, `T-APR-002-a`, `T-APR-013-a`, `T-PUR-005-a`, `T-RCV-003-a`, E2E `T-SC-01` | 27, 29, 31, 32, 33 |
| SC-02 Low-value direct purchase | SRC-006 (`LowValueDirectPurchase`), APR-002 (L1 only), PUR-003 | `T-SRC-006-a/b/c`, `T-APR-002-a`, E2E `T-SC-02` | 29, 31, 32, 33 |
| SC-03 Insufficient quotations | SRC-014, SRC-017 (`InsufficientQuotations`), APR-001 | `T-SRC-017-a/b`, E2E `T-SC-03` | 29, 31, 33 |
| SC-04 Rejection and cancellation | APR-009, REQ-008, BUD-007 | `T-APR-009-a/b`, `T-REQ-008-a/b/c`, `T-BUD-007-a` | 27, 31, 33 |
| SC-05 Expiry | APR-011, REQ-009, BUD-007 | `T-APR-011-a/b`, `T-REQ-009-a/b` | 27, 31, 33 |
| SC-06 Supplier suspension | SUP-003/004/005, APR-012, PUR-003, SRC-016 | `T-SUP-004-b`, `T-APR-012-b`, `T-PUR-003-b` | 25, 29, 31, 32, 33 |
| SC-07 Partial receipt and correction | RCV-002/003/007, PUR-009, CALC-009 | `T-RCV-003-a/b`, `T-RCV-007-a/b`, `T-PUR-009-b` | 32, 33 |
| SC-08 Concurrency | BUD-006, APR-014, API ETag rules ([api-guidelines.md](api-guidelines.md#4-optimistic-concurrency)) | `T-BUD-006-a/b`, `T-APR-014-a`, `T-API-412` | 24, 27, 31, 33 |
| SC-09 Self-approval denial | APR-005/006 | `T-APR-005-a/b`, `T-APR-006-a` | 31, 33 |

Each phase report extends this table with real test names and results for the rules it implemented. A rule is "done" only when its planned tests (or approved equivalents) pass.
