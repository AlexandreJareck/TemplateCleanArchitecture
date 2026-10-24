import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { ApprovalInstancesService } from '../generated/approval-instances/approval-instances.service';
import { ApprovalApiAdapter, mapInstance, mapSummary } from './approval-api-adapter';

interface Options {
  headers?: Record<string, string>;
}

const view = {
  id: 'approval-1',
  version: '3',
  status: 'Pending',
  requisitionId: 'req-1',
  requisitionNumber: 'REQ-2027-000001',
  amountEvaluated: { amount: '16200.00', currency: 'BRL' },
  requisitionAmount: { amount: '16200.00', currency: 'BRL' },
  policy: 'ApprovalPolicy:1',
  currentLevel: 2,
  finalLevel: 2,
  nextAction: 'Wait',
  startedAt: '2027-03-01T13:00:00Z',
  steps: [
    { level: 2, resolver: 'DepartmentHead', status: 'Pending', escalationCount: 1 },
    { level: 1, resolver: 'CostCenterManager', status: 'Approved', decision: 'Approved' },
  ],
  award: {
    rfqId: 'rfq-1',
    rfqNumber: 'RFQ-1',
    supplierName: 'Alfa',
    rank: 1,
    awarded: { amount: '16200.00', currency: 'BRL' },
    normalized: { amount: '16200.00', currency: 'BRL' },
    comparison: [{ supplierId: 's1', supplierName: 'Alfa', rank: 1, isValid: true }],
  },
  allowedActions: ['approve', 'reject', 'unknown-action'],
};

function adapter(service: object) {
  TestBed.configureTestingModule({
    providers: [ApprovalApiAdapter, { provide: ApprovalInstancesService, useValue: service }],
  });
  return TestBed.inject(ApprovalApiAdapter);
}

describe('ApprovalApiAdapter', () => {
  it('maps the instance without inventing values and keeps steps in level order', () => {
    const instance = mapInstance(view);
    expect(instance.steps.map((step) => step.level)).toEqual([1, 2]);
    expect(instance.allowedActions).toEqual(['approve', 'reject']);
    expect(instance.amount).toEqual({ amount: '16200.00', currency: 'BRL' });
    expect(instance.award?.comparison[0]).toMatchObject({ valid: true, normalized: undefined });
    expect(mapInstance({ status: 'Weird', steps: [{ status: 'Odd' }] })).toMatchObject({
      status: 'Pending',
      steps: [{ status: 'Waiting', escalationCount: 0 }],
      award: undefined,
    });
    expect(mapSummary({ escalated: true }).amount).toEqual({ amount: '0.00', currency: '' });
  });

  it('asks the inbox for the caller assignments ordered by due time', async () => {
    let params: Record<string, unknown> = {};
    const subject = adapter({
      getApiV2ApprovalInstances: (value: Record<string, unknown>) => {
        params = value;
        return of({ items: [view], page: 1, pageSize: 20, totalItems: 1, totalPages: 1 });
      },
    });
    const result = await subject.inbox({
      page: 1,
      pageSize: 20,
      status: 'Pending',
      escalated: true,
    });
    expect(params).toMatchObject({
      assignedTo: 'me',
      status: 'Pending',
      escalated: true,
      sort: 'dueAt',
    });
    expect(result.ok && result.value.items[0]!.requisitionNumber).toBe('REQ-2027-000001');
  });

  it('sends the quoted version and a fresh Idempotency-Key on every change', async () => {
    const calls: { level?: number; body: Record<string, unknown>; options: Options }[] = [];
    const subject = adapter({
      postApiV2ApprovalInstancesIdStepsLevelDecision: (
        _id: string,
        level: number,
        body: Record<string, unknown>,
        options: Options,
      ) => {
        calls.push({ level, body, options });
        return of(view);
      },
      postApiV2ApprovalInstancesIdStepsLevelDelegate: (
        _id: string,
        level: number,
        body: Record<string, unknown>,
        options: Options,
      ) => {
        calls.push({ level, body, options });
        return of(view);
      },
      postApiV2ApprovalInstancesIdCancel: (
        _id: string,
        body: Record<string, unknown>,
        options: Options,
      ) => {
        calls.push({ body, options });
        return of({ ...view, status: 'Cancelled' });
      },
    });
    const instance = mapInstance(view);
    await subject.decide(instance, 'Approved', '   ');
    await subject.decide(instance, 'Rejected', '  Price above the reference  ');
    await subject.delegate(instance, 'user-2', 'Vacation coverage');
    const cancelled = await subject.cancel(instance, 'Supplier withdrew');
    expect(calls[0]).toMatchObject({ level: 2, body: { decision: 'Approved', reason: null } });
    expect(calls[1]!.body).toEqual({ decision: 'Rejected', reason: 'Price above the reference' });
    expect(calls[2]!.body).toEqual({ delegateUserId: 'user-2', reason: 'Vacation coverage' });
    expect(calls.every((call) => call.options.headers!['If-Match'] === '"3"')).toBe(true);
    const keys = new Set(calls.map((call) => call.options.headers!['Idempotency-Key']));
    expect(keys.size).toBe(4);
    expect(cancelled.ok && cancelled.value.status).toBe('Cancelled');
  });

  it('maps a stale version and a workflow outage to Application errors', async () => {
    const subject = adapter({
      postApiV2ApprovalInstancesIdStepsLevelDecision: () =>
        throwError(
          () =>
            new HttpErrorResponse({
              status: 412,
              error: { code: 'concurrency.version-mismatch', currentVersion: '4' },
            }),
        ),
      getApiV2ApprovalInstancesId: () =>
        throwError(
          () =>
            new HttpErrorResponse({
              status: 503,
              error: { code: 'approval.workflow-unavailable' },
            }),
        ),
    });
    const stale = await subject.decide(mapInstance(view), 'Approved', undefined);
    expect(stale.ok ? undefined : stale.error.kind).toBe('version-mismatch');
    const outage = await subject.get('approval-1');
    expect(outage.ok ? undefined : outage.error).toMatchObject({
      kind: 'unavailable',
      code: 'approval.workflow-unavailable',
    });
  });

  it('maps delegate candidates with their ineligibility reason', async () => {
    const subject = adapter({
      getApiV2ApprovalInstancesIdStepsLevelDelegates: (
        _id: string,
        level: number,
        params: Record<string, unknown>,
      ) => {
        expect(level).toBe(2);
        expect(params['q']).toBeUndefined();
        return of({
          items: [
            {
              userId: 'u1',
              userName: 'otto',
              eligible: false,
              ineligibleReason: 'separation-of-duties',
            },
          ],
          page: 1,
          pageSize: 20,
          totalItems: 1,
          totalPages: 1,
        });
      },
    });
    const result = await subject.delegates(mapInstance(view), '  ');
    expect(result.ok && result.value.items[0]).toEqual({
      userId: 'u1',
      userName: 'otto',
      displayName: undefined,
      eligible: false,
      ineligibleReason: 'separation-of-duties',
    });
  });
});
