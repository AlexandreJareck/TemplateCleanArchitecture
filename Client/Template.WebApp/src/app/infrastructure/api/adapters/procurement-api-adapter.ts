import { inject, Injectable } from '@angular/core';
import { failure, success, type Result } from '@application/common/result';
import type { Page } from '@application/master-data/master-data';
import type {
  AttachmentInput,
  BudgetAllocation,
  BudgetAllocationInput,
  BudgetAvailability,
  BudgetPort,
  BudgetReservation,
  FiscalPeriod,
  FiscalPeriodInput,
  Money,
  Requisition,
  RequisitionAction,
  RequisitionDraftInput,
  RequisitionLineInput,
  RequisitionPort,
  RequisitionQuery,
  RequisitionStatus,
  RequisitionSummary,
  TimelineEntry,
} from '@application/procurement/procurement';
import { firstValueFrom, fromEvent, takeUntil, type Observable } from 'rxjs';
import { BudgetAllocationsService } from '../generated/budget-allocations/budget-allocations.service';
import { FiscalPeriodsService } from '../generated/fiscal-periods/fiscal-periods.service';
import type { BudgetAllocationView } from '../generated/models/budgetAllocationView';
import type { BudgetAvailabilityView } from '../generated/models/budgetAvailabilityView';
import type { BudgetReservationView } from '../generated/models/budgetReservationView';
import type { FiscalPeriodView } from '../generated/models/fiscalPeriodView';
import type { MoneyView } from '../generated/models/moneyView';
import type { RequisitionLineBody } from '../generated/models/requisitionLineBody';
import type { RequisitionSummaryView } from '../generated/models/requisitionSummaryView';
import type { RequisitionTimelineView } from '../generated/models/requisitionTimelineView';
import type { RequisitionView } from '../generated/models/requisitionView';
import { PurchaseRequisitionsService } from '../generated/purchase-requisitions/purchase-requisitions.service';
import { mapProblem } from '../../http/problem-mapper';

const ACTIONS: readonly RequisitionAction[] = [
  'edit',
  'discard',
  'attach',
  'submit',
  'withdraw',
  'cancel',
  'copy',
];

/**
 * Generated-client adapter of the Phase 27 contract. It sends the strong version as quoted `If-Match` on every change
 * of an existing aggregate and a fresh `Idempotency-Key` on creating and transition POSTs (api-guidelines §4/§6),
 * and maps transport DTOs and Problem Details into Application types. Totals and availability are copied, never computed.
 */
@Injectable({ providedIn: 'root' })
export class ProcurementApiAdapter implements RequisitionPort, BudgetPort {
  private readonly requisitions = inject(PurchaseRequisitionsService);
  private readonly allocations = inject(BudgetAllocationsService);
  private readonly periods = inject(FiscalPeriodsService);

  list(query: RequisitionQuery, signal?: AbortSignal) {
    return this.execute(
      this.requisitions.getApiV2PurchaseRequisitions({
        scope: query.scope,
        page: query.page,
        pageSize: query.pageSize,
        status: query.status || undefined,
        q: query.q || undefined,
        sort: query.sort,
      }),
      signal,
      (value) => mapPage(value, mapSummary),
    );
  }

  get(id: string, signal?: AbortSignal) {
    return this.execute(
      this.requisitions.getApiV2PurchaseRequisitionsId(id),
      signal,
      mapRequisition,
    );
  }

  history(id: string, signal?: AbortSignal) {
    return this.execute(
      this.requisitions.getApiV2PurchaseRequisitionsIdHistory(id),
      signal,
      mapTimeline,
    );
  }

  availability(id: string, signal?: AbortSignal) {
    return this.execute(
      this.requisitions.getApiV2PurchaseRequisitionsIdBudgetAvailability(id),
      signal,
      mapAvailability,
    );
  }

  create(input: RequisitionDraftInput, signal?: AbortSignal) {
    return this.execute(
      this.requisitions.postApiV2PurchaseRequisitions(draftBody(input), { headers: idempotency() }),
      signal,
      mapRequisition,
    );
  }

  update(requisition: Requisition, input: RequisitionDraftInput, signal?: AbortSignal) {
    return this.execute(
      this.requisitions.putApiV2PurchaseRequisitionsId(requisition.id, draftBody(input), {
        headers: ifMatch(requisition.version),
      }),
      signal,
      mapRequisition,
    );
  }

  discard(requisition: Requisition, signal?: AbortSignal): Promise<Result<void>> {
    return this.execute(
      this.requisitions.deleteApiV2PurchaseRequisitionsId(requisition.id, {
        headers: ifMatch(requisition.version),
      }),
      signal,
      () => undefined,
    );
  }

  addLine(requisition: Requisition, line: RequisitionLineInput, signal?: AbortSignal) {
    return this.execute(
      this.requisitions.postApiV2PurchaseRequisitionsIdLines(requisition.id, lineBody(line), {
        headers: { ...ifMatch(requisition.version), ...idempotency() },
      }),
      signal,
      mapRequisition,
    );
  }

  updateLine(
    requisition: Requisition,
    lineNumber: number,
    line: RequisitionLineInput,
    signal?: AbortSignal,
  ) {
    return this.execute(
      this.requisitions.putApiV2PurchaseRequisitionsIdLinesLineNumber(
        requisition.id,
        lineNumber,
        lineBody(line),
        { headers: ifMatch(requisition.version) },
      ),
      signal,
      mapRequisition,
    );
  }

  removeLine(requisition: Requisition, lineNumber: number, signal?: AbortSignal) {
    return this.execute(
      this.requisitions.deleteApiV2PurchaseRequisitionsIdLinesLineNumber(
        requisition.id,
        lineNumber,
        {
          headers: ifMatch(requisition.version),
        },
      ),
      signal,
      mapRequisition,
    );
  }

  addAttachment(requisition: Requisition, attachment: AttachmentInput, signal?: AbortSignal) {
    return this.execute(
      this.requisitions.postApiV2PurchaseRequisitionsIdAttachments(requisition.id, attachment, {
        headers: { ...ifMatch(requisition.version), ...idempotency() },
      }),
      signal,
      mapRequisition,
    );
  }

  removeAttachment(requisition: Requisition, attachmentId: string, signal?: AbortSignal) {
    return this.execute(
      this.requisitions.deleteApiV2PurchaseRequisitionsIdAttachmentsAttachmentId(
        requisition.id,
        attachmentId,
        { headers: ifMatch(requisition.version) },
      ),
      signal,
      mapRequisition,
    );
  }

  submit(requisition: Requisition, signal?: AbortSignal) {
    return this.transition(
      this.requisitions.postApiV2PurchaseRequisitionsIdSubmit(requisition.id, {
        headers: { ...ifMatch(requisition.version), ...idempotency() },
      }),
      signal,
    );
  }

  withdraw(requisition: Requisition, signal?: AbortSignal) {
    return this.transition(
      this.requisitions.postApiV2PurchaseRequisitionsIdWithdraw(requisition.id, {
        headers: { ...ifMatch(requisition.version), ...idempotency() },
      }),
      signal,
    );
  }

  cancel(requisition: Requisition, reason: string, signal?: AbortSignal) {
    return this.transition(
      this.requisitions.postApiV2PurchaseRequisitionsIdCancel(
        requisition.id,
        { reason },
        { headers: { ...ifMatch(requisition.version), ...idempotency() } },
      ),
      signal,
    );
  }

  copy(requisition: Requisition, signal?: AbortSignal) {
    return this.transition(
      this.requisitions.postApiV2PurchaseRequisitionsIdCopy(requisition.id, {
        headers: idempotency(),
      }),
      signal,
    );
  }

  listPeriods(request: { page: number; pageSize: number }, signal?: AbortSignal) {
    return this.execute(
      this.periods.getApiV2FiscalPeriods({ page: request.page, pageSize: request.pageSize }),
      signal,
      (value) => mapPage(value, mapPeriod),
    );
  }

  createPeriod(input: FiscalPeriodInput, signal?: AbortSignal) {
    return this.execute(
      this.periods.postApiV2FiscalPeriods(input, { headers: idempotency() }),
      signal,
      mapPeriod,
    );
  }

  changePeriodStatus(period: FiscalPeriod, transition: 'open' | 'close', signal?: AbortSignal) {
    const options = { headers: { ...ifMatch(period.version), ...idempotency() } };
    return this.execute(
      transition === 'open'
        ? this.periods.postApiV2FiscalPeriodsIdOpen(period.id, options)
        : this.periods.postApiV2FiscalPeriodsIdClose(period.id, options),
      signal,
      mapPeriod,
    );
  }

  listAllocations(
    request: { page: number; pageSize: number; fiscalPeriodId?: string },
    signal?: AbortSignal,
  ) {
    return this.execute(
      this.allocations.getApiV2BudgetAllocations({
        page: request.page,
        pageSize: request.pageSize,
        fiscalPeriodId: request.fiscalPeriodId || undefined,
      }),
      signal,
      (value) => mapPage(value, mapAllocation),
    );
  }

  getAllocation(id: string, signal?: AbortSignal) {
    return this.execute(this.allocations.getApiV2BudgetAllocationsId(id), signal, mapAllocation);
  }

  createAllocation(input: BudgetAllocationInput, signal?: AbortSignal) {
    return this.execute(
      this.allocations.postApiV2BudgetAllocations(input, { headers: idempotency() }),
      signal,
      mapAllocation,
    );
  }

  changeAllocationAmount(
    allocation: BudgetAllocation,
    amount: string,
    reason: string,
    signal?: AbortSignal,
  ) {
    return this.execute(
      this.allocations.postApiV2BudgetAllocationsIdAmountChanges(
        allocation.id,
        { amount, reason },
        { headers: { ...ifMatch(allocation.version), ...idempotency() } },
      ),
      signal,
      mapAllocation,
    );
  }

  closeAllocation(allocation: BudgetAllocation, reason: string, signal?: AbortSignal) {
    return this.execute(
      this.allocations.postApiV2BudgetAllocationsIdClose(
        allocation.id,
        { reason },
        { headers: { ...ifMatch(allocation.version), ...idempotency() } },
      ),
      signal,
      mapAllocation,
    );
  }

  listReservations(
    allocationId: string,
    request: { page: number; pageSize: number },
    signal?: AbortSignal,
  ) {
    return this.execute(
      this.allocations.getApiV2BudgetAllocationsIdReservations(allocationId, {
        page: request.page,
        pageSize: request.pageSize,
      }),
      signal,
      (value) => mapPage(value, mapReservation),
    );
  }

  private transition(source: Observable<RequisitionView>, signal?: AbortSignal) {
    return this.execute(source, signal, mapRequisition);
  }

  private async execute<T, U>(
    source: Observable<T>,
    signal: AbortSignal | undefined,
    map: (value: T) => U,
  ): Promise<Result<U>> {
    try {
      const bounded = signal ? source.pipe(takeUntil(fromEvent(signal, 'abort'))) : source;
      return success(map(await firstValueFrom(bounded)));
    } catch (error) {
      return failure<U>(mapProblem(error, signal));
    }
  }
}

interface PageEnvelope<T> {
  items?: T[] | null;
  page?: number;
  pageSize?: number;
  totalItems?: number;
  totalPages?: number;
}

export function mapPage<T, U>(value: PageEnvelope<T>, map: (item: T) => U): Page<U> {
  return {
    items: (value.items ?? []).map(map),
    page: value.page ?? 1,
    pageSize: value.pageSize ?? 20,
    totalItems: value.totalItems ?? 0,
    totalPages: value.totalPages ?? 0,
  };
}

function money(value: MoneyView | null | undefined): Money {
  return { amount: value?.amount ?? '0.00', currency: value?.currency ?? '' };
}

function optionalMoney(value: MoneyView | null | undefined): Money | undefined {
  return value ? money(value) : undefined;
}

export function mapSummary(value: RequisitionSummaryView): RequisitionSummary {
  return {
    id: value.id ?? '',
    number: value.number ?? '',
    costCenter: {
      id: value.costCenter?.id ?? '',
      code: value.costCenter?.code ?? '',
      name: value.costCenter?.name ?? '',
    },
    neededBy: value.neededBy ?? undefined,
    estimatedTotal: money(value.estimatedTotal),
    status: (value.status ?? 'Draft') as RequisitionStatus,
    revision: value.revision ?? 1,
    createdAt: value.createdAt ?? '',
    version: value.version ?? '',
  };
}

export function mapRequisition(value: RequisitionView): Requisition {
  return {
    ...mapSummary(value),
    requesterId: value.requesterId ?? '',
    purpose: value.purpose ?? undefined,
    currency: value.currency ?? '',
    sourceRequisitionId: value.sourceRequisitionId ?? undefined,
    submittedAt: value.submittedAt ?? undefined,
    lines: (value.lines ?? []).map((line) => ({
      lineNumber: line.lineNumber ?? 0,
      catalogItemId: line.catalogItemId ?? undefined,
      description: line.description ?? '',
      kind: line.kind === 'Service' ? 'Service' : 'Good',
      categoryId: line.categoryId ?? '',
      categoryCode: line.categoryCode ?? '',
      unitOfMeasureId: line.unitOfMeasureId ?? '',
      unitCode: line.unitCode ?? '',
      quantity: line.quantity ?? '0',
      estimatedUnitPrice: money(line.estimatedUnitPrice),
      lineTotal: money(line.lineTotal),
    })),
    attachments: (value.attachments ?? []).map((attachment) => ({
      id: attachment.id ?? '',
      fileName: attachment.fileName ?? '',
      mediaType: attachment.mediaType ?? '',
      length: attachment.length ?? 0,
      status: attachment.status ?? '',
      locked: attachment.lockedAtRevision !== null && attachment.lockedAtRevision !== undefined,
    })),
    submissions: (value.submissions ?? []).map((submission) => ({
      revision: submission.revision ?? 0,
      submittedAt: submission.submittedAt ?? '',
      estimatedTotal: money(submission.estimatedTotal),
      lineCount: submission.lines?.length ?? 0,
    })),
    allowedActions: (value.allowedActions ?? []).filter((action): action is RequisitionAction =>
      ACTIONS.includes(action as RequisitionAction),
    ),
  };
}

function mapTimeline(value: RequisitionTimelineView): readonly TimelineEntry[] {
  return (value.entries ?? []).map((entry) => ({
    kind: entry.kind === 'reservation' ? 'reservation' : 'state',
    at: entry.at ?? '',
    actorId: entry.actorId ?? undefined,
    from: entry.from ?? undefined,
    to: entry.to ?? '',
    revision: entry.revision ?? undefined,
    reason: entry.reason ?? undefined,
    reasonCode: entry.reasonCode ?? undefined,
    amount: optionalMoney(entry.amount),
  }));
}

function mapAvailability(value: BudgetAvailabilityView): BudgetAvailability {
  return {
    hasOpenAllocation: value.hasOpenAllocation ?? false,
    asOfDate: value.asOfDate ?? '',
    fiscalPeriodCode: value.fiscalPeriodCode ?? undefined,
    allocationId: value.allocationId ?? undefined,
    amount: optionalMoney(value.amount),
    reserved: optionalMoney(value.reserved),
    committed: optionalMoney(value.committed),
    available: optionalMoney(value.available),
  };
}

function mapPeriod(value: FiscalPeriodView): FiscalPeriod {
  const status = value.status === 'Open' || value.status === 'Closed' ? value.status : 'Planned';
  return {
    id: value.id ?? '',
    code: value.code ?? '',
    name: value.name ?? '',
    startDate: value.startDate ?? '',
    endDate: value.endDate ?? '',
    status,
    version: value.version ?? '',
  };
}

function mapAllocation(value: BudgetAllocationView): BudgetAllocation {
  return {
    id: value.id ?? '',
    fiscalPeriodId: value.fiscalPeriodId ?? '',
    costCenterId: value.costCenterId ?? '',
    currency: value.currency ?? '',
    amount: money(value.amount),
    reserved: money(value.reserved),
    committed: money(value.committed),
    available: money(value.available),
    status: value.status === 'Closed' ? 'Closed' : 'Active',
    version: value.version ?? '',
    amountHistory: (value.amountHistory ?? []).map((change) => ({
      sequence: change.sequence ?? 0,
      previousAmount: money(change.previousAmount),
      newAmount: money(change.newAmount),
      reason: change.reason ?? '',
      at: change.at ?? '',
    })),
  };
}

function mapReservation(value: BudgetReservationView): BudgetReservation {
  const status =
    value.status === 'Committed' || value.status === 'Released' ? value.status : 'Reserved';
  return {
    id: value.id ?? '',
    requisitionId: value.requisitionId ?? '',
    requisitionRevision: value.requisitionRevision ?? 1,
    amount: money(value.amount),
    status,
    releaseReason: value.releaseReason ?? undefined,
    createdAt: value.createdAt ?? '',
    releasedAt: value.releasedAt ?? undefined,
  };
}

function draftBody(input: RequisitionDraftInput) {
  return {
    costCenterId: input.costCenterId,
    currency: input.currency,
    purpose: input.purpose.trim() || null,
    neededBy: input.neededBy || null,
  };
}

function lineBody(line: RequisitionLineInput): RequisitionLineBody {
  return {
    catalogItemId: line.catalogItemId || null,
    description: line.description,
    kind: line.kind,
    categoryId: line.categoryId || null,
    unitOfMeasureId: line.unitOfMeasureId || null,
    quantity: line.quantity.trim(),
    estimatedUnitPrice: { amount: line.unitPrice.trim(), currency: line.currency },
  };
}

function ifMatch(version: string) {
  return { 'If-Match': `"${version}"` };
}

function idempotency() {
  return { 'Idempotency-Key': crypto.randomUUID() };
}
