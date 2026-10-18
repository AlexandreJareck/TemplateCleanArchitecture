# Product vision — Procurement Hub

## 1. Purpose

Procurement Hub turns the academic Clean Architecture template into a realistic, local-first procurement application for **one organization** (D23-1). It models the purchase lifecycle

`purchase requisition -> budget reservation -> request for quotation -> supplier quotes -> comparison -> award approval -> purchase order -> partial/full receipt -> closure`

with corporate-style controls (budget, competition, maker-checker approval, supplier eligibility, auditable corrections) while staying small enough to teach and test.

## 2. Users

| Persona | Role(s) | Main goals |
|---|---|---|
| Requester | `Requester` | Ask for goods or services, see budget availability, follow progress, cancel or withdraw |
| Cost-center manager | `CostCenterManager` | Approve level-1 awards for their cost centers; see team requisitions |
| Department head | `DepartmentHead` | Approve level-2 awards; see department spend commitments |
| Finance approver | `FinanceApprover` | Approve level-3 awards |
| Budget controller | `BudgetController` | Maintain fiscal periods and allocations; monitor reservations |
| Buyer | `Buyer` | Run RFQs, enter quotes, compare, submit awards, issue purchase orders |
| Procurement manager | `ProcurementManager` | Supervise sourcing, grant high-value waivers, manage suppliers, administer approvals |
| Receiver | `Receiver` | Record goods/service receipts and discrepancies |
| Auditor | `Auditor` | Read history, audit trail and reports; never change data |
| Administrator | `Administrator` | Manage users, roles and master data; has **no** business-rule bypass (B23-9) |

Detailed permissions are in [permissions.md](permissions.md).

## 3. Problems addressed

1. Purchases are approved without knowing whether budget exists → reservation at submission (D23-4).
2. Single-source buying without justification → minimum competition with explicit waivers (D23-14).
3. Self-approval and approval of one's own sourcing decision → maker-checker rules (D23-13, B23-2).
4. Purchasing from blocked suppliers → eligibility on every new commitment (D23-16).
5. Edited history (prices, receipts) → immutable snapshots and compensating records (D23-18).
6. Slow document production → asynchronous PDF/XLSX reports (D23-5, D23-6).

## 4. Goals

- G1: A complete requisition-to-receipt journey through API and UI with auditable history.
- G2: Every cross-module rule has a stable ID, a classification, a failure result and tests ([business-rules.md](business-rules.md)).
- G3: Business state lives in PostgreSQL; RabbitMQ, Durable Functions and blob storage are infrastructure behind Application ports.
- G4: Backend authority: the SPA never decides a business outcome.
- G5: Local execution with disposable dependencies; Azure only as validated IaC (D23-9).
- G6: Existing API v1 and sample endpoints keep working ([api-v1-lifecycle.md](../refactoring/api-v1-lifecycle.md)).

## 5. Non-goals

Multi-tenancy; supplier portal; accounts payable, invoices, payments, tax calculation, NFe, three-way matching; inventory and warehouse; returns to supplier; purchase-order amendments; split awards; generic workflow designer or rules engine; microservices; Azure Service Bus; production Azure resources; API v1 removal; legal or regulatory compliance certification.

## 6. Release boundary

The first release (Phase 33 gate) is complete when a user can: authenticate; create a requisition; reserve budget; run an RFQ and compare quotes (or record an approved direct-purchase waiver); complete a timed multi-level approval; issue a purchase order with its PDF; record partial and final receipts (including a compensating correction); generate PDF/XLSX reports asynchronously; and audit the full history through the UI. Every required automated and real-service gate must be green.

## 7. Success scenarios

Each scenario is traced to rule IDs and planned tests in [business-rules.md](business-rules.md#scenario-traceability).

| ID | Scenario | Outcome |
|---|---|---|
| SC-01 | Standard competitive purchase: 18,000.00 BRL requisition, 3 invited Active suppliers, 3 valid quotes, leading quote selected, 2-level approval, PO issued, received in full | Requisition `Closed`, PO `Completed`, budget `Committed` |
| SC-02 | Low-value direct purchase: 1,200.00 BRL requisition, buyer records a `LowValueDirectPurchase` waiver, 1 supplier, level-1 approval, PO, receipt | Waiver audited; no competition required |
| SC-03 | Insufficient quotations: 3 invited, 2 valid quotes at close; procurement manager records an `InsufficientQuotations` waiver; award approved | Waiver audited; award allowed |
| SC-04 | Rejection and cancellation: level-2 approver rejects with reason → requisition `Rejected`, reservation `Released`; a second requisition is cancelled by the requester while `Submitted` → `Cancelled`, reservation `Released` | Funds released exactly once |
| SC-05 | Expiry: approval step passes its deadline twice (reminder, escalation) and the instance expires | Requisition `Expired`, reservation `Released` |
| SC-06 | Supplier suspension: supplier suspended after award submission but before PO issue | Pending approval invalidated; requisition back to `InSourcing`; PO issue blocked; history preserved |
| SC-07 | Partial receipt: PO line of 10 units received as 4 then 6; an over-receipt of 1 unit is denied; a correction of −1 with reason reopens the line | PO `PartiallyReceived` → `Completed` → `PartiallyReceived` → `Completed` after a final receipt |
| SC-08 | Concurrency: two submissions compete for the last 5,000.00 BRL of an allocation; two approvers decide the same step at the same time | Exactly one succeeds; the other receives a deterministic conflict |
| SC-09 | Self-approval denial: the requester is also the cost-center manager and the award needs only level 1 (the final step) | Step is reassigned per APR-006; a direct decision attempt by the requester is denied |
