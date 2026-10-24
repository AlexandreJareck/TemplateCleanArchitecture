import { describe, expect, it } from 'vitest';
import { PurchasingUseCases, type PurchaseOrder, type PurchaseOrderLine } from './purchasing';

const line: PurchaseOrderLine = {
  lineNumber: 1,
  description: 'Notebook',
  kind: 'Good',
  unitCode: 'UN',
  unitDecimals: 0,
  quantity: '18',
  promisedDeliveryDate: '2027-03-10',
  receivedQuantity: '10',
  acceptedQuantity: '10',
  rejectedQuantity: '0',
  remainingQuantity: '8',
  receiptCap: '18',
  shortClosed: false,
};

const hours: PurchaseOrderLine = {
  ...line,
  lineNumber: 2,
  unitCode: 'H',
  unitDecimals: 2,
  remainingQuantity: '2.5',
};

function order(overrides: Partial<PurchaseOrder> = {}): PurchaseOrder {
  return {
    id: 'po-1',
    status: 'Issued',
    version: '4',
    requisitionId: 'req-1',
    requisitionNumber: 'REQ-2027-000001',
    rfqId: 'rfq-1',
    rfqNumber: 'RFQ-2027-000001',
    supplier: {
      supplierId: 's1',
      legalName: 'Fornecedor Alfa',
      country: 'BR',
      taxId: '11222333000181',
    },
    currency: 'BRL',
    paymentTerms: '30 dias',
    lines: [line],
    shortClosed: false,
    documentStatus: 'Ready',
    documentAttempts: 1,
    notificationStatus: 'Sent',
    notificationAttempts: 1,
    receivingPolicy: 'ReceivingPolicy:1',
    pricesVisible: true,
    allowedActions: ['receive'],
    ...overrides,
  };
}

describe('PurchasingUseCases', () => {
  it('only offers the actions the server allows', () => {
    expect(PurchasingUseCases.can(order(), 'receive')).toBe(true);
    expect(PurchasingUseCases.can(order(), 'issue')).toBe(false);
    expect(PurchasingUseCases.can(undefined, 'receive')).toBe(false);
  });

  it('mirrors the 10–500 character reason rule', () => {
    expect(PurchasingUseCases.reasonError('curto')).toBe('required');
    expect(PurchasingUseCases.reasonError('  Motivo suficiente  ')).toBeUndefined();
    expect(PurchasingUseCases.reasonError('x'.repeat(501))).toBe('too-long');
    expect(PurchasingUseCases.reasonError(undefined)).toBe('required');
  });

  it('compares and adds decimal strings exactly', () => {
    expect(PurchasingUseCases.compare('0.1', '0.10')).toBe(0);
    expect(PurchasingUseCases.compare('8.001', '8')).toBe(1);
    expect(PurchasingUseCases.compare('2', '10')).toBe(-1);
    expect(PurchasingUseCases.add('0.1', '0.2')).toBe('0.3');
    expect(PurchasingUseCases.add('1.25', '2.75')).toBe('4');
    expect(PurchasingUseCases.places('1.500')).toBe(1);
    expect(PurchasingUseCases.places('abc')).toBeUndefined();
    expect(PurchasingUseCases.places('-1')).toBeUndefined();
  });

  it('flags RCV-002/004 issues and a quantity above the remaining balance before posting', () => {
    const draft = { poLineNumber: 1, received: '4', accepted: '4', rejected: '' };
    expect(PurchasingUseCases.lineIssues(draft, line)).toEqual([]);
    expect(PurchasingUseCases.lineIssues({ ...draft, received: '9', accepted: '9' }, line)).toEqual(
      ['over-remaining'],
    );
    expect(PurchasingUseCases.lineIssues({ ...draft, accepted: '3' }, line)).toEqual([
      'unbalanced',
    ]);
    expect(
      PurchasingUseCases.lineIssues({ ...draft, received: '1.5', accepted: '1.5' }, line),
    ).toEqual(['decimals']);
    expect(
      PurchasingUseCases.lineIssues({ ...draft, received: '1.5', accepted: '1.5' }, hours),
    ).toEqual([]);
    expect(PurchasingUseCases.lineIssues({ ...draft, received: 'x' }, line)).toEqual([
      'received-required',
    ]);
    expect(PurchasingUseCases.lineIssues({ ...draft, received: '0', accepted: '0' }, line)).toEqual(
      ['received-required'],
    );
    expect(PurchasingUseCases.lineIssues({ ...draft, accepted: '2', rejected: '2' }, line)).toEqual(
      ['discrepancy-type', 'discrepancy-notes'],
    );
    expect(
      PurchasingUseCases.lineIssues(
        {
          ...draft,
          accepted: '2',
          rejected: '2',
          discrepancyType: 'Damaged',
          discrepancyNotes: 'Caixas amassadas',
        },
        line,
      ),
    ).toEqual([]);
    expect(
      PurchasingUseCases.lineIssues(
        {
          ...draft,
          accepted: '2',
          rejected: '2',
          discrepancyType: 'Other',
          discrepancyNotes: 'x'.repeat(1001),
        },
        line,
      ),
    ).toEqual(['discrepancy-notes']);
  });

  it('posts only lines with a received quantity', () => {
    expect(
      PurchasingUseCases.activeLines([
        { poLineNumber: 1, received: '', accepted: '', rejected: '' },
        { poLineNumber: 2, received: '0', accepted: '0', rejected: '' },
        { poLineNumber: 3, received: '1', accepted: '1', rejected: '' },
      ]).map((value) => value.poLineNumber),
    ).toEqual([3]);
  });

  it('classifies stale errors and maps stable problem codes to translation keys', () => {
    expect(
      PurchasingUseCases.isStale({
        kind: 'version-mismatch',
        code: 'concurrency.version-mismatch',
      }),
    ).toBe(true);
    expect(PurchasingUseCases.isStale({ kind: 'conflict', code: 'purchase-order.immutable' })).toBe(
      true,
    );
    expect(
      PurchasingUseCases.isStale({ kind: 'business-rule', code: 'receipt.over-receipt' }),
    ).toBe(false);
    expect(
      PurchasingUseCases.problemKey({ kind: 'business-rule', code: 'receipt.over-receipt' }),
    ).toBe('purchasing.problems.receipt-over-receipt');
    expect(
      PurchasingUseCases.problemKey({ kind: 'unexpected', code: 'unexpected' }),
    ).toBeUndefined();
  });
});
