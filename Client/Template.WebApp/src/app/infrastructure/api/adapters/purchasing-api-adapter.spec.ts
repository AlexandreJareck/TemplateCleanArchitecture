import { HttpClient, HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { PurchaseOrdersService } from '../generated/purchase-orders/purchase-orders.service';
import { ReceiptsService } from '../generated/receipts/receipts.service';
import { BrowserFileSaver } from '../../runtime/browser-file-saver';
import { mapOrder, mapReceipt, mapSummary, PurchasingApiAdapter } from './purchasing-api-adapter';

interface Options {
  headers?: Record<string, string>;
}

const view = {
  id: '11111111-2222-3333-4444-555555555555',
  number: 'PO-2027-000001',
  status: 'Issued',
  version: '4',
  requisitionId: 'req-1',
  requisitionNumber: 'REQ-2027-000001',
  rfqNumber: 'RFQ-2027-000001',
  supplier: {
    supplierId: 's1',
    legalName: 'Alfa',
    country: 'BR',
    taxId: '1',
    contact: { name: 'Ana', email: 'a@example.invalid' },
  },
  currency: 'BRL',
  total: { amount: '16200.00', currency: 'BRL' },
  lines: [
    {
      lineNumber: 1,
      description: 'Notebook',
      unitCode: 'UN',
      unitAllowedDecimals: 0,
      quantity: '18',
      unitPrice: { amount: '900.00', currency: 'BRL' },
      remainingQuantity: '8',
      receiptCap: '18',
    },
  ],
  document: { status: 'Ready', attempts: 1, templateVersion: 'purchase-order-pdf.v1' },
  notification: { status: 'Weird', attempts: 1 },
  receivingPolicy: 'ReceivingPolicy:1',
  pricesVisible: true,
  allowedActions: ['receive', 'download-document', 'unknown'],
};

function adapter(
  orders: object,
  receipts: object = {},
  http: object = {},
  saved: { name?: string } = {},
) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      PurchasingApiAdapter,
      { provide: PurchaseOrdersService, useValue: orders },
      { provide: ReceiptsService, useValue: receipts },
      { provide: HttpClient, useValue: http },
      {
        provide: BrowserFileSaver,
        useValue: { save: (_: Blob, name: string) => (saved.name = name) },
      },
    ],
  });
  return TestBed.inject(PurchasingApiAdapter);
}

describe('PurchasingApiAdapter', () => {
  it('maps orders without inventing prices or actions', () => {
    const order = mapOrder(view);
    expect(order.allowedActions).toEqual(['receive', 'download-document']);
    expect(order.lines[0]).toMatchObject({
      remainingQuantity: '8',
      unitDecimals: 0,
      unitPrice: { amount: '900.00' },
    });
    expect(order.notificationStatus).toBe('NotRequested');
    expect(order.supplier.contactEmail).toBe('a@example.invalid');
    const hidden = mapOrder({
      ...view,
      total: { amount: null, currency: null },
      pricesVisible: false,
      lines: [{ lineNumber: 1 }],
    });
    expect(hidden.total).toBeUndefined();
    expect(hidden.lines[0]!.unitPrice).toBeUndefined();
    expect(mapOrder({}).status).toBe('Draft');
    expect(mapSummary({ status: 'Completed', supplierLegalName: 'Alfa' })).toMatchObject({
      status: 'Completed',
      supplierName: 'Alfa',
    });
    expect(
      mapReceipt({
        id: 'r1',
        lines: [{ poLineNumber: 1, discrepancyType: 'Damaged', acceptedQuantity: '1' }],
        allowedActions: ['correct'],
      }),
    ).toMatchObject({
      canCorrect: true,
      lines: [{ discrepancyType: 'Damaged', correctedAcceptedQuantity: '1' }],
    });
  });

  it('issues with the quoted If-Match and a fresh Idempotency-Key', async () => {
    const calls: Options[] = [];
    const subject = adapter({
      postApiV2PurchaseOrdersIdIssue: (_: string, options: Options) => {
        calls.push(options);
        return of(view);
      },
      postApiV2PurchaseOrdersIdCancel: (_: string, body: { reason: string }, options: Options) => {
        calls.push({ headers: { ...options.headers, reason: body.reason } });
        return throwError(
          () =>
            new HttpErrorResponse({ status: 409, error: { code: 'purchase-order.has-receipts' } }),
        );
      },
    });
    const order = mapOrder(view);
    const issued = await subject.issue(order);
    expect(issued.ok).toBe(true);
    expect(calls[0]!.headers!['If-Match']).toBe('"4"');
    expect(calls[0]!.headers!['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/);
    const cancelled = await subject.cancel(order, '  Motivo do cancelamento  ');
    expect(cancelled).toMatchObject({
      ok: false,
      error: { kind: 'conflict', code: 'purchase-order.has-receipts' },
    });
    expect(calls[1]!.headers!['reason']).toBe('Motivo do cancelamento');
    expect(calls[1]!.headers!['Idempotency-Key']).not.toBe(calls[0]!.headers!['Idempotency-Key']);
  });

  it('lists the queue, updates the draft and posts receipts and corrections with decimal strings', async () => {
    const requests: Record<string, unknown>[] = [];
    const posting = {
      receipt: { id: 'r1', number: 'RCV-2027-000001', version: '1' },
      purchaseOrder: view,
    };
    const subject = adapter(
      {
        getApiV2PurchaseOrders: (params: Record<string, unknown>) => {
          requests.push(params);
          return of({
            items: [{ id: 'po-1', status: 'Draft' }],
            page: 1,
            pageSize: 20,
            totalItems: 1,
            totalPages: 1,
          });
        },
        putApiV2PurchaseOrdersId: (_: string, body: Record<string, unknown>, options: Options) => {
          requests.push({ ...body, ifMatch: options.headers!['If-Match'] });
          return of(view);
        },
        getApiV2PurchaseOrdersIdHistory: () =>
          of({
            entries: [{ sequence: 1, action: 'created', to: 'Draft', at: '2027-03-01T13:00:00Z' }],
          }),
      },
      {
        postApiV2Receipts: (body: Record<string, unknown>) => {
          requests.push(body);
          return of(posting);
        },
        postApiV2ReceiptsIdCorrections: (
          id: string,
          body: Record<string, unknown>,
          options: Options,
        ) => {
          requests.push({ id, ...body, ifMatch: options.headers!['If-Match'] });
          return of(posting);
        },
        getApiV2Receipts: () =>
          of({ items: [posting.receipt], page: 1, pageSize: 100, totalItems: 1, totalPages: 1 }),
      },
    );
    expect((await subject.list({ page: 1, pageSize: 20, status: 'Draft', q: '  REQ ' })).ok).toBe(
      true,
    );
    expect(requests[0]).toMatchObject({ status: 'Draft', q: 'REQ', sort: '-updatedAt' });
    await subject.update(mapOrder(view), {
      deliveryAddress: 'Rua 1',
      promisedDates: [{ lineNumber: 1, date: '2027-03-20' }],
    });
    expect(requests[1]).toMatchObject({
      deliveryAddress: 'Rua 1',
      notes: null,
      promisedDates: [{ lineNumber: 1, promisedDeliveryDate: '2027-03-20' }],
      ifMatch: '"4"',
    });
    const history = await subject.history('po-1');
    expect(history.ok && history.value[0]!.action).toBe('created');
    const posted = await subject.postReceipt({
      purchaseOrderId: 'po-1',
      receivedOn: '2027-03-05',
      lines: [
        {
          poLineNumber: 1,
          received: ' 4 ',
          accepted: '3',
          rejected: '1',
          discrepancyType: 'Damaged',
          discrepancyNotes: ' Caixa ',
        },
      ],
    });
    expect(posted.ok && posted.value.order.number).toBe('PO-2027-000001');
    expect(requests[2]).toMatchObject({
      lines: [
        {
          received: '4',
          accepted: '3',
          rejected: '1',
          discrepancyType: 'Damaged',
          discrepancyNotes: 'Caixa',
        },
      ],
      location: null,
      attachments: [],
    });
    const receipt = mapReceipt(posting.receipt);
    await subject.correct(receipt, 1, ' -1 ', 'Motivo da correção');
    expect(requests[3]).toMatchObject({
      id: 'r1',
      poLineNumber: 1,
      acceptedDelta: '-1',
      ifMatch: '"1"',
    });
    expect((await subject.receipts('po-1')).ok).toBe(true);
  });

  it('downloads the issued document through the API path and parses Blob Problem Details', async () => {
    const saved: { name?: string } = {};
    let path = '';
    const http = {
      get: (url: string) => {
        path = url;
        return of(new HttpResponse({ body: new Blob(['%PDF-1.7']), headers: new HttpHeaders() }));
      },
    };
    const subject = adapter({}, {}, http, saved);
    expect((await subject.downloadDocument(mapOrder(view))).ok).toBe(true);
    expect(path).toBe(`/api/v2/purchase-orders/${view.id}/document`);
    expect(saved.name).toBe('pedido-PO-2027-000001.pdf');

    const failing = adapter(
      {},
      {},
      {
        get: () =>
          throwError(
            () =>
              new HttpErrorResponse({
                status: 409,
                error: new Blob([JSON.stringify({ code: 'purchase-order.document-not-ready' })], {
                  type: 'application/problem+json',
                }),
              }),
          ),
      },
    );
    expect(await failing.downloadDocument(mapOrder(view))).toMatchObject({
      ok: false,
      error: { code: 'purchase-order.document-not-ready' },
    });
  });
});
