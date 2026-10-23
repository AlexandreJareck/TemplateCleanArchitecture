# Event catalog

## 1. Domain events versus integration events

| Aspect | Domain event | Integration event |
|---|---|---|
| Purpose | Signal a fact inside one transaction to in-process handlers (audit write, Outbox mapping, cross-aggregate update in the same use case) | Notify other processes (Worker, WorkflowFunctions, projections, notifications) |
| Defined in | Domain (`<Module>/Events`), framework-free | Application (`Messaging/<Module>/`), versioned contract types |
| Transport | None (dispatched by the Application unit of work before commit) | Outbox row in the same transaction → Worker dispatcher → RabbitMQ (D23-8) |
| Versioning | Internal; may change with the code | Name carries the version (`.v1`); breaking change = new version published side by side until consumers migrate |
| Delivery | Exactly once with the transaction | At least once; consumers idempotent through the Inbox (INT-003) |

Domain events are named in past tense without version (`RequisitionSubmitted`). Each integration event below is produced by mapping one domain event.

## 2. Envelope (all integration events)

```json
{
  "eventId": "uuid",
  "eventType": "procurement.requisition.submitted",
  "eventVersion": 1,
  "occurredAtUtc": "2027-03-01T13:00:00Z",
  "correlationId": "string",
  "causationId": "uuid|null",
  "aggregateType": "PurchaseRequisition",
  "aggregateId": "uuid",
  "aggregateVersion": "string",
  "payload": { }
}
```

- `eventId` is generated when the Outbox row is written and never changes on redelivery.
- Payloads contain IDs, states, numbers and amounts needed by consumers; never personal contact data, free-text descriptions, attachment content, tokens or full entities (NFR-SEC-7).
- RabbitMQ: topic exchange `procurement.events` (new, separate from the existing sample order exchanges), routing key = `eventType`; each consumer owns a durable queue with its dead-letter queue; retry/dead-letter behavior follows the existing messaging conventions (bounded attempts, then dead-letter with reason). Exact topology is fixed in Phase 28.
- Ordering: guaranteed only per aggregate within one Outbox dispatcher partition (`aggregateId`); consumers check `aggregateVersion` or state and ignore stale events.
- The existing sample `OrderPlacedEvent` v1 and its topology are unchanged.

## 3. Integration events

Idempotency key for every consumer = (consumer name, `eventId`) in the Inbox. The "Business key" column is the additional natural key the consumer's use case uses to stay idempotent if the same fact is published twice with different event IDs.

| Event type (v1) | Producer (module / use case) | Payload (minimal) | Consumers | Business key | Phase |
|---|---|---|---|---|---|
| `procurement.requisition.submitted` | Requisitions / Submit | requisitionId, number, revision, costCenterId, estimatedTotal, neededBy | Notifications (buyers queue), projections | requisitionId + revision | 28 |
| `procurement.requisition.withdrawn` | Requisitions / Withdraw | requisitionId, revision | Notifications | requisitionId + revision | 28 |
| `procurement.requisition.cancelled` | Requisitions / Cancel | requisitionId, reasonCode, cancelledBy | Sourcing (cancel open RFQ), Approvals (cancel instance), Notifications | requisitionId | 28 |
| `procurement.requisition.expired` | Requisitions / Expire | requisitionId | Sourcing, Notifications | requisitionId | 33 (event in 28) |
| `procurement.budget.reservation-created` | Budgeting (inside Submit) | reservationId, requisitionId, allocationId, amount | Projections, reports | reservationId | 28 |
| `procurement.budget.reservation-adjusted` | Budgeting (inside Award) | reservationId, requisitionId, awardId, previousAmount, newAmount | Projections | reservationId + awardId | 29 |
| `procurement.budget.reservation-committed` | Budgeting (inside final approval) | reservationId, amount | Projections, reports | reservationId | 31 |
| `procurement.budget.reservation-released` | Budgeting | reservationId, releasedAmount, reasonCode | Projections, notifications | reservationId + reasonCode (+ poId for partial) | 28 |
| `procurement.rfq.published` | Sourcing / Publish | rfqId, number, requisitionId, deadline, invitedSupplierIds, sourcingMethod | Notifications (invitation emails) | rfqId | 29 |
| `procurement.rfq.deadline-extended` | Sourcing / Extend | rfqId, previousDeadline, newDeadline, invitedSupplierIds | Notifications, scheduler | rfqId + newDeadline | 29 |
| `procurement.rfq.reminder-requested` | Sourcing / Remind (D29-6) | rfqId, deadline, supplierIds (suppliers without a current quote) | Notifications (reminder) | eventId (one reminder per request) | 29 |
| `procurement.rfq.closed` | Sourcing / Close | rfqId, closedAt, validQuoteCount, comparisonStatus | Notifications (buyer), report projections | rfqId | 29 |
| `procurement.rfq.cancelled` | Sourcing / Cancel | rfqId, reasonCode | Approvals (cancel pending), Notifications | rfqId | 29 |
| `procurement.quote.recorded` | Sourcing / Enter or revise quote | rfqId, quoteId, revision, supplierId | Projections | quoteId + revision | 29 |
| `procurement.award.submitted` | Sourcing / Award | awardId, rfqId, requisitionId, supplierId, normalizedTotal, policyRef | WorkflowFunctions starter (APR-001), Notifications | awardId | 29 (consumed in 31) |
| `procurement.approval.started` | Approvals / Start activity | approvalInstanceId, awardId, requiredLevels, policyVersion | Notifications | approvalInstanceId | 31 |
| `procurement.approval.step-assigned` | Approvals | approvalInstanceId, level, assigneeUserId or assigneeRole, dueAt | Notifications (approver email) | approvalInstanceId + level + assignment sequence | 31 |
| `procurement.approval.reminder-due` | Approvals (timer activity) | approvalInstanceId, level | Notifications | approvalInstanceId + level + reminder n | 31 |
| `procurement.approval.step-escalated` | Approvals (timer activity) | approvalInstanceId, level, escalationCount | Notifications | approvalInstanceId + level + escalation n | 31 |
| `procurement.approval.completed` | Approvals / Final approval | approvalInstanceId, awardId, requisitionId | Purchasing (create PO draft, PUR-001), Notifications | approvalInstanceId | 31 |
| `procurement.approval.rejected` | Approvals / Reject | approvalInstanceId, level, requisitionId | Notifications | approvalInstanceId | 31 |
| `procurement.approval.expired` | Approvals / Expire | approvalInstanceId, requisitionId | Notifications | approvalInstanceId | 31 |
| `procurement.approval.invalidated` | Approvals / Invalidate | approvalInstanceId, cause | WorkflowFunctions (terminate orchestration), Notifications | approvalInstanceId + cause | 31 |
| `procurement.approval.cancelled` | Approvals / Cancel | approvalInstanceId, cause | WorkflowFunctions, Notifications | approvalInstanceId | 31 |
| `procurement.supplier.status-changed` | Suppliers / Change status | supplierId, previousStatus, newStatus | Approvals (APR-012 invalidation), Sourcing warnings | supplierId + status-change sequence | 25 (event), 28 (outbox), 31 (consumer) |
| `procurement.purchase-order.drafted` | Purchasing / Create draft | purchaseOrderId, awardId | Notifications (buyer) | awardId | 32 |
| `procurement.purchase-order.issued` | Purchasing / Issue | purchaseOrderId, number, supplierId, total | Reporting (PO PDF), Notifications (supplier email) | purchaseOrderId | 32 |
| `procurement.purchase-order.cancelled` | Purchasing / Cancel | purchaseOrderId, reasonCode | Notifications | purchaseOrderId | 32 |
| `procurement.purchase-order.completed` | Purchasing / Recompute | purchaseOrderId, shortClosed | Requisitions (close), Notifications | purchaseOrderId + completion sequence | 32 |
| `procurement.purchase-order.reopened` | Purchasing / Recompute | purchaseOrderId | Requisitions | purchaseOrderId + completion sequence | 32 |
| `procurement.receipt.posted` | Receiving / Post | receiptId, purchaseOrderId, lineCount, hasDiscrepancy | Purchasing recompute (same transaction; event for projections), Notifications | receiptId | 32 |
| `procurement.receipt.corrected` | Receiving / Correct | receiptId, correctionId | Projections | correctionId | 32 |
| `procurement.report.requested` | Reporting / Request | reportJobId, reportType, requestedBy | Projections (the Worker claims jobs from the job table, D30-1) | reportJobId | 30 |
| `procurement.report.ready` / `procurement.report.failed` | Worker report handler | reportJobId, reportType, requestedBy, failureCode? | Notifications (in-app, requester only, D30-11), projections | reportJobId | 30 |

Notification consumers produce `NotificationRequested` commands to a local adapter (fake until Phase 33, Mailpit in Phase 33); templates are versioned and localized; delivery records are idempotent per (event, recipient, template). Phase 29 implements this for sourcing: the Worker consumer `procurement-notification-v1` on queue `procurement.notifications.v1` (bound to `procurement.rfq.published`, `procurement.rfq.deadline-extended` and `procurement.rfq.reminder-requested`) records one `MessagingNotificationDeliveries` row per invited supplier and template (`sourcing.rfq-invitation.v1`, `sourcing.rfq-deadline-extended.v1`, `sourcing.rfq-reminder.v1`) in the Inbox transaction (D29-8).

## 4. Commands that cross process boundaries

| Command | Sender | Receiver | Mechanism | Idempotency |
|---|---|---|---|---|
| Start approval orchestration | Worker (on `award.submitted`) | WorkflowFunctions | Application `IApprovalWorkflowClient` port; Infrastructure adapter calls the Functions boundary with an authenticated internal mechanism (ADR-0003) | Deterministic instance ID = award ID |
| Raise approval decision event | WebApi (after the decision use case commits) | WorkflowFunctions | Same port | Event carries decision ID; orchestration ignores repeats |
| Terminate orchestration | Worker (on invalidated/cancelled) | WorkflowFunctions | Same port | Terminating an ended instance is a no-op |
| Run scheduled sweeps (RFQ close, requisition expiry, cleanup) | Scheduler (Functions timer per ADR-0003, Phase 33) | Application use cases through the Worker | Message or direct activity call | Sweeps are idempotent per (record, due instant) |

## 5. Contract tests

Each integration event has: a serializer round-trip test, a byte-level JSON contract test of v1 (as done for the sample `OrderPlacedEvent`), a consumer duplicate-delivery test (INT-003) and, from Phase 28, a real-broker test for routing and dead-letter.
