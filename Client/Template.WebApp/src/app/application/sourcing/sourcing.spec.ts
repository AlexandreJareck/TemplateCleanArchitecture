import { describe, expect, it } from 'vitest';
import { SourcingUseCases, type Rfq } from './sourcing';

const money = (amount: string) => ({ amount, currency: 'BRL' });

function rfq(overrides: Partial<Rfq> = {}): Rfq {
  return {
    id: 'rfq-1',
    number: 'RFQ-2027-000001',
    requisitionId: 'req-1',
    requisitionNumber: 'REQ-2027-000001',
    roundNumber: 1,
    method: 'Competitive',
    status: 'Published',
    invitationCount: 2,
    version: '5',
    requisition: {
      id: 'req-1',
      number: 'REQ-2027-000001',
      revision: 1,
      estimatedTotal: money('18000.00'),
      neededBy: '2027-04-01',
    },
    policyVersion: 1,
    baseCurrency: 'BRL',
    aboveThreshold: true,
    requiredInvitations: 3,
    closedEarly: false,
    lines: [],
    invitations: [
      {
        supplierId: 's1',
        supplierLegalName: 'A',
        supplierTaxId: '1',
        supplierStatusAtInvitation: 'Active',
        invitedAt: '',
        isActive: true,
      },
      {
        supplierId: 's2',
        supplierLegalName: 'B',
        supplierTaxId: '2',
        supplierStatusAtInvitation: 'Active',
        invitedAt: '',
        isActive: true,
      },
      {
        supplierId: 's3',
        supplierLegalName: 'C',
        supplierTaxId: '3',
        supplierStatusAtInvitation: 'Active',
        invitedAt: '',
        removedAt: 'x',
        isActive: false,
      },
    ],
    commercialDetailsVisible: true,
    quotes: [
      {
        id: 'q1',
        supplierId: 's1',
        supplierLegalName: 'A',
        latestStatus: 'Current',
        revisions: [],
      },
    ],
    awards: [],
    waivers: [],
    exchangeRates: [],
    allowedActions: ['enter-quote', 'close-early'],
    ...overrides,
  };
}

describe('SourcingUseCases', () => {
  it('checks server-computed allowed actions only', () => {
    expect(SourcingUseCases.can(rfq(), 'enter-quote')).toBe(true);
    expect(SourcingUseCases.can(rfq(), 'award')).toBe(false);
    expect(SourcingUseCases.can(undefined, 'award')).toBe(false);
  });

  it('lists active invitations and suppliers still without a current quote', () => {
    expect(
      SourcingUseCases.activeInvitations(rfq()).map((invitation) => invitation.supplierId),
    ).toEqual(['s1', 's2']);
    expect(
      SourcingUseCases.supplierWithoutQuote(rfq()).map((invitation) => invitation.supplierId),
    ).toEqual(['s2']);
  });

  it('keeps the server ranking and finds the server recommendation', () => {
    const entries = [
      {
        quoteId: 'q2',
        quoteRevisionId: 'r2',
        supplierId: 's2',
        supplierLegalName: 'B',
        revisionNumber: 1,
        rawTotal: money('9'),
        deliveryDays: 1,
        receivedAt: '',
        validUntil: '',
        coveredLines: 1,
        totalLines: 1,
        isValid: true,
        invalidReasons: [],
        rank: 1,
      },
      {
        quoteId: 'q1',
        quoteRevisionId: 'r1',
        supplierId: 's1',
        supplierLegalName: 'A',
        revisionNumber: 1,
        rawTotal: money('10'),
        deliveryDays: 1,
        receivedAt: '',
        validUntil: '',
        coveredLines: 1,
        totalLines: 1,
        isValid: true,
        invalidReasons: [],
        rank: 2,
      },
    ];
    const current = rfq({
      comparison: {
        sequence: 1,
        status: 'Complete',
        baseCurrency: 'BRL',
        closeLocalDate: '2027-03-04',
        validQuoteCount: 2,
        requiredValidQuotes: 3,
        warnings: [],
        missingRateCurrencies: [],
        recommendedQuoteRevisionId: 'r2',
        entries,
      },
    });
    expect(SourcingUseCases.rankedEntries(current.comparison).map((entry) => entry.rank)).toEqual([
      1, 2,
    ]);
    expect(SourcingUseCases.recommended(current)?.supplierId).toBe('s2');
    expect(SourcingUseCases.recommended(rfq())).toBeUndefined();
  });

  it('maps award problems to the waiver the server asked for', () => {
    expect(SourcingUseCases.waiverForProblem('award.insufficient-quotations')).toBe('insufficient');
    expect(SourcingUseCases.waiverForProblem('award.non-leading-requires-waiver')).toBe(
      'non-leading',
    );
    expect(SourcingUseCases.waiverForProblem('budget.insufficient')).toBeUndefined();
  });

  it('converts a local deadline to a UTC instant and rejects malformed input', () => {
    expect(SourcingUseCases.deadlineInstant('2027-03-04T10:00')).toMatch(
      /^2027-03-0\dT\d{2}:00:00\.000Z$/,
    );
    expect(SourcingUseCases.deadlineInstant('04/03/2027')).toBeUndefined();
  });
});
