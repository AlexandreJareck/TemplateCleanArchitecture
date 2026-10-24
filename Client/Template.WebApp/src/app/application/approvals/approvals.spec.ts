import { describe, expect, it } from 'vitest';
import { ApprovalUseCases, DUE_SOON_MS, type ApprovalInstance } from './approvals';

function instance(overrides: Partial<ApprovalInstance> = {}): ApprovalInstance {
  return {
    id: 'approval-1',
    version: '3',
    status: 'Pending',
    requisitionId: 'req-1',
    requisitionNumber: 'REQ-2027-000001',
    amount: { amount: '16200.00', currency: 'BRL' },
    requisitionAmount: { amount: '16200.00', currency: 'BRL' },
    policy: 'ApprovalPolicy:1',
    currentLevel: 2,
    finalLevel: 2,
    nextAction: 'Wait',
    startedAt: '2027-03-01T13:00:00Z',
    steps: [
      {
        level: 1,
        resolver: 'CostCenterManager',
        status: 'Approved',
        escalationCount: 0,
        decision: 'Approved',
      },
      {
        level: 2,
        resolver: 'DepartmentHead',
        status: 'Pending',
        escalationCount: 1,
        dueAt: '2027-03-06T13:00:00Z',
      },
    ],
    allowedActions: ['approve', 'reject'],
    ...overrides,
  };
}

describe('ApprovalUseCases', () => {
  it('only offers the actions the server allows', () => {
    expect(ApprovalUseCases.can(instance(), 'approve')).toBe(true);
    expect(ApprovalUseCases.can(instance(), 'delegate')).toBe(false);
    expect(ApprovalUseCases.can(instance({ allowedActions: [] }), 'reject')).toBe(false);
    expect(ApprovalUseCases.can(undefined, 'approve')).toBe(false);
  });

  it('finds the current step by the server level', () => {
    expect(ApprovalUseCases.currentStep(instance())?.resolver).toBe('DepartmentHead');
    expect(ApprovalUseCases.currentStep(instance({ currentLevel: 9 }))).toBeUndefined();
  });

  it('mirrors the reason validation without deciding the rule', () => {
    expect(ApprovalUseCases.reasonError('Approved', undefined)).toBeUndefined();
    expect(ApprovalUseCases.reasonError('Approved', 'x'.repeat(501))).toBe('too-long');
    expect(ApprovalUseCases.reasonError('Rejected', '   short   ')).toBe('required');
    expect(
      ApprovalUseCases.reasonError('Rejected', 'Price above the market reference'),
    ).toBeUndefined();
    expect(ApprovalUseCases.reasonError('delegate', '')).toBe('required');
    expect(ApprovalUseCases.reasonError('cancel', 'Supplier withdrew the offer')).toBeUndefined();
  });

  it('treats stale versions and decided or ended approvals as a reload, not a retry', () => {
    expect(
      ApprovalUseCases.isStale({ kind: 'version-mismatch', code: 'concurrency.version-mismatch' }),
    ).toBe(true);
    expect(
      ApprovalUseCases.isStale({ kind: 'conflict', code: 'approval.step-already-decided' }),
    ).toBe(true);
    expect(ApprovalUseCases.isStale({ kind: 'conflict', code: 'approval.instance-ended' })).toBe(
      true,
    );
    expect(ApprovalUseCases.isStale({ kind: 'forbidden', code: 'approval.self-approval' })).toBe(
      false,
    );
    expect(
      ApprovalUseCases.isStale({ kind: 'unavailable', code: 'approval.workflow-unavailable' }),
    ).toBe(false);
  });

  it('classifies due times for display only', () => {
    const now = Date.parse('2027-03-05T13:00:00Z');
    expect(ApprovalUseCases.dueState(undefined, now)).toBe('none');
    expect(ApprovalUseCases.dueState('not-a-date', now)).toBe('none');
    expect(ApprovalUseCases.dueState('2027-03-05T12:59:59Z', now)).toBe('overdue');
    expect(ApprovalUseCases.dueState(new Date(now + DUE_SOON_MS).toISOString(), now)).toBe(
      'due-soon',
    );
    expect(ApprovalUseCases.dueState(new Date(now + DUE_SOON_MS + 1).toISOString(), now)).toBe(
      'on-time',
    );
  });

  it('maps known approval codes to dedicated messages', () => {
    expect(ApprovalUseCases.problemKey({ kind: 'forbidden', code: 'approval.maker-checker' })).toBe(
      'approvals.problems.maker-checker',
    );
    expect(
      ApprovalUseCases.problemKey({ kind: 'unavailable', code: 'approval.workflow-unavailable' }),
    ).toBe('approvals.problems.workflow-unavailable');
    expect(ApprovalUseCases.problemKey({ kind: 'validation', code: 'validation' })).toBeUndefined();
  });
});
