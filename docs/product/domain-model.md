# Domain model

## 1. Modules and ownership

The product is a modular monolith ([ADR-0001](adr/0001-modular-monolith.md)). Modules share one deployment and one PostgreSQL application database but own their tables, namespaces, contracts and tests. A module references another module's aggregate **by ID only**, plus an immutable snapshot of the fields it needs to keep history stable.

| Module | Namespace suffix | Aggregates owned | Rule prefix | Phase |
|---|---|---|---|---|
| Access (identity/session) | `Access` | `UserSession` (Infrastructure.Identity), permission catalog | `SEC` | 24 |
| Organization | `Organization` | `OrganizationSettings`, `Department`, `CostCenter` | `ORG` | 25 |
| Suppliers | `Suppliers` | `Supplier` | `SUP` | 25 |
| Catalog | `Catalog` | `CatalogItem`, `Category`, `UnitOfMeasure` | `CAT` | 25 |
| Budgeting | `Budgeting` | `FiscalPeriod`, `BudgetAllocation` (owns `BudgetReservation`) | `BUD` | 27 |
| Requisitions | `Requisitions` | `PurchaseRequisition` | `REQ` | 27 |
| Sourcing | `Sourcing` | `RequestForQuotation` (owns invitations, quotes, comparison, award) | `SRC` | 29 |
| Approvals | `Approvals` | `ApprovalPolicy`, `ApprovalInstance` | `APR` | 31 |
| Purchasing | `Purchasing` | `PurchaseOrder` | `PUR` | 32 |
| Receiving | `Receiving` | `Receipt` | `RCV` | 32 |
| Reporting and files | `Reporting`, `Files` | `ReportJob`, `StoredFile` | `RPT`, `FIL` | 30 |
| Integrations | `Integrations` | none (ports and enrichment snapshots only) | `INT` | 25, 28 |
| Audit (cross-cutting) | `Auditing` | `AuditRecord` (append-only) | `AUD` | 24 onward |

Target code placement follows the existing layers: `Src/Core/Template.Domain/<Module>/...`, `Src/Core/Template.Application/Features/<Module>/...`, persistence in `Template.Infrastructure.Persistence` with one `IEntityTypeConfiguration` per aggregate and a module-prefixed table naming convention (for example `requisitions_purchase_requisition`). A split into per-module projects is not required; architecture tests enforce that a module's Domain namespace does not reference another module's Domain types except shared kernel value objects.

## 2. Shared kernel (Domain)

Framework-free value objects usable by every module:

| Type | Fields | Invariants |
|---|---|---|
| `Money` | `Amount: decimal`, `Currency: CurrencyCode` | Arithmetic and comparison only with the same currency (INV, CALC-001); amounts are rounded only through `CalculationRules` boundaries |
| `CurrencyCode` | 3 uppercase letters | ISO 4217 format; supported list is configuration (`BRL`, `USD`, `EUR` seeded) |
| `Quantity` | `Value: decimal` | `> 0` for requested/ordered quantities; precision ≤ 3 decimals and ≤ the unit's allowed decimals |
| `ExchangeRateSnapshot` | `From`, `To`, `Rate (decimal, 6 dp)`, `Source (BcbPtax/Manual)`, `QuoteDate`, `RetrievedAtUtc`, `EnteredBy?`, `Reason?` | `Rate > 0`; `Manual` requires `EnteredBy` and `Reason` |
| `PolicyReference` | `PolicyType`, `Version` | Immutable once captured |
| `WaiverDecision` | `WaiverType`, `RuleId`, `ActorId`, `Permission`, `Reason`, `DecidedAtUtc`, `Scope (aggregate type + id)` | Reason 10–1,000 characters; actor and permission required |
| `StateChange` | `From`, `To`, `ActorId`, `AtUtc`, `Reason?`, `CorrelationId` | Appended, never edited |
| `LocalDate` handling | `DateOnly` in the organization time zone | Converted from UTC only through `IClock` + organization time zone |
| Strongly typed IDs | `RequisitionId`, `SupplierId`, ... (`Guid`) | Non-empty |

Every aggregate root carries `Version` (optimistic concurrency, mapped by Persistence per SP-1), `CreatedAtUtc`, `CreatedBy`, and appends `StateChange` entries. The existing `BaseEntity`/`AuditableBaseEntity` remain for the sample entities; new aggregates use private setters, behavior methods and EF-compatible private constructors as required by `Src/Core/AGENTS.md`.

## 3. Aggregates

### Organization

- **OrganizationSettings** (singleton): `DisplayName`, `BaseCurrency = BRL`, `TimeZone = America/Sao_Paulo`, `SupportedCurrencies`. Changing `BaseCurrency` after the first reservation is forbidden (ORG-004).
- **Department**: `Code` (unique, immutable, `^[A-Z0-9-]{2,20}$`), `Name`, `HeadUserId?`, `IsActive`.
- **CostCenter**: `Code` (unique, immutable), `Name`, `DepartmentId`, `ManagerUserId?`, `IsActive`. Inactive cost centers keep history but reject new requisitions and allocations (ORG-003).

### Suppliers

- **Supplier**: `LegalName`, `TradeName?`, `TaxId` (normalized digits; CNPJ structural check digits for Brazilian suppliers, free format otherwise), `Country`, `Status`, `Contacts[]` (name, email, phone, role), `Addresses[]` (type, postal code, street, number, district, city, state, country), `CategoryIds[]`, `EnrichmentSnapshot?` (provider, retrievedAtUtc, accepted fields, acceptedBy), `StatusHistory[]`.
- Lifecycle: `Draft → Active → Suspended ↔ Active`, `Active/Suspended → Inactive`, `Inactive → Active` (reactivation). Reason required for suspend, inactivate and reactivate (SUP-005).
- Invariants: unique normalized `TaxId` per country (SUP-001); at least one contact with email before activation (SUP-002).

### Catalog

- **Category**: `Code`, `Name`, `IsActive`.
- **UnitOfMeasure**: `Code` (for example `UN`, `KG`, `H`), `Name`, `AllowedDecimals (0–3)`, `IsActive`.
- **CatalogItem**: `Code` (unique), `Description`, `Kind (Good/Service)`, `CategoryId`, `UnitOfMeasureId`, `IsActive`, optional `ReferencePrice: Money` (advisory only).

### Budgeting

- **FiscalPeriod**: `Code` (unique), `StartDate`, `EndDate` (local dates, non-overlapping), `Status (Planned/Open/Closed)`. At most one `Open` period contains a given date (BUD-001).
- **BudgetAllocation**: `FiscalPeriodId`, `CostCenterId`, `Currency`, `Amount: Money`, `Status (Active/Closed)`, `Reservations[]`, `AmountHistory[]`. Unique per (period, cost center, currency) (BUD-002). Owns **BudgetReservation** entities: `RequisitionId`, `Amount: Money`, `Status (Reserved/Committed/Released)`, `ReleaseReason?`, `History[]`. Reservations live inside the allocation aggregate so availability is checked and changed in one consistency boundary under one row version (BUD-005, BUD-006).
- Derived: `Available = Amount − Σ Amount(reservations in Reserved or Committed)` (CALC-003).

### Requisitions

- **PurchaseRequisition**: `Number` (`REQ-{yyyy}-{nnnnnn}`), `RequesterId`, `CostCenterId` + snapshot (code, name, department), `Purpose` (10–2,000 chars), `NeededBy: DateOnly`, `Currency`, `Lines[]`, `EstimatedTotal: Money` (server-calculated), `Status`, `Revision`, `SubmissionSnapshots[]`, `ActiveReservationId?`, `RfqId?`, `AwardSnapshot?` (supplier, amount, rate), `AttachmentIds[]`, `StateChanges[]`.
- **RequisitionLine**: `LineNumber`, `CatalogItemId?`, `Description`, `Kind`, `CategoryId` + snapshot, `Quantity`, `UnitOfMeasure` snapshot, `EstimatedUnitPrice: Money`, `LineTotal: Money`.
- Lifecycle in [workflows.md](workflows.md#1-purchase-requisition).

### Sourcing

- **RequestForQuotation**: `Number` (`RFQ-{yyyy}-{nnnnnn}`), `RequisitionId` + requisition snapshot (lines, revision, estimated total), `RoundNumber`, `SourcingMethod`, `SourcingPolicy: PolicyReference`, `Deadline (UTC instant)`, `Status`, `Invitations[]`, `Quotes[]`, `Waivers[]`, `Comparison?`, `Award?`, `StateChanges[]`.
- **Invitation**: `SupplierId`, supplier snapshot (legal name, tax ID, status at invitation), `InvitedAtUtc`, `InvitedBy`, `RemovedAtUtc?`.
- **Quote** with **QuoteRevision**: `SupplierId`, `RevisionNumber`, `Status (Current/Superseded/Withdrawn)`, `Currency`, `LineOffers[]` (rfqLineNumber, unit price, quantity offered), `Freight`, `Discount`, `TaxesIncluded: Money` (explicit quoted amount, no tax engine), `ValidUntil`, `DeliveryDays`, `PaymentTerms`, `Source (Email/Phone/Document/Other)`, `ReceivedAtUtc`, `EnteredBy`, `EnteredAtUtc`, `AttachmentIds[]`.
- **Comparison** (immutable value): `BaseCurrency`, `RateSnapshots[]`, `Entries[]` (quoteRevisionId, rawTotal, normalizedTotal, isValid, invalidReasons[], rank), `Warnings[]`, `ComputedAtUtc`.
- **Award**: `QuoteRevisionId`, `SupplierId`, `AwardedTotal: Money` (quote currency), `NormalizedTotal: Money` (base currency), `RequisitionCurrencyTotal: Money`, `Justification?`, `SubmittedBy`, `SubmittedAtUtc`, `Status (PendingApproval/Approved/Rejected/Invalidated/Withdrawn)`.

### Approvals

- **ApprovalPolicy**: `Version` (immutable once used), `EffectiveFromUtc`, `Levels[]` (`Level`, `MinimumAmountExclusive: Money (base currency)`, `AssigneeResolver (CostCenterManager/DepartmentHead/RoleGroup:FinanceApprover)`, `StepDurationHours`, `ReminderAfterHours`, `EscalationAssignee`), `MaxEscalations`. Policy v1 values in [business-rules.md](business-rules.md#approval-apr).
- **ApprovalInstance**: `RequisitionId`, `RfqId`, `AwardId`, `Policy: PolicyReference`, `AmountEvaluated: Money (base currency)`, `RequiredLevels`, `Steps[]`, `Status (Pending/Approved/Rejected/Cancelled/Expired/Invalidated)`, `WorkflowInstanceId` (Durable instance ID, opaque), `StateChanges[]`.
- **ApprovalStep**: `Level`, `AssigneeUserId?` or `AssigneeRole?`, `AssignedAtUtc`, `DueAtUtc`, `RemindedAtUtc?`, `EscalationCount`, `Delegations[]`, `Decision? (Approved/Rejected)`, `DecidedBy?`, `DecidedAtUtc?`, `Reason?`, `Status (Pending/Approved/Rejected/Skipped/Cancelled/Expired)`.

### Purchasing

- **PurchaseOrder**: `Number?` (allocated at issue), `AwardId`, `ApprovalInstanceId`, `RequisitionId`, supplier snapshot (legal name, tax ID, address, contact), buyer snapshot, delivery address snapshot, `Currency`, `Lines[]`, `Freight`, `Discount`, `TaxesIncluded`, `Total: Money`, `PaymentTerms`, `Status`, `DocumentFileId?` + template version, `StateChanges[]`.
- **PurchaseOrderLine**: `LineNumber`, `RequisitionLineNumber`, `Description`, `Kind`, `Quantity`, `Unit`, `UnitPrice: Money`, `LineTotal`, `PromisedDeliveryDate`, `AcceptedQuantity` (derived from receipts and corrections), `IsShortClosed`.

### Receiving

- **Receipt**: `Number` (`RCV-{yyyy}-{nnnnnn}`), `PurchaseOrderId`, `ReceiverId`, `ReceivedOn: DateOnly`, `Location/Reference`, `Lines[]` (poLineNumber, receivedQuantity, acceptedQuantity, rejectedQuantity, discrepancyType?, notes), `Corrections[]` (poLineNumber, signed accepted-quantity delta, reason, actor, atUtc), `Status (Posted)`, `ReceivingPolicy: PolicyReference`, `AttachmentIds[]`.
- Receipts are posted on creation; there is no editable draft receipt in the first release (the UI keeps the unsaved form client-side).

### Reporting and files

- **StoredFile**: `OwnerType`, `OwnerId`, `OriginalName` (sanitized), `MediaType`, `Length`, `Sha256`, `StorageKey` (opaque, never exposed), `Classification (Internal/Confidential)`, `Status (Pending/Available/Deleted)`, `CreatedBy`, `CreatedAtUtc`, `ExpiresAtUtc?`.
- **ReportJob**: `ReportType`, `RequestedBy`, `ParametersSnapshot` (JSON, validated per type), `AsOfUtc`, `Status`, `Progress?`, `OutputFileId?`, `FailureCode?`, `IdempotencyKey`, `ExpiresAtUtc`.

### Audit

- **AuditRecord**: `Id`, `OccurredAtUtc`, `ActorId?` (null for system), `Action` (for example `requisition.submitted`), `ResourceType`, `ResourceId`, `FromState?`, `ToState?`, `RuleIds[]`, `WaiverId?`, `Reason?`, `CorrelationId`, `CausationId?`, `Details` (allow-listed fields only). Insert-only table; no update or delete through the application (AUD-001).

## 4. Cross-module references

| From | To | Reference | Snapshot kept |
|---|---|---|---|
| CostCenter | Department | `DepartmentId` | — |
| BudgetAllocation | FiscalPeriod, CostCenter | IDs | — |
| BudgetReservation | PurchaseRequisition | `RequisitionId` | — |
| PurchaseRequisition | CostCenter, CatalogItem, Category, UnitOfMeasure, BudgetReservation, RFQ | IDs | cost center code/name/department, line description/category/unit |
| RequestForQuotation | PurchaseRequisition, Supplier, SourcingPolicy | IDs | requisition lines and revision, supplier identity and status at invitation |
| ApprovalInstance | Requisition, RFQ, Award, ApprovalPolicy | IDs | amount evaluated, policy version |
| PurchaseOrder | Award, ApprovalInstance, Requisition, Supplier | IDs | full commercial snapshot |
| Receipt | PurchaseOrder | ID | PO line numbers |
| StoredFile | any owner | `OwnerType` + `OwnerId` | — |
| AuditRecord | any | type + ID | allow-listed details |

Cross-aggregate changes that must be atomic in the same database (for example submission + reservation, final approval + commitment) run in one Application use case and one `IUnitOfWork` transaction. Changes that cross process boundaries (Worker, Durable activities) use the Outbox/Inbox and idempotent use cases ([event-catalog.md](event-catalog.md)).

## 5. What is not a domain invariant

The blueprint classifies each check ([business-rules.md](business-rules.md#classification)). Examples that are **not** invariants: competition threshold (policy), who may submit (authorization), the reminder schedule (workflow), totals rounding (calculation), field length (input validation). Aggregates enforce invariants and state transitions; policies are evaluated by Application services that read the pinned policy version and pass the result into the aggregate method.

## 6. Legacy sample model

`ProductEntity`, `Person` and `Order` (and their v1/v2 endpoints, cache, search and messaging samples) are not part of the procurement domain. They stay unchanged and isolated in their current namespaces. Procurement catalog items do not reuse `ProductEntity`. Removal follows [api-guidelines.md](api-guidelines.md#10-compatibility-and-legacy-samples).
