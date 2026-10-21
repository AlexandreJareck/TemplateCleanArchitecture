import type { Result } from '../common/result';
import type { Page } from '../master-data/master-data';
import { fitsAvailability, previewLineTotal, previewTotal } from '../../domain/requisition-preview';

/** Money on the wire: the amount is a decimal string (never a binary float). */
export interface Money {
  readonly amount: string;
  readonly currency: string;
}

export type FiscalPeriodStatus = 'Planned' | 'Open' | 'Closed';

export interface FiscalPeriod {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly status: FiscalPeriodStatus;
  readonly version: string;
}

export interface FiscalPeriodInput {
  readonly code: string;
  readonly name: string;
  readonly startDate: string;
  readonly endDate: string;
}

export interface AmountChange {
  readonly sequence: number;
  readonly previousAmount: Money;
  readonly newAmount: Money;
  readonly reason: string;
  readonly at: string;
}

/** Server-computed totals (CALC-003); the client never derives availability. */
export interface BudgetAllocation {
  readonly id: string;
  readonly fiscalPeriodId: string;
  readonly costCenterId: string;
  readonly currency: string;
  readonly amount: Money;
  readonly reserved: Money;
  readonly committed: Money;
  readonly available: Money;
  readonly status: 'Active' | 'Closed';
  readonly version: string;
  readonly amountHistory: readonly AmountChange[];
}

export interface BudgetAllocationInput {
  readonly fiscalPeriodId: string;
  readonly costCenterId: string;
  readonly currency: string;
  readonly amount: string;
}

export interface BudgetReservation {
  readonly id: string;
  readonly requisitionId: string;
  readonly requisitionRevision: number;
  readonly amount: Money;
  readonly status: 'Reserved' | 'Committed' | 'Released';
  readonly releaseReason?: string;
  readonly createdAt: string;
  readonly releasedAt?: string;
}

export interface BudgetAvailability {
  readonly hasOpenAllocation: boolean;
  readonly asOfDate: string;
  readonly fiscalPeriodCode?: string;
  readonly allocationId?: string;
  readonly amount?: Money;
  readonly reserved?: Money;
  readonly committed?: Money;
  readonly available?: Money;
}

export type RequisitionStatus =
  | 'Draft'
  | 'Submitted'
  | 'InSourcing'
  | 'PendingApproval'
  | 'Approved'
  | 'Ordered'
  | 'Closed'
  | 'Rejected'
  | 'Cancelled'
  | 'Expired';

export type RequisitionAction =
  'edit' | 'discard' | 'attach' | 'submit' | 'withdraw' | 'cancel' | 'copy';

export interface CostCenterRef {
  readonly id: string;
  readonly code: string;
  readonly name: string;
}

export interface RequisitionLine {
  readonly lineNumber: number;
  readonly catalogItemId?: string;
  readonly description: string;
  readonly kind: 'Good' | 'Service';
  readonly categoryId: string;
  readonly categoryCode: string;
  readonly unitOfMeasureId: string;
  readonly unitCode: string;
  readonly quantity: string;
  readonly estimatedUnitPrice: Money;
  readonly lineTotal: Money;
}

export interface RequisitionAttachment {
  readonly id: string;
  readonly fileName: string;
  readonly mediaType: string;
  readonly length: number;
  readonly status: string;
  readonly locked: boolean;
}

export interface RequisitionSubmission {
  readonly revision: number;
  readonly submittedAt: string;
  readonly estimatedTotal: Money;
  readonly lineCount: number;
}

export interface RequisitionSummary {
  readonly id: string;
  readonly number: string;
  readonly costCenter: CostCenterRef;
  readonly neededBy?: string;
  readonly estimatedTotal: Money;
  readonly status: RequisitionStatus;
  readonly revision: number;
  readonly createdAt: string;
  readonly version: string;
}

export interface Requisition extends RequisitionSummary {
  readonly requesterId: string;
  readonly purpose?: string;
  readonly currency: string;
  readonly sourceRequisitionId?: string;
  readonly submittedAt?: string;
  readonly lines: readonly RequisitionLine[];
  readonly attachments: readonly RequisitionAttachment[];
  readonly submissions: readonly RequisitionSubmission[];
  readonly allowedActions: readonly RequisitionAction[];
}

export interface TimelineEntry {
  readonly kind: 'state' | 'reservation';
  readonly at: string;
  readonly actorId?: string;
  readonly from?: string;
  readonly to: string;
  readonly revision?: number;
  readonly reason?: string;
  readonly reasonCode?: string;
  readonly amount?: Money;
}

export interface RequisitionDraftInput {
  readonly costCenterId: string;
  readonly currency: string;
  readonly purpose: string;
  readonly neededBy: string;
}

export interface RequisitionLineInput {
  readonly catalogItemId?: string;
  readonly description: string;
  readonly kind: 'Good' | 'Service';
  readonly categoryId: string;
  readonly unitOfMeasureId: string;
  readonly quantity: string;
  readonly unitPrice: string;
  readonly currency: string;
}

export interface AttachmentInput {
  readonly fileName: string;
  readonly mediaType: string;
  readonly length: number;
}

export type RequisitionScope = 'mine' | 'visible';

export interface RequisitionQuery {
  readonly scope: RequisitionScope;
  readonly page: number;
  readonly pageSize: number;
  readonly status?: string;
  readonly q?: string;
  readonly sort?: string;
}

/** Client use-case needs for requisitions; every write carries the version it was read with. */
export interface RequisitionPort {
  list(query: RequisitionQuery, signal?: AbortSignal): Promise<Result<Page<RequisitionSummary>>>;
  get(id: string, signal?: AbortSignal): Promise<Result<Requisition>>;
  history(id: string, signal?: AbortSignal): Promise<Result<readonly TimelineEntry[]>>;
  availability(id: string, signal?: AbortSignal): Promise<Result<BudgetAvailability>>;
  create(input: RequisitionDraftInput, signal?: AbortSignal): Promise<Result<Requisition>>;
  update(
    requisition: Requisition,
    input: RequisitionDraftInput,
    signal?: AbortSignal,
  ): Promise<Result<Requisition>>;
  discard(requisition: Requisition, signal?: AbortSignal): Promise<Result<void>>;
  addLine(
    requisition: Requisition,
    line: RequisitionLineInput,
    signal?: AbortSignal,
  ): Promise<Result<Requisition>>;
  updateLine(
    requisition: Requisition,
    lineNumber: number,
    line: RequisitionLineInput,
    signal?: AbortSignal,
  ): Promise<Result<Requisition>>;
  removeLine(
    requisition: Requisition,
    lineNumber: number,
    signal?: AbortSignal,
  ): Promise<Result<Requisition>>;
  addAttachment(
    requisition: Requisition,
    attachment: AttachmentInput,
    signal?: AbortSignal,
  ): Promise<Result<Requisition>>;
  removeAttachment(
    requisition: Requisition,
    attachmentId: string,
    signal?: AbortSignal,
  ): Promise<Result<Requisition>>;
  submit(requisition: Requisition, signal?: AbortSignal): Promise<Result<Requisition>>;
  withdraw(requisition: Requisition, signal?: AbortSignal): Promise<Result<Requisition>>;
  cancel(
    requisition: Requisition,
    reason: string,
    signal?: AbortSignal,
  ): Promise<Result<Requisition>>;
  copy(requisition: Requisition, signal?: AbortSignal): Promise<Result<Requisition>>;
}

/** Client use-case needs for budget administration and visibility (BUD-001..012). */
export interface BudgetPort {
  listPeriods(
    request: { page: number; pageSize: number },
    signal?: AbortSignal,
  ): Promise<Result<Page<FiscalPeriod>>>;
  createPeriod(input: FiscalPeriodInput, signal?: AbortSignal): Promise<Result<FiscalPeriod>>;
  changePeriodStatus(
    period: FiscalPeriod,
    transition: 'open' | 'close',
    signal?: AbortSignal,
  ): Promise<Result<FiscalPeriod>>;
  listAllocations(
    request: { page: number; pageSize: number; fiscalPeriodId?: string },
    signal?: AbortSignal,
  ): Promise<Result<Page<BudgetAllocation>>>;
  getAllocation(id: string, signal?: AbortSignal): Promise<Result<BudgetAllocation>>;
  createAllocation(
    input: BudgetAllocationInput,
    signal?: AbortSignal,
  ): Promise<Result<BudgetAllocation>>;
  changeAllocationAmount(
    allocation: BudgetAllocation,
    amount: string,
    reason: string,
    signal?: AbortSignal,
  ): Promise<Result<BudgetAllocation>>;
  closeAllocation(
    allocation: BudgetAllocation,
    reason: string,
    signal?: AbortSignal,
  ): Promise<Result<BudgetAllocation>>;
  listReservations(
    allocationId: string,
    request: { page: number; pageSize: number },
    signal?: AbortSignal,
  ): Promise<Result<Page<BudgetReservation>>>;
}

export interface LinePreview {
  readonly lineTotals: readonly (string | undefined)[];
  readonly total: string;
}

/** Requisition use cases: thin orchestration over the port plus the advisory client preview. */
export class RequisitionUseCases {
  constructor(private readonly port: RequisitionPort) {}

  list(query: RequisitionQuery, signal?: AbortSignal) {
    return this.port.list(query, signal);
  }

  /** CALC-002 preview of draft lines; the server total returned after saving is authoritative. */
  static preview(lines: readonly { quantity: string; unitPrice: string }[]): LinePreview {
    const lineTotals = lines.map((line) => previewLineTotal(line.quantity, line.unitPrice));
    return { lineTotals, total: previewTotal(lineTotals) };
  }

  /** Advisory pre-submit check; BUD-005 is decided by the server under its row lock. */
  static fitsBudget(requisition: Requisition, availability: BudgetAvailability | undefined) {
    return availability?.hasOpenAllocation
      ? fitsAvailability(requisition.estimatedTotal.amount, availability.available?.amount)
      : false;
  }

  static can(requisition: Requisition | undefined, action: RequisitionAction): boolean {
    return requisition?.allowedActions.includes(action) ?? false;
  }

  /** Latest reason recorded for a terminal transition (cancellation, rejection, expiry), if any. */
  static closingReason(timeline: readonly TimelineEntry[]): TimelineEntry | undefined {
    return [...timeline]
      .reverse()
      .find(
        (entry) =>
          entry.kind === 'state' &&
          (entry.to === 'Cancelled' || entry.to === 'Rejected' || entry.to === 'Expired'),
      );
  }
}
