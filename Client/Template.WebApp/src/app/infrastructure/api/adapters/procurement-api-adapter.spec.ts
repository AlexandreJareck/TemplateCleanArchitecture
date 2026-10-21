import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import type { Requisition } from '@application/procurement/procurement';
import { of, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { BudgetAllocationsService } from '../generated/budget-allocations/budget-allocations.service';
import { FiscalPeriodsService } from '../generated/fiscal-periods/fiscal-periods.service';
import { PurchaseRequisitionsService } from '../generated/purchase-requisitions/purchase-requisitions.service';
import { ProcurementApiAdapter } from './procurement-api-adapter';

interface Options {
  headers?: Record<string, string>;
}

function adapter(requisitions: object, allocations: object = {}, periods: object = {}) {
  TestBed.configureTestingModule({
    providers: [
      ProcurementApiAdapter,
      { provide: PurchaseRequisitionsService, useValue: requisitions },
      { provide: BudgetAllocationsService, useValue: allocations },
      { provide: FiscalPeriodsService, useValue: periods },
    ],
  });
  return TestBed.inject(ProcurementApiAdapter);
}

const view = {
  id: 'r1',
  number: 'REQ-2027-000001',
  requesterId: 'u1',
  costCenter: { id: 'c1', code: 'CC', name: 'Support', departmentId: 'd1' },
  currency: 'BRL',
  estimatedTotal: { amount: '300.00', currency: 'BRL' },
  status: 'Draft',
  revision: 1,
  lines: [
    {
      lineNumber: 1,
      description: 'Laptop',
      kind: 'Good',
      categoryId: 'g1',
      categoryCode: 'IT',
      unitOfMeasureId: 'u1',
      unitCode: 'UN',
      quantity: '2',
      estimatedUnitPrice: { amount: '150.00', currency: 'BRL' },
      lineTotal: { amount: '300.00', currency: 'BRL' },
    },
  ],
  attachments: [{ id: 'a1', fileName: 'quote.pdf', lockedAtRevision: 1 }],
  submissions: [],
  allowedActions: ['edit', 'submit', 'unknown-action'],
  version: '4',
};

describe('ProcurementApiAdapter', () => {
  it('maps the requisition representation without computing totals', async () => {
    const result = await adapter({ getApiV2PurchaseRequisitionsId: () => of(view) }).get('r1');
    expect(result).toMatchObject({
      ok: true,
      value: {
        number: 'REQ-2027-000001',
        estimatedTotal: { amount: '300.00' },
        lines: [{ lineTotal: { amount: '300.00' }, quantity: '2' }],
        attachments: [{ locked: true }],
        allowedActions: ['edit', 'submit'],
        version: '4',
      },
    });
  });

  it('sends quoted If-Match and a fresh Idempotency-Key on submit', async () => {
    let headers: Record<string, string> = {};
    const result = await adapter({
      postApiV2PurchaseRequisitionsIdSubmit: (_id: string, options: Options) => {
        headers = options.headers ?? {};
        return of({ ...view, status: 'Submitted', version: '5' });
      },
    }).submit({ id: 'r1', version: '4' } as Requisition);
    expect(headers['If-Match']).toBe('"4"');
    expect(headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/);
    expect(result).toMatchObject({ ok: true, value: { status: 'Submitted', version: '5' } });
  });

  it('maps budget.insufficient to a business-rule error with its rule ID', async () => {
    const result = await adapter({
      postApiV2PurchaseRequisitionsIdSubmit: () =>
        throwError(
          () =>
            new HttpErrorResponse({
              status: 422,
              error: { code: 'budget.insufficient', ruleId: 'BUD-005', traceId: 't1' },
            }),
        ),
    }).submit({ id: 'r1', version: '4' } as Requisition);
    expect(result).toMatchObject({
      ok: false,
      error: { kind: 'business-rule', code: 'budget.insufficient', ruleId: 'BUD-005' },
    });
  });

  it('sends line quantities and prices as decimal strings', async () => {
    let body: Record<string, unknown> = {};
    await adapter({
      postApiV2PurchaseRequisitionsIdLines: (_id: string, line: Record<string, unknown>) => {
        body = line;
        return of(view);
      },
    }).addLine({ id: 'r1', version: '4' } as Requisition, {
      description: 'Laptop',
      kind: 'Good',
      categoryId: 'g1',
      unitOfMeasureId: 'u1',
      quantity: ' 2 ',
      unitPrice: '150.0000',
      currency: 'BRL',
    });
    expect(body).toMatchObject({
      quantity: '2',
      estimatedUnitPrice: { amount: '150.0000', currency: 'BRL' },
      catalogItemId: null,
    });
  });

  it('maps allocations, reservations and availability from server totals', async () => {
    const budget = adapter(
      {
        getApiV2PurchaseRequisitionsIdBudgetAvailability: () =>
          of({
            hasOpenAllocation: true,
            asOfDate: '2027-03-01',
            available: { amount: '700.00', currency: 'BRL' },
          }),
      },
      {
        getApiV2BudgetAllocationsId: () =>
          of({
            id: 'b1',
            currency: 'BRL',
            amount: { amount: '1000.00', currency: 'BRL' },
            reserved: { amount: '300.00', currency: 'BRL' },
            available: { amount: '700.00', currency: 'BRL' },
            status: 'Active',
            version: '2',
            amountHistory: [{ sequence: 1, newAmount: { amount: '1000.00', currency: 'BRL' } }],
          }),
        getApiV2BudgetAllocationsIdReservations: () =>
          of({
            items: [{ id: 'x', status: 'Released', releaseReason: 'Cancelled' }],
            page: 1,
            totalPages: 1,
          }),
      },
    );
    expect(await budget.getAllocation('b1')).toMatchObject({
      ok: true,
      value: { available: { amount: '700.00' }, amountHistory: [{ sequence: 1 }] },
    });
    expect(await budget.listReservations('b1', { page: 1, pageSize: 20 })).toMatchObject({
      ok: true,
      value: { items: [{ status: 'Released', releaseReason: 'Cancelled' }] },
    });
    expect(await budget.availability('r1')).toMatchObject({
      ok: true,
      value: { hasOpenAllocation: true, available: { amount: '700.00' } },
    });
  });

  it('maps a stale version on a period transition to version-mismatch', async () => {
    const result = await adapter(
      {},
      {},
      {
        postApiV2FiscalPeriodsIdClose: () =>
          throwError(
            () =>
              new HttpErrorResponse({
                status: 412,
                error: { code: 'concurrency.version-mismatch', version: '9' },
              }),
          ),
      },
    ).changePeriodStatus(
      {
        id: 'p1',
        code: 'FY',
        name: 'FY',
        startDate: '',
        endDate: '',
        status: 'Open',
        version: '2',
      },
      'close',
    );
    expect(result).toMatchObject({
      ok: false,
      error: { kind: 'version-mismatch', currentVersion: '9' },
    });
  });
});
