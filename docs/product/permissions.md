# Permissions and separation of duties

## 1. Model

- **Permission**: stable lowercase string `{module}.{resource}.{action}` (or `{module}.{action}`). Controllers authorize permissions through ASP.NET Core policies named after the permission (SEC-008). Permission names are distinct from business-rule IDs.
- **Role**: a seeded, editable group of permissions. Users may hold several roles. Business code never checks role names.
- **Resource scope**: some permissions are "own" or "assigned" scoped; the use case checks ownership/assignment after the permission check (for example REQ-001, APR-004).
- **No bypass**: authorization answers whether an actor may *attempt* an operation; the use case and domain still enforce every business rule. No role, including `Administrator`, bypasses a rule; exceptions exist only as waivers with their dedicated permission ([business-rules.md](business-rules.md#waiver-catalog)).
- Effective permissions are returned by `GET /api/v2/account/me` (SEC-009) so the SPA can hide unavailable actions; the API remains authoritative.
- Changes to role-permission assignments and user roles require `access.roles.manage` / `access.users.manage` and are audited (`access.role.changed`, `access.user.roles-changed`).

## 2. Permission catalog

| Permission | Meaning | Phase |
|---|---|---|
| `access.users.manage` | Create/deactivate users, assign roles, look up users for manager/head assignment | 25 (seeded in 24) |
| `access.roles.manage` | Edit role → permission mapping | 25 (seeded in 24) |
| `audit.read` | Query the global audit trail | 24 |
| `organization.settings.manage` | Edit organization settings (ORG-004) | 25 |
| `organization.manage` | Departments and cost centers | 25 |
| `organization.read` | Read departments and cost centers | 25 |
| `suppliers.read` | Read suppliers including contacts/addresses | 25 |
| `suppliers.manage` | Create/edit supplier data (draft, contacts, addresses, enrichment acceptance) | 25 |
| `suppliers.status.change` | Activate, suspend, inactivate, reactivate | 25 |
| `catalog.read` | Read catalog, categories, units | 25 |
| `catalog.manage` | Manage catalog, categories, units | 25 |
| `budget.read` | Read allocations, availability, reservations | 27 |
| `budget.periods.manage` | Fiscal period lifecycle | 27 |
| `budget.allocations.manage` | Create/change allocations | 27 |
| `requisitions.create` | Create, edit, discard, submit, withdraw own requisitions | 27 |
| `requisitions.submit` | Submit/withdraw own requisitions (held together with `create`) | 27 |
| `requisitions.edit-any` | Proxy editing of another user's draft | 27 |
| `requisitions.cancel-own` | Cancel own requisitions (REQ-008) | 27 |
| `requisitions.cancel-any` | Cancel any requisition before PO issue | 27 |
| `requisitions.read-team` | Read requisitions of managed cost centers / department (scope resolved from organization data) | 27 |
| `requisitions.read-all` | Read all requisitions | 27 |
| `sourcing.rfqs.manage` | Create, publish, invite, extend, cancel RFQs | 29 |
| `sourcing.rfqs.close-early` | Early close with reason | 29 |
| `sourcing.quotes.enter` | Enter, revise, withdraw quotes | 29 |
| `sourcing.quotes.read` | Read quote commercial details and comparisons | 29 |
| `sourcing.awards.submit` | Submit an award for approval | 29 |
| `sourcing.waivers.low-value` | `LowValueDirectPurchase` waiver | 29 |
| `sourcing.waivers.direct-purchase` | `DirectPurchase` waiver | 29 |
| `sourcing.waivers.insufficient-quotations` | `InsufficientQuotations` waiver | 29 |
| `sourcing.waivers.non-leading` | `NonLeadingSelection` waiver | 29 |
| `sourcing.rates.manual` | `ManualExchangeRate` waiver | 29 |
| `approvals.decide` | Decide steps assigned to the actor | 31 |
| `approvals.delegate` | Delegate own assigned steps | 31 |
| `approvals.administer` | Reassign steps, cancel instances (APR-015) | 31 |
| `approvals.read-all` | Read all approval instances | 31 |
| `approvals.policies.manage` | Create approval/sourcing/receiving policy versions | 31 |
| `purchasing.orders.read` | Read purchase orders with prices | 32 |
| `purchasing.orders.manage` | Edit draft PO delivery data | 32 |
| `purchasing.orders.issue` | Issue POs, retry document/notification | 32 |
| `purchasing.orders.cancel` | Cancel POs (PUR-008) | 32 |
| `purchasing.orders.short-close` | `ShortClose` waiver | 32 |
| `receiving.receipts.create` | Post receipts; read issued POs without prices | 32 |
| `receiving.receipts.correct` | Post receipt corrections | 32 |
| `receiving.receipts.read` | Read receipts | 32 |
| `reports.request` | Request and download own reports | 30 |
| `reports.read-all` | Download any report job output | 30 |
| `files.upload` | Upload attachments to records the actor can edit | 30 |

## 3. Seeded roles (policy data, editable)

| Permission ↓ / Role → | Requester | CostCenterManager | DepartmentHead | FinanceApprover | BudgetController | Buyer | ProcurementManager | Receiver | Auditor | Administrator |
|---|---|---|---|---|---|---|---|---|---|---|
| `access.users.manage`, `access.roles.manage` | | | | | | | | | | ✓ |
| `audit.read` | | | | | | | | | ✓ | ✓ |
| `organization.settings.manage`, `organization.manage` | | | | | | | | | | ✓ |
| `organization.read`, `catalog.read` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `suppliers.read` | | | | | | ✓ | ✓ | | ✓ | ✓ |
| `suppliers.manage` | | | | | | ✓ | ✓ | | | |
| `suppliers.status.change` | | | | | | | ✓ | | | |
| `catalog.manage` | | | | | | | ✓ | | | ✓ |
| `budget.read` | | ✓ | ✓ | ✓ | ✓ | | ✓ | | ✓ | |
| `budget.periods.manage`, `budget.allocations.manage` | | | | | ✓ | | | | | |
| `requisitions.create`, `requisitions.submit`, `requisitions.cancel-own` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | | |
| `requisitions.edit-any` | | | | | | | ✓ | | | |
| `requisitions.cancel-any` | | | | | | | ✓ | | | |
| `requisitions.read-team` | | ✓ | ✓ | | | | | | | |
| `requisitions.read-all` | | | | ✓ | ✓ | ✓ | ✓ | | ✓ | |
| `sourcing.rfqs.manage`, `sourcing.quotes.enter`, `sourcing.awards.submit`, `sourcing.rfqs.close-early` | | | | | | ✓ | ✓ | | | |
| `sourcing.quotes.read` | | | | | | ✓ | ✓ | | ✓ | |
| `sourcing.waivers.low-value` | | | | | | ✓ | ✓ | | | |
| `sourcing.waivers.direct-purchase`, `sourcing.waivers.insufficient-quotations`, `sourcing.waivers.non-leading`, `sourcing.rates.manual` | | | | | | | ✓ | | | |
| `approvals.decide`, `approvals.delegate` | | ✓ | ✓ | ✓ | | | ✓ | | | |
| `approvals.administer`, `approvals.policies.manage` | | | | | | | ✓ | | | |
| `approvals.read-all` | | | | ✓ | | | ✓ | | ✓ | |
| `purchasing.orders.read` | | | | | | ✓ | ✓ | | ✓ | |
| `purchasing.orders.manage`, `purchasing.orders.issue`, `purchasing.orders.cancel` | | | | | | ✓ | ✓ | | | |
| `purchasing.orders.short-close` | | | | | | | ✓ | | | |
| `receiving.receipts.create`, `receiving.receipts.read` | | | | | | | ✓ | ✓ | ✓ (read) | |
| `receiving.receipts.correct` | | | | | | | ✓ | ✓ | | |
| `reports.request`, `files.upload` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ (reports only) | |
| `reports.read-all` | | | | | | | | | ✓ | |

Notes:

- The PO cancellation of an *issued* PO is limited to `ProcurementManager` by assigning `purchasing.orders.cancel` scope rules in the use case: buyers may cancel `Draft` POs only (PUR-008). This is an ownership-style check in the use case, not a role check.
- Every role that can request also holds `requisitions.*` own permissions; approvers are also requesters for their own needs, which is exactly why APR-005..008 exist.
- `Administrator` deliberately holds no procurement decision permission (B23-9).

## 4. Separation-of-duty constraints

| ID | Constraint | Enforced by |
|---|---|---|
| SOD-1 | Requester cannot give the final approval of their own requisition (D23-13) | APR-005, APR-006 |
| SOD-2 | The same user cannot decide two levels of one approval instance | APR-007 |
| SOD-3 | The buyer who submitted the award cannot give the final approval | APR-008 |
| SOD-4 | A delegate must satisfy SOD-1..3 and cannot be the delegator | APR-010 |
| SOD-5 | Waivers require dedicated permissions; high-value and post-competition waivers are held only by `ProcurementManager` | Waiver catalog, SRC-006/017/018 |
| SOD-6 | Administrators manage access and master data but hold no budget, sourcing, approval, purchasing or receiving decision permission | Seeded roles, SEC-008 |
| SOD-7 | Budget allocations are managed by `BudgetController`, who does not hold sourcing or purchasing permissions | Seeded roles |
| SOD-8 | Audit data is read-only for everyone, including administrators | AUD-002 |

A role-permission change that would give one role both sides of SOD-5..7 is allowed (it is configuration) but is written to the audit trail with the affected constraint IDs so the release review can detect it. SOD-1..4 are invariants and cannot be weakened by configuration.

## 5. Authorization tests

Phase 24 adds a data-driven functional test that iterates the matrix above for each new endpoint: allowed role → not 403; every other role → 403 (or 404 for resource-scoped reads, REQ-011). Each later phase extends the data set for its endpoints.
