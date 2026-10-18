# Ubiquitous language

Terms are written in English in code, contracts, events and technical documents (D23-10). The pt-BR column is the user-facing label used in localization resources; it is an example, not a contract. A term means exactly one thing; when two concepts look alike, both are defined and the difference is stated.

## Organization and master data

| Term | Definition | pt-BR label |
|---|---|---|
| Organization | The single company served by an installation (D23-1). Holds settings: base currency (BRL), time zone (`America/Sao_Paulo`), display name. | Organização |
| Department | Organizational unit with a stable code, a name, an optional head user and an active flag. | Departamento |
| Cost center | Accounting unit owned by one department, with a stable code, a manager user and an active flag. Budgets are allocated to cost centers. | Centro de custo |
| User | An authenticated person (ASP.NET Core Identity account) referenced by `UserId` everywhere else. | Usuário |
| Role | A named group of permissions assigned to users. Never checked directly by business code. | Perfil |
| Permission | Stable string that authorizes an actor to *attempt* an operation (for example `requisitions.submit`). Distinct from a business-rule ID. | Permissão |
| Supplier | Company that can be invited, selected and ordered from. Has a lifecycle (`Draft`, `Active`, `Suspended`, `Inactive`). | Fornecedor |
| Eligible supplier | A supplier whose status is `Active` at the moment of a new commitment (invitation, award, PO issue). | Fornecedor elegível |
| Catalog item | Reusable description of a good or service with code, kind (`Good`/`Service`), category and unit of measure. | Item de catálogo |
| Category | Controlled classification of catalog items and suppliers. | Categoria |
| Unit of measure | Controlled unit (code, name, decimal places allowed). | Unidade de medida |
| Enrichment | Optional advisory data fetched from an external provider (BrasilAPI, BCB). Never authoritative and never required. | Enriquecimento |

## Money and budget

| Term | Definition | pt-BR label |
|---|---|---|
| Money | Value object `{ amount: decimal, currency: ISO 4217 code }`. Amounts in different currencies are never added or compared directly. | Valor monetário |
| Base currency | The organization currency (BRL) used to normalize cross-currency comparisons and approval thresholds. | Moeda base |
| Exchange-rate snapshot | Persisted rate with source (`BcbPtax` or `Manual`), quote date, rate and retrieval instant, used for one normalization. | Cotação registrada |
| Fiscal period | A named interval of local dates (for example `FY2027`) with status `Planned`, `Open` or `Closed`. Only `Open` periods accept reservations. | Período fiscal |
| Budget allocation | Amount of money assigned to one cost center, in one currency, for one fiscal period. | Dotação orçamentária |
| Available budget | Allocation amount minus the sum of active reservations (`Reserved` and `Committed`). | Saldo disponível |
| Budget reservation | Earmark of part of an allocation for one requisition. Created `Reserved` at submission, becomes `Committed` at final approval, or `Released`. | Reserva orçamentária |
| Commitment | A reservation in state `Committed`: money promised to an approved award. | Compromisso |
| Release | Returning reserved or committed money to availability, exactly once, with a reason code. | Liberação |

## Requisition

| Term | Definition | pt-BR label |
|---|---|---|
| Purchase requisition | Internal request for goods or services with requester, cost center, purpose, needed-by date, currency and lines. | Requisição de compra |
| Requisition line | One requested item: optional catalog reference plus description, quantity, unit, estimated unit price and category snapshot. | Item da requisição |
| Estimated total | Server-calculated sum of line totals of a requisition (CALC-002). | Total estimado |
| Revision | Integer incremented each time a requisition returns to `Draft` (withdrawal). Submitted snapshots keep their revision. | Revisão |
| Submission snapshot | Immutable copy of the requisition content at submission. | Retrato da submissão |
| Withdrawal | Requester action returning a `Submitted` requisition to `Draft` before an RFQ is published. | Retirada |
| Cancellation | Terminal ending of a requisition by an authorized actor, with reason. | Cancelamento |
| Expiry | Terminal ending caused by time (needed-by date passed without award, or approval timeout). | Expiração |

## Sourcing

| Term | Definition | pt-BR label |
|---|---|---|
| Request for quotation (RFQ) | A quotation round for one submitted requisition: invited suppliers, deadline, sourcing method and quotes. Round number increments if a previous RFQ for the same requisition was cancelled. | Cotação (RFQ) |
| Sourcing method | `Competitive` (minimum invitations apply) or `Direct` (one supplier, requires a direct-purchase waiver). | Modalidade |
| Invitation | Record that a supplier was invited to an RFQ, with the supplier status snapshot at invitation time. | Convite |
| Quote | One supplier's offer for an RFQ, entered by a buyer (D23-3). Has revisions; only the latest non-withdrawn revision is current. | Proposta |
| Quote revision | Immutable version of a quote. Revising creates a new revision; the old one becomes `Superseded`. | Revisão da proposta |
| Valid quote | Current revision, received before the deadline, not expired, from a supplier eligible at close, covering every RFQ line (SRC-014). | Proposta válida |
| Comparison | Immutable snapshot computed at RFQ close: normalized totals, rank, coverage, warnings, rate provenance. | Mapa comparativo |
| Leading quote | Rank 1 quote in the comparison (CALC-005). | Proposta vencedora (melhor classificada) |
| Award | Buyer's selection of one valid quote for the whole RFQ (D23-15), submitted for approval. | Adjudicação |
| Waiver | Explicit, permission-protected exception decision with rule ID, actor, reason, timestamp and scope. Never a boolean flag. | Exceção autorizada |

## Approval

| Term | Definition | pt-BR label |
|---|---|---|
| Approval policy | Versioned configuration of amount thresholds, levels, approver resolution and deadlines. | Política de aprovação |
| Approval instance | One execution of the approval policy for one award, pinned to one policy version. | Processo de aprovação |
| Approval step | One level of an approval instance with an assignee (user or role group), deadline, reminders and decision. | Etapa de aprovação |
| Approver | User currently assigned to a step who holds `approvals.decide`. | Aprovador |
| Delegation | Reassignment of a pending step by its approver to another eligible user, with reason. | Delegação |
| Escalation | Automatic reassignment of an overdue step to the escalation assignee defined by policy. | Escalonamento |
| Final approval | Decision on the last required step; confirms the budget (D23-4). | Aprovação final |
| Invalidation | Ending a pending approval because a material input changed (supplier suspended, award withdrawn). | Invalidação |

## Purchasing and receiving

| Term | Definition | pt-BR label |
|---|---|---|
| Purchase order (PO) | Commercial commitment issued to the awarded supplier, created once from one approved award, with an immutable commercial snapshot. | Pedido de compra |
| PO number | Unique human-readable identifier `PO-{yyyy}-{nnnnnn}` allocated at issue (PUR-005). | Número do pedido |
| Issue | Transition making a PO binding and triggering its document and supplier notification. | Emissão |
| Receipt | Record of goods or services received against an issued PO, by a receiver, with per-line received, accepted and rejected quantities. | Recebimento |
| Discrepancy | Difference recorded on a receipt line (rejected quantity, damage, wrong item, other) with notes. | Divergência |
| Receipt correction | Compensating record that adjusts a posted receipt line by a signed quantity with reason (D23-18). | Correção de recebimento |
| Over-receipt tolerance | Policy percentage allowing accepted quantity above ordered quantity; 0 % in policy v1. | Tolerância de recebimento |
| Short close | Permission-protected exception closing a PO line set as complete although less was received. | Encerramento com saldo |

## Reporting, files and audit

| Term | Definition | pt-BR label |
|---|---|---|
| Report job | Asynchronous request to generate one report with a parameter snapshot; states `Pending`, `Running`, `Ready`, `Failed`, `Expired`, `Cancelled`. | Solicitação de relatório |
| Stored file | Metadata of bytes held in blob storage: owner resource, name, media type, length, SHA-256, classification, expiry. | Arquivo armazenado |
| Attachment | Stored file linked to a business record (requisition, quote, receipt). | Anexo |
| Audit record | Append-only entry of who did what, when, to which record, with previous/new state, reason and correlation ID. | Registro de auditoria |
| Domain event | In-process fact raised by an aggregate, handled in the same transaction. | Evento de domínio |
| Integration event | Versioned message written to the Outbox and published through RabbitMQ. | Evento de integração |
