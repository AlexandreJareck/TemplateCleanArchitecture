import { describe, expect, it } from 'vitest';
import { RequisitionUseCases, type Requisition, type TimelineEntry } from './procurement';

const requisition: Requisition = {
  id: 'r1',
  number: 'REQ-2027-000001',
  requesterId: 'u1',
  costCenter: { id: 'c1', code: 'CC', name: 'Support' },
  currency: 'BRL',
  estimatedTotal: { amount: '300.00', currency: 'BRL' },
  status: 'Draft',
  revision: 1,
  createdAt: '2027-03-01T13:00:00Z',
  version: '3',
  lines: [],
  attachments: [],
  submissions: [],
  allowedActions: ['edit', 'submit'],
};

describe('RequisitionUseCases', () => {
  it('previews line totals and the requisition total from decimal strings', () => {
    const preview = RequisitionUseCases.preview([
      { quantity: '3', unitPrice: '33.3333' },
      { quantity: '2', unitPrice: '0.125' },
      { quantity: 'x', unitPrice: '1' },
    ]);
    expect(preview.lineTotals).toEqual(['100.00', '0.25', undefined]);
    expect(preview.total).toBe('100.25');
  });

  it('uses only server-provided allowed actions and availability', () => {
    expect(RequisitionUseCases.can(requisition, 'submit')).toBe(true);
    expect(RequisitionUseCases.can(requisition, 'cancel')).toBe(false);
    expect(RequisitionUseCases.can(undefined, 'edit')).toBe(false);
    expect(
      RequisitionUseCases.fitsBudget(requisition, {
        hasOpenAllocation: true,
        asOfDate: '2027-03-01',
        available: { amount: '299.99', currency: 'BRL' },
      }),
    ).toBe(false);
    expect(
      RequisitionUseCases.fitsBudget(requisition, {
        hasOpenAllocation: false,
        asOfDate: '2027-03-01',
      }),
    ).toBe(false);
  });

  it('finds the closing reason of a cancelled, rejected or expired requisition', () => {
    const timeline: TimelineEntry[] = [
      { kind: 'state', at: '1', to: 'Draft' },
      { kind: 'reservation', at: '2', to: 'Released', reasonCode: 'Cancelled' },
      { kind: 'state', at: '3', to: 'Cancelled', reason: 'Duplicated request' },
    ];
    expect(RequisitionUseCases.closingReason(timeline)?.reason).toBe('Duplicated request');
    expect(RequisitionUseCases.closingReason(timeline.slice(0, 1))).toBeUndefined();
  });
});
