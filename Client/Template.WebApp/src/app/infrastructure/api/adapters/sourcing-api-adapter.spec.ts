import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import type { Rfq } from '@application/sourcing/sourcing';
import { of, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { RequestsForQuotationService } from '../generated/requests-for-quotation/requests-for-quotation.service';
import { SourcingApiAdapter } from './sourcing-api-adapter';

interface Options {
  headers?: Record<string, string>;
}

function adapter(service: object) {
  TestBed.configureTestingModule({
    providers: [SourcingApiAdapter, { provide: RequestsForQuotationService, useValue: service }],
  });
  return TestBed.inject(SourcingApiAdapter);
}

const view = {
  id: 'rfq-1',
  number: 'RFQ-2027-000001',
  requisition: {
    id: 'req-1',
    number: 'REQ-2027-000001',
    revision: 1,
    estimatedTotal: { amount: '18000.00', currency: 'BRL' },
  },
  roundNumber: 1,
  method: 'Competitive',
  policy: { type: 'SourcingPolicy', version: 1 },
  baseCurrency: 'BRL',
  aboveThreshold: true,
  requiredInvitations: 3,
  status: 'Closed',
  invitations: [{ supplierId: 's1', supplierLegalName: 'Alpha', isActive: true }],
  commercialDetailsVisible: true,
  quotes: [
    {
      id: 'q1',
      supplierId: 's1',
      supplierLegalName: 'Alpha',
      latestStatus: 'Current',
      revisions: [
        {
          id: 'r1',
          revisionNumber: 1,
          status: 'Current',
          currency: 'USD',
          total: { amount: '105.00', currency: 'USD' },
          source: 'Email',
        },
      ],
    },
  ],
  comparison: {
    sequence: 2,
    status: 'Complete',
    baseCurrency: 'BRL',
    warnings: ['insufficient-valid-quotes'],
    recommendedQuoteRevisionId: 'r1',
    entries: [
      {
        quoteRevisionId: 'r1',
        supplierId: 's1',
        rawTotal: { amount: '105.00', currency: 'USD' },
        normalizedTotal: { amount: '570.37', currency: 'BRL' },
        rate: '5.4321',
        isValid: true,
        invalidReasons: [],
        rank: 1,
      },
    ],
  },
  exchangeRates: [
    {
      purpose: 'Comparison',
      fromCurrency: 'USD',
      toCurrency: 'BRL',
      rate: '5.4321',
      source: 'Manual',
    },
  ],
  allowedActions: ['award', 'cancel', 'unknown'],
  version: '9',
};

describe('SourcingApiAdapter', () => {
  it('copies raw and normalized totals, ranks and rate provenance from the server', async () => {
    const result = await adapter({ getApiV2RequestsForQuotationId: () => of(view) }).get('rfq-1');
    expect(result).toMatchObject({
      ok: true,
      value: {
        status: 'Closed',
        invitationCount: 1,
        comparison: {
          status: 'Complete',
          entries: [
            {
              rawTotal: { amount: '105.00' },
              normalizedTotal: { amount: '570.37' },
              rank: 1,
              rate: '5.4321',
            },
          ],
          recommendedQuoteRevisionId: 'r1',
        },
        exchangeRates: [{ source: 'Manual', rate: '5.4321' }],
        quotes: [{ revisions: [{ currency: 'USD', total: { amount: '105.00' } }] }],
        allowedActions: ['award', 'cancel'],
        version: '9',
      },
    });
  });

  it('hides missing commercial details as empty collections', async () => {
    const result = await adapter({
      getApiV2RequestsForQuotationId: () =>
        of({ ...view, commercialDetailsVisible: false, quotes: null, comparison: undefined }),
    }).get('rfq-1');
    expect(result).toMatchObject({
      ok: true,
      value: { commercialDetailsVisible: false, quotes: [], comparison: undefined },
    });
  });

  it('sends quoted If-Match, an Idempotency-Key and decimal strings when entering a quote', async () => {
    let headers: Record<string, string> = {};
    let body: Record<string, unknown> = {};
    await adapter({
      postApiV2RequestsForQuotationIdQuotes: (
        _id: string,
        quote: Record<string, unknown>,
        options: Options,
      ) => {
        body = quote;
        headers = options.headers ?? {};
        return of(view);
      },
    }).enterQuote({ id: 'rfq-1', version: '9' } as Rfq, {
      supplierId: 's1',
      currency: 'BRL',
      lines: [{ rfqLineNumber: 1, quantityOffered: ' 10 ', unitPrice: '33.3333' }],
      freight: '',
      discount: '5.50',
      taxesIncluded: '',
      validUntil: '2027-04-30',
      deliveryDays: 7,
      paymentTerms: '30 days',
      notes: ' ',
      source: 'Email',
      receivedAt: '2027-03-01T13:00:00.000Z',
    });
    expect(headers['If-Match']).toBe('"9"');
    expect(headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/);
    expect(body).toMatchObject({
      supplierId: 's1',
      lines: [{ quantityOffered: '10', unitPrice: '33.3333' }],
      freight: null,
      discount: '5.50',
      notes: null,
    });
    expect(body).not.toHaveProperty('total');
  });

  it('maps award waiver problems to business-rule errors with their codes', async () => {
    const result = await adapter({
      postApiV2RequestsForQuotationIdAward: () =>
        throwError(
          () =>
            new HttpErrorResponse({
              status: 422,
              error: { code: 'award.insufficient-quotations', ruleId: 'SRC-017', traceId: 't1' },
            }),
        ),
    }).award({ id: 'rfq-1', version: '9' } as Rfq, { quoteRevisionId: 'r1' });
    expect(result).toMatchObject({
      ok: false,
      error: { kind: 'business-rule', code: 'award.insufficient-quotations', ruleId: 'SRC-017' },
    });
  });

  it('maps a stale version to version-mismatch for conflict recovery', async () => {
    const result = await adapter({
      postApiV2RequestsForQuotationIdClose: () =>
        throwError(
          () =>
            new HttpErrorResponse({
              status: 412,
              error: { code: 'concurrency.version-mismatch', version: '10' },
            }),
        ),
    }).close({ id: 'rfq-1', version: '9' } as Rfq);
    expect(result).toMatchObject({ ok: false, error: { kind: 'version-mismatch' } });
  });

  it('creates an RFQ with an idempotency key and maps list pages', async () => {
    let headers: Record<string, string> = {};
    const service = adapter({
      postApiV2RequestsForQuotation: (_body: unknown, options: Options) => {
        headers = options.headers ?? {};
        return of(view);
      },
      getApiV2RequestsForQuotation: () =>
        of({
          items: [{ id: 'rfq-1', number: 'RFQ-1', status: 'Published', invitationCount: 3 }],
          page: 1,
          pageSize: 20,
          totalItems: 1,
          totalPages: 1,
        }),
    });
    await service.create('req-1', 'Direct');
    expect(headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/);
    expect(await service.list({ page: 1, pageSize: 20 })).toMatchObject({
      ok: true,
      value: { items: [{ status: 'Published', invitationCount: 3 }], totalItems: 1 },
    });
  });
});
