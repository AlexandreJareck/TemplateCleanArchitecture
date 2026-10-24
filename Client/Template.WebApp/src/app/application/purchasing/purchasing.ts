import type { ApplicationError } from '../common/application-error';
import type { Result } from '../common/result';
import type { Page } from '../master-data/master-data';
import type { Money } from '../procurement/procurement';

export type PurchaseOrderStatus =
  'Draft' | 'Issued' | 'PartiallyReceived' | 'Completed' | 'Cancelled';

export type DocumentStatus = 'NotRequested' | 'Pending' | 'Ready' | 'Failed';

export type NotificationStatus = 'NotRequested' | 'Pending' | 'Sent' | 'Failed';

export type PurchaseOrderAction =
  | 'update'
  | 'issue'
  | 'cancel'
  | 'short-close'
  | 'retry-document'
  | 'retry-notification'
  | 'download-document'
  | 'receive';

export type DiscrepancyType = 'Damaged' | 'WrongItem' | 'QualityIssue' | 'Other';

export const DISCREPANCY_TYPES: readonly DiscrepancyType[] = [
  'Damaged',
  'WrongItem',
  'QualityIssue',
  'Other',
];

export interface PurchaseOrderLine {
  readonly lineNumber: number;
  readonly description: string;
  readonly kind: string;
  readonly unitCode: string;
  readonly unitDecimals: number;
  readonly quantity: string;
  /** Absent when the caller may not see prices (PUR-011). */
  readonly unitPrice?: Money;
  readonly lineTotal?: Money;
  readonly promisedDeliveryDate: string;
  readonly receivedQuantity: string;
  readonly acceptedQuantity: string;
  readonly rejectedQuantity: string;
  readonly remainingQuantity: string;
  /** CALC-009 cap of the pinned receiving policy; computed by the server. */
  readonly receiptCap: string;
  readonly shortClosed: boolean;
}

export interface PurchaseOrderSupplier {
  readonly supplierId: string;
  readonly legalName: string;
  readonly tradeName?: string;
  readonly country: string;
  readonly taxId: string;
  readonly address?: string;
  readonly contactName?: string;
  readonly contactEmail?: string;
  readonly contactPhone?: string;
}

export interface PurchaseOrder {
  readonly id: string;
  readonly number?: string;
  readonly status: PurchaseOrderStatus;
  readonly version: string;
  readonly requisitionId: string;
  readonly requisitionNumber: string;
  readonly rfqId: string;
  readonly rfqNumber: string;
  readonly supplier: PurchaseOrderSupplier;
  readonly buyerName?: string;
  readonly currency: string;
  readonly subtotal?: Money;
  readonly freight?: Money;
  readonly discount?: Money;
  readonly taxesIncluded?: Money;
  readonly total?: Money;
  readonly paymentTerms: string;
  readonly deliveryAddress?: string;
  readonly notes?: string;
  readonly lines: readonly PurchaseOrderLine[];
  readonly issuedAt?: string;
  readonly issueDate?: string;
  readonly cancelledAt?: string;
  readonly cancellationReason?: string;
  readonly completedAt?: string;
  readonly shortClosed: boolean;
  readonly shortCloseReason?: string;
  readonly documentStatus: DocumentStatus;
  readonly documentAttempts: number;
  readonly documentFailureCode?: string;
  readonly documentTemplateVersion?: string;
  readonly notificationStatus: NotificationStatus;
  readonly notificationAttempts: number;
  readonly notificationFailureCode?: string;
  readonly receivingPolicy: string;
  readonly pricesVisible: boolean;
  /** Server-computed (state and permissions); the UI never derives eligibility. */
  readonly allowedActions: readonly PurchaseOrderAction[];
}

export interface PurchaseOrderSummary {
  readonly id: string;
  readonly number?: string;
  readonly status: PurchaseOrderStatus;
  readonly requisitionNumber: string;
  readonly supplierName: string;
  readonly total?: Money;
  readonly lineCount: number;
  readonly documentStatus: DocumentStatus;
  readonly notificationStatus: NotificationStatus;
  readonly earliestPromisedDate?: string;
  readonly updatedAt: string;
}

export interface PurchaseOrderHistoryEntry {
  readonly sequence: number;
  readonly action: string;
  readonly from?: string;
  readonly to: string;
  readonly at: string;
  readonly reason?: string;
  readonly reasonCode?: string;
}

export interface ReceiptLine {
  readonly poLineNumber: number;
  readonly description: string;
  readonly unitCode: string;
  readonly receivedQuantity: string;
  readonly acceptedQuantity: string;
  readonly rejectedQuantity: string;
  readonly correctedAcceptedQuantity: string;
  readonly discrepancyType?: DiscrepancyType;
  readonly discrepancyNotes?: string;
}

export interface ReceiptCorrection {
  readonly sequence: number;
  readonly poLineNumber: number;
  readonly acceptedDelta: string;
  readonly reason: string;
  readonly at: string;
}

export interface Receipt {
  readonly id: string;
  readonly number: string;
  readonly version: string;
  readonly purchaseOrderId: string;
  readonly purchaseOrderNumber: string;
  readonly receivedOn: string;
  readonly location?: string;
  readonly deliveryReference?: string;
  readonly notes?: string;
  readonly policy: string;
  readonly hasDiscrepancy: boolean;
  readonly postedAt: string;
  readonly lines: readonly ReceiptLine[];
  readonly corrections: readonly ReceiptCorrection[];
  readonly canCorrect: boolean;
}

export interface ReceiptPosting {
  readonly receipt: Receipt;
  readonly order: PurchaseOrder;
}

export interface PurchaseOrderQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly status?: string;
  readonly q?: string;
}

export interface DraftChanges {
  readonly deliveryAddress?: string;
  readonly notes?: string;
  readonly promisedDates?: readonly { lineNumber: number; date: string }[];
}

/** One line of the receipt editor as typed by the receiver (decimal strings, server validates again). */
export interface ReceiptLineDraft {
  readonly poLineNumber: number;
  readonly received: string;
  readonly accepted: string;
  readonly rejected: string;
  readonly discrepancyType?: DiscrepancyType;
  readonly discrepancyNotes?: string;
}

export interface ReceiptDraft {
  readonly purchaseOrderId: string;
  readonly receivedOn: string;
  readonly location?: string;
  readonly deliveryReference?: string;
  readonly notes?: string;
  readonly lines: readonly ReceiptLineDraft[];
}

/** Client needs of the purchase-order and receiving screens; every rule is decided again by the API. */
export interface PurchasingPort {
  list(
    query: PurchaseOrderQuery,
    signal?: AbortSignal,
  ): Promise<Result<Page<PurchaseOrderSummary>>>;
  get(id: string, signal?: AbortSignal): Promise<Result<PurchaseOrder>>;
  history(id: string, signal?: AbortSignal): Promise<Result<readonly PurchaseOrderHistoryEntry[]>>;
  update(
    order: PurchaseOrder,
    changes: DraftChanges,
    signal?: AbortSignal,
  ): Promise<Result<PurchaseOrder>>;
  issue(order: PurchaseOrder, signal?: AbortSignal): Promise<Result<PurchaseOrder>>;
  cancel(
    order: PurchaseOrder,
    reason: string,
    signal?: AbortSignal,
  ): Promise<Result<PurchaseOrder>>;
  shortClose(
    order: PurchaseOrder,
    reason: string,
    signal?: AbortSignal,
  ): Promise<Result<PurchaseOrder>>;
  retryDocument(order: PurchaseOrder, signal?: AbortSignal): Promise<Result<PurchaseOrder>>;
  retryNotification(order: PurchaseOrder, signal?: AbortSignal): Promise<Result<PurchaseOrder>>;
  downloadDocument(order: PurchaseOrder, signal?: AbortSignal): Promise<Result<void>>;
  receipts(purchaseOrderId: string, signal?: AbortSignal): Promise<Result<Page<Receipt>>>;
  receipt(id: string, signal?: AbortSignal): Promise<Result<Receipt>>;
  postReceipt(draft: ReceiptDraft, signal?: AbortSignal): Promise<Result<ReceiptPosting>>;
  correct(
    receipt: Receipt,
    poLineNumber: number,
    acceptedDelta: string,
    reason: string,
    signal?: AbortSignal,
  ): Promise<Result<ReceiptPosting>>;
}

export const REASON = { min: 10, max: 500 } as const;

export const DISCREPANCY_NOTES_MAX = 1000;

export type LineIssue =
  | 'received-required'
  | 'decimals'
  | 'unbalanced'
  | 'over-remaining'
  | 'discrepancy-type'
  | 'discrepancy-notes';

/**
 * Purchasing presentation rules. They mirror the VAL-class checks (RCV-002/004) and display the server's remaining
 * quantities so the receiver sees an over-receipt before posting; the server stays authoritative (RCV-003 cap,
 * tolerance, completion, PUR-011 visibility).
 */
export class PurchasingUseCases {
  static can(order: PurchaseOrder | undefined, action: PurchaseOrderAction): boolean {
    return order?.allowedActions.includes(action) ?? false;
  }

  static reasonError(reason: string | undefined): 'required' | 'too-long' | undefined {
    const trimmed = reason?.trim() ?? '';
    if (trimmed.length > REASON.max) return 'too-long';
    return trimmed.length < REASON.min ? 'required' : undefined;
  }

  /** Decimal places of a typed quantity; `undefined` when it is not a non-negative decimal string. */
  static places(value: string): number | undefined {
    const trimmed = value.trim();
    if (!/^\d+(\.\d+)?$/.test(trimmed)) return undefined;
    const fraction = trimmed.split('.')[1] ?? '';
    return fraction.replace(/0+$/, '').length;
  }

  /** Compares two decimal strings exactly (scaled integers), avoiding floating-point rounding. */
  static compare(left: string, right: string): number {
    const scale = 3;
    const toScaled = (value: string) => {
      const [whole = '0', fraction = ''] = value.trim().split('.');
      return BigInt(whole + fraction.padEnd(scale, '0').slice(0, scale));
    };
    const difference = toScaled(left) - toScaled(right);
    return difference === 0n ? 0 : difference > 0n ? 1 : -1;
  }

  static add(left: string, right: string): string {
    const scale = 3;
    const toScaled = (value: string) => {
      const [whole = '0', fraction = ''] = value.trim().split('.');
      return BigInt(whole + fraction.padEnd(scale, '0').slice(0, scale));
    };
    const sum = toScaled(left) + toScaled(right);
    const digits = sum.toString().padStart(scale + 1, '0');
    const whole = digits.slice(0, -scale);
    const fraction = digits.slice(-scale).replace(/0+$/, '');
    return fraction ? `${whole}.${fraction}` : whole;
  }

  /** Validation issues of one editor line against the order line (display only). */
  static lineIssues(draft: ReceiptLineDraft, line: PurchaseOrderLine): readonly LineIssue[] {
    const issues: LineIssue[] = [];
    const values = [draft.received, draft.accepted, draft.rejected || '0'];
    const places = values.map((value) => PurchasingUseCases.places(value));
    if (places.some((value) => value === undefined)) return ['received-required'];
    if (places.some((value) => (value ?? 0) > Math.min(line.unitDecimals, 3)))
      issues.push('decimals');
    if (PurchasingUseCases.compare(draft.received, '0') <= 0) issues.push('received-required');
    if (
      PurchasingUseCases.compare(
        PurchasingUseCases.add(draft.accepted, draft.rejected || '0'),
        draft.received,
      ) !== 0
    )
      issues.push('unbalanced');
    if (PurchasingUseCases.compare(draft.accepted, line.remainingQuantity) > 0)
      issues.push('over-remaining');
    if (PurchasingUseCases.compare(draft.rejected || '0', '0') > 0) {
      if (!draft.discrepancyType) issues.push('discrepancy-type');
      const notes = draft.discrepancyNotes?.trim() ?? '';
      if (!notes || notes.length > DISCREPANCY_NOTES_MAX) issues.push('discrepancy-notes');
    }
    return issues;
  }

  /** Lines that take part in the posting (a received quantity was typed). */
  static activeLines(lines: readonly ReceiptLineDraft[]): readonly ReceiptLineDraft[] {
    return lines.filter((line) => line.received.trim() !== '' && line.received.trim() !== '0');
  }

  /** Errors that mean the screen shows an outdated state: reload instead of retrying. */
  static isStale(error: ApplicationError): boolean {
    return (
      error.kind === 'version-mismatch' ||
      error.code === 'invalid-transition' ||
      error.code === 'purchase-order.immutable' ||
      error.code === 'receipt.order-not-receivable'
    );
  }

  /** Translation key of a stable purchasing problem code; others use the generic Problem Details message. */
  static problemKey(error: ApplicationError): string | undefined {
    const known = [
      'purchase-order.not-issuable',
      'purchase-order.has-receipts',
      'purchase-order.immutable',
      'purchase-order.document-not-ready',
      'purchase-order.not-retryable',
      'supplier.not-eligible',
      'receipt.over-receipt',
      'receipt.order-not-receivable',
      'receipt.correction-out-of-range',
    ];
    return known.includes(error.code)
      ? `purchasing.problems.${error.code.replace(/\./g, '-')}`
      : undefined;
  }
}
