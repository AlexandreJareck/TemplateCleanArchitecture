import { HttpClient, HttpErrorResponse, type HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { failure, success, type Result } from '@application/common/result';
import type { Money } from '@application/procurement/procurement';
import {
  DISCREPANCY_TYPES,
  type DiscrepancyType,
  type DocumentStatus,
  type DraftChanges,
  type NotificationStatus,
  type PurchaseOrder,
  type PurchaseOrderAction,
  type PurchaseOrderHistoryEntry,
  type PurchaseOrderLine,
  type PurchaseOrderQuery,
  type PurchaseOrderStatus,
  type PurchaseOrderSummary,
  type PurchasingPort,
  type Receipt,
  type ReceiptDraft,
  type ReceiptPosting,
} from '@application/purchasing/purchasing';
import { firstValueFrom, fromEvent, takeUntil, type Observable } from 'rxjs';
import { PurchaseOrdersService } from '../generated/purchase-orders/purchase-orders.service';
import { ReceiptsService } from '../generated/receipts/receipts.service';
import type { MoneyView } from '../generated/models/moneyView';
import type { PurchaseOrderHistoryEntryView } from '../generated/models/purchaseOrderHistoryEntryView';
import type { PurchaseOrderLineView } from '../generated/models/purchaseOrderLineView';
import type { PurchaseOrderSummaryView } from '../generated/models/purchaseOrderSummaryView';
import type { PurchaseOrderView } from '../generated/models/purchaseOrderView';
import type { ReceiptPostingView } from '../generated/models/receiptPostingView';
import type { ReceiptView } from '../generated/models/receiptView';
import { mapProblem } from '../../http/problem-mapper';
import { BrowserFileSaver } from '../../runtime/browser-file-saver';
import { mapPage } from './procurement-api-adapter';

const STATUSES: readonly PurchaseOrderStatus[] = [
  'Draft',
  'Issued',
  'PartiallyReceived',
  'Completed',
  'Cancelled',
];
const DOCUMENT_STATUSES: readonly DocumentStatus[] = ['NotRequested', 'Pending', 'Ready', 'Failed'];
const NOTIFICATION_STATUSES: readonly NotificationStatus[] = [
  'NotRequested',
  'Pending',
  'Sent',
  'Failed',
];
const ACTIONS: readonly PurchaseOrderAction[] = [
  'update',
  'issue',
  'cancel',
  'short-close',
  'retry-document',
  'retry-notification',
  'download-document',
  'receive',
];
const DOCUMENT_PATH = /^\/api\/v2\/purchase-orders\/[0-9a-f-]{36}\/document$/i;

/**
 * Generated-client adapter of the Phase 32 purchasing and receiving contract: every change sends the quoted `If-Match`
 * of the current version and a fresh `Idempotency-Key`; Problem Details become Application errors. The issued document
 * is streamed from the API path with the bearer token and saved as an attachment; no storage URL is involved.
 */
@Injectable({ providedIn: 'root' })
export class PurchasingApiAdapter implements PurchasingPort {
  private readonly orders = inject(PurchaseOrdersService);
  private readonly receiptsApi = inject(ReceiptsService);
  private readonly http = inject(HttpClient);
  private readonly saver = inject(BrowserFileSaver);

  list(query: PurchaseOrderQuery, signal?: AbortSignal) {
    return this.execute(
      this.orders.getApiV2PurchaseOrders({
        page: query.page,
        pageSize: query.pageSize,
        status: query.status || undefined,
        q: query.q?.trim() || undefined,
        sort: '-updatedAt',
      }),
      signal,
      (value) => mapPage(value, mapSummary),
    );
  }

  get(id: string, signal?: AbortSignal) {
    return this.execute(this.orders.getApiV2PurchaseOrdersId(id), signal, mapOrder);
  }

  history(id: string, signal?: AbortSignal) {
    return this.execute(this.orders.getApiV2PurchaseOrdersIdHistory(id), signal, (value) =>
      (value.entries ?? []).map(mapHistory),
    );
  }

  update(order: PurchaseOrder, changes: DraftChanges, signal?: AbortSignal) {
    return this.execute(
      this.orders.putApiV2PurchaseOrdersId(
        order.id,
        {
          deliveryAddress: changes.deliveryAddress ?? null,
          notes: changes.notes ?? null,
          promisedDates:
            changes.promisedDates?.map((change) => ({
              lineNumber: change.lineNumber,
              promisedDeliveryDate: change.date,
            })) ?? null,
        },
        { headers: { 'If-Match': `"${order.version}"` } },
      ),
      signal,
      mapOrder,
    );
  }

  issue(order: PurchaseOrder, signal?: AbortSignal) {
    return this.execute(
      this.orders.postApiV2PurchaseOrdersIdIssue(order.id, {
        headers: changeHeaders(order.version),
      }),
      signal,
      mapOrder,
    );
  }

  cancel(order: PurchaseOrder, reason: string, signal?: AbortSignal) {
    return this.execute(
      this.orders.postApiV2PurchaseOrdersIdCancel(
        order.id,
        { reason: reason.trim() },
        { headers: changeHeaders(order.version) },
      ),
      signal,
      mapOrder,
    );
  }

  shortClose(order: PurchaseOrder, reason: string, signal?: AbortSignal) {
    return this.execute(
      this.orders.postApiV2PurchaseOrdersIdShortClose(
        order.id,
        { reason: reason.trim() },
        { headers: changeHeaders(order.version) },
      ),
      signal,
      mapOrder,
    );
  }

  retryDocument(order: PurchaseOrder, signal?: AbortSignal) {
    return this.execute(
      this.orders.postApiV2PurchaseOrdersIdDocumentRetry(order.id, {
        headers: changeHeaders(order.version),
      }),
      signal,
      mapOrder,
    );
  }

  retryNotification(order: PurchaseOrder, signal?: AbortSignal) {
    return this.execute(
      this.orders.postApiV2PurchaseOrdersIdNotificationRetry(order.id, {
        headers: changeHeaders(order.version),
      }),
      signal,
      mapOrder,
    );
  }

  async downloadDocument(order: PurchaseOrder, signal?: AbortSignal): Promise<Result<void>> {
    const path = `/api/v2/purchase-orders/${order.id}/document`;
    if (!DOCUMENT_PATH.test(path))
      return failure({ kind: 'conflict', code: 'purchase-order.document-not-ready' });
    try {
      const source = this.http.get(path, { observe: 'response', responseType: 'blob' });
      const response = await firstValueFrom(
        signal ? source.pipe(takeUntil(fromEvent(signal, 'abort'))) : source,
      );
      const blob = (response as HttpResponse<Blob>).body;
      if (!blob) return failure({ kind: 'unexpected', code: 'purchase-order.document-empty' });
      this.saver.save(blob, `pedido-${order.number ?? order.id}.pdf`);
      return success(undefined);
    } catch (error) {
      return failure(mapProblem(await withJsonProblem(error), signal));
    }
  }

  receipts(purchaseOrderId: string, signal?: AbortSignal) {
    return this.execute(
      this.receiptsApi.getApiV2Receipts({
        purchaseOrderId,
        page: 1,
        pageSize: 100,
        sort: 'postedAt',
      }),
      signal,
      (value) => mapPage(value, mapReceipt),
    );
  }

  receipt(id: string, signal?: AbortSignal) {
    return this.execute(this.receiptsApi.getApiV2ReceiptsId(id), signal, mapReceipt);
  }

  postReceipt(draft: ReceiptDraft, signal?: AbortSignal) {
    return this.execute(
      this.receiptsApi.postApiV2Receipts(
        {
          purchaseOrderId: draft.purchaseOrderId,
          receivedOn: draft.receivedOn,
          location: draft.location?.trim() || null,
          deliveryReference: draft.deliveryReference?.trim() || null,
          notes: draft.notes?.trim() || null,
          lines: draft.lines.map((line) => ({
            poLineNumber: line.poLineNumber,
            received: line.received.trim(),
            accepted: line.accepted.trim(),
            rejected: line.rejected.trim() || '0',
            discrepancyType: line.discrepancyType ?? null,
            discrepancyNotes: line.discrepancyNotes?.trim() || null,
          })),
          attachments: [],
        },
        { headers: { 'Idempotency-Key': crypto.randomUUID() } },
      ),
      signal,
      mapPosting,
    );
  }

  correct(
    receipt: Receipt,
    poLineNumber: number,
    acceptedDelta: string,
    reason: string,
    signal?: AbortSignal,
  ) {
    return this.execute(
      this.receiptsApi.postApiV2ReceiptsIdCorrections(
        receipt.id,
        { poLineNumber, acceptedDelta: acceptedDelta.trim(), reason: reason.trim() },
        { headers: changeHeaders(receipt.version) },
      ),
      signal,
      mapPosting,
    );
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

function changeHeaders(version: string) {
  return { 'If-Match': `"${version}"`, 'Idempotency-Key': crypto.randomUUID() };
}

/** Blob responses carry Problem Details as a Blob; parse it so the stable `code` reaches the Application error. */
async function withJsonProblem(error: unknown): Promise<unknown> {
  if (!(error instanceof HttpErrorResponse) || !(error.error instanceof Blob)) return error;
  try {
    return new HttpErrorResponse({
      error: JSON.parse(await error.error.text()) as unknown,
      headers: error.headers,
      status: error.status,
      statusText: error.statusText,
      url: error.url ?? undefined,
    });
  } catch {
    return new HttpErrorResponse({
      status: error.status,
      headers: error.headers,
      statusText: error.statusText,
    });
  }
}

function money(value: MoneyView | null | undefined): Money | undefined {
  return value?.amount && value.currency
    ? { amount: value.amount, currency: value.currency }
    : undefined;
}

function pick<T extends string>(
  values: readonly T[],
  value: string | null | undefined,
  fallback: T,
): T {
  return values.find((candidate) => candidate === value) ?? fallback;
}

function mapLine(value: PurchaseOrderLineView): PurchaseOrderLine {
  return {
    lineNumber: value.lineNumber ?? 0,
    description: value.description ?? '',
    kind: value.kind ?? 'Good',
    unitCode: value.unitCode ?? '',
    unitDecimals: value.unitAllowedDecimals ?? 0,
    quantity: value.quantity ?? '0',
    unitPrice: money(value.unitPrice),
    lineTotal: money(value.lineTotal),
    promisedDeliveryDate: value.promisedDeliveryDate ?? '',
    receivedQuantity: value.receivedQuantity ?? '0',
    acceptedQuantity: value.acceptedQuantity ?? '0',
    rejectedQuantity: value.rejectedQuantity ?? '0',
    remainingQuantity: value.remainingQuantity ?? '0',
    receiptCap: value.receiptCap ?? '0',
    shortClosed: value.isShortClosed ?? false,
  };
}

export function mapOrder(value: PurchaseOrderView): PurchaseOrder {
  return {
    id: value.id ?? '',
    number: value.number ?? undefined,
    status: pick(STATUSES, value.status, 'Draft'),
    version: value.version ?? '',
    requisitionId: value.requisitionId ?? '',
    requisitionNumber: value.requisitionNumber ?? '',
    rfqId: value.rfqId ?? '',
    rfqNumber: value.rfqNumber ?? '',
    supplier: {
      supplierId: value.supplier?.supplierId ?? '',
      legalName: value.supplier?.legalName ?? '',
      tradeName: value.supplier?.tradeName ?? undefined,
      country: value.supplier?.country ?? '',
      taxId: value.supplier?.taxId ?? '',
      address: value.supplier?.address ?? undefined,
      contactName: value.supplier?.contact?.name ?? undefined,
      contactEmail: value.supplier?.contact?.email ?? undefined,
      contactPhone: value.supplier?.contact?.phone ?? undefined,
    },
    buyerName: value.buyerName ?? undefined,
    currency: value.currency ?? '',
    subtotal: money(value.subtotal),
    freight: money(value.freight),
    discount: money(value.discount),
    taxesIncluded: money(value.taxesIncluded),
    total: money(value.total),
    paymentTerms: value.paymentTerms ?? '',
    deliveryAddress: value.deliveryAddress ?? undefined,
    notes: value.notes ?? undefined,
    lines: (value.lines ?? []).map(mapLine),
    issuedAt: value.issuedAt ?? undefined,
    issueDate: value.issueDate ?? undefined,
    cancelledAt: value.cancelledAt ?? undefined,
    cancellationReason: value.cancellationReason ?? undefined,
    completedAt: value.completedAt ?? undefined,
    shortClosed: value.shortClosed ?? false,
    shortCloseReason: value.shortCloseReason ?? undefined,
    documentStatus: pick(DOCUMENT_STATUSES, value.document?.status, 'NotRequested'),
    documentAttempts: value.document?.attempts ?? 0,
    documentFailureCode: value.document?.failureCode ?? undefined,
    documentTemplateVersion: value.document?.templateVersion ?? undefined,
    notificationStatus: pick(NOTIFICATION_STATUSES, value.notification?.status, 'NotRequested'),
    notificationAttempts: value.notification?.attempts ?? 0,
    notificationFailureCode: value.notification?.failureCode ?? undefined,
    receivingPolicy: value.receivingPolicy ?? '',
    pricesVisible: value.pricesVisible ?? false,
    allowedActions: (value.allowedActions ?? []).filter((action): action is PurchaseOrderAction =>
      ACTIONS.includes(action as PurchaseOrderAction),
    ),
  };
}

export function mapSummary(value: PurchaseOrderSummaryView): PurchaseOrderSummary {
  return {
    id: value.id ?? '',
    number: value.number ?? undefined,
    status: pick(STATUSES, value.status, 'Draft'),
    requisitionNumber: value.requisitionNumber ?? '',
    supplierName: value.supplierLegalName ?? '',
    total: money(value.total),
    lineCount: value.lineCount ?? 0,
    documentStatus: pick(DOCUMENT_STATUSES, value.documentStatus, 'NotRequested'),
    notificationStatus: pick(NOTIFICATION_STATUSES, value.notificationStatus, 'NotRequested'),
    earliestPromisedDate: value.earliestPromisedDate ?? undefined,
    updatedAt: value.updatedAt ?? '',
  };
}

function mapHistory(value: PurchaseOrderHistoryEntryView): PurchaseOrderHistoryEntry {
  return {
    sequence: value.sequence ?? 0,
    action: value.action ?? '',
    from: value.from ?? undefined,
    to: value.to ?? '',
    at: value.at ?? '',
    reason: value.reason ?? undefined,
    reasonCode: value.reasonCode ?? undefined,
  };
}

export function mapReceipt(value: ReceiptView): Receipt {
  return {
    id: value.id ?? '',
    number: value.number ?? '',
    version: value.version ?? '',
    purchaseOrderId: value.purchaseOrderId ?? '',
    purchaseOrderNumber: value.purchaseOrderNumber ?? '',
    receivedOn: value.receivedOn ?? '',
    location: value.location ?? undefined,
    deliveryReference: value.deliveryReference ?? undefined,
    notes: value.notes ?? undefined,
    policy: value.policy ?? '',
    hasDiscrepancy: value.hasDiscrepancy ?? false,
    postedAt: value.postedAt ?? '',
    lines: (value.lines ?? []).map((line) => ({
      poLineNumber: line.poLineNumber ?? 0,
      description: line.description ?? '',
      unitCode: line.unitCode ?? '',
      receivedQuantity: line.receivedQuantity ?? '0',
      acceptedQuantity: line.acceptedQuantity ?? '0',
      rejectedQuantity: line.rejectedQuantity ?? '0',
      correctedAcceptedQuantity: line.correctedAcceptedQuantity ?? line.acceptedQuantity ?? '0',
      discrepancyType: DISCREPANCY_TYPES.find(
        (type): type is DiscrepancyType => type === line.discrepancyType,
      ),
      discrepancyNotes: line.discrepancyNotes ?? undefined,
    })),
    corrections: (value.corrections ?? []).map((correction) => ({
      sequence: correction.sequence ?? 0,
      poLineNumber: correction.poLineNumber ?? 0,
      acceptedDelta: correction.acceptedDelta ?? '0',
      reason: correction.reason ?? '',
      at: correction.at ?? '',
    })),
    canCorrect: (value.allowedActions ?? []).includes('correct'),
  };
}

function mapPosting(value: ReceiptPostingView): ReceiptPosting {
  return { receipt: mapReceipt(value.receipt ?? {}), order: mapOrder(value.purchaseOrder ?? {}) };
}
