import { inject, Injectable } from '@angular/core';
import {
  type ApprovalAction,
  type ApprovalAward,
  type ApprovalDecision,
  type ApprovalDelegate,
  type ApprovalHistoryEntry,
  type ApprovalInstance,
  type ApprovalPort,
  type ApprovalQuery,
  type ApprovalStatus,
  type ApprovalStep,
  type ApprovalStepStatus,
  type ApprovalSummary,
} from '@application/approvals/approvals';
import { failure, success, type Result } from '@application/common/result';
import type { Money } from '@application/procurement/procurement';
import { firstValueFrom, fromEvent, takeUntil, type Observable } from 'rxjs';
import { ApprovalInstancesService } from '../generated/approval-instances/approval-instances.service';
import type { ApprovalAwardView } from '../generated/models/approvalAwardView';
import type { ApprovalHistoryEntryView } from '../generated/models/approvalHistoryEntryView';
import type { ApprovalInstanceView } from '../generated/models/approvalInstanceView';
import type { ApprovalStepView } from '../generated/models/approvalStepView';
import type { ApprovalSummaryView } from '../generated/models/approvalSummaryView';
import type { MoneyView } from '../generated/models/moneyView';
import { mapProblem } from '../../http/problem-mapper';
import { mapPage } from './procurement-api-adapter';

const STATUSES: readonly ApprovalStatus[] = [
  'Pending',
  'Approved',
  'Rejected',
  'Cancelled',
  'Expired',
  'Invalidated',
];
const STEP_STATUSES: readonly ApprovalStepStatus[] = [
  'Waiting',
  'Pending',
  'Approved',
  'Rejected',
  'Cancelled',
  'Expired',
];
const ACTIONS: readonly ApprovalAction[] = ['approve', 'reject', 'delegate', 'reassign', 'cancel'];

/**
 * Generated-client adapter of the Phase 31 approval contract: every change sends the quoted `If-Match` of the
 * instance version and a fresh `Idempotency-Key`; Problem Details become Application errors. Only the v2 API is
 * called — the Durable workflow host is never reachable from the browser.
 */
@Injectable({ providedIn: 'root' })
export class ApprovalApiAdapter implements ApprovalPort {
  private readonly approvals = inject(ApprovalInstancesService);

  inbox(query: ApprovalQuery, signal?: AbortSignal) {
    return this.execute(
      this.approvals.getApiV2ApprovalInstances({
        assignedTo: 'me',
        page: query.page,
        pageSize: query.pageSize,
        status: query.status || undefined,
        escalated: query.escalated,
        sort: 'dueAt',
      }),
      signal,
      (value) => mapPage(value, mapSummary),
    );
  }

  get(id: string, signal?: AbortSignal) {
    return this.execute(this.approvals.getApiV2ApprovalInstancesId(id), signal, mapInstance);
  }

  history(id: string, signal?: AbortSignal) {
    return this.execute(this.approvals.getApiV2ApprovalInstancesIdHistory(id), signal, (value) =>
      (value.entries ?? []).map(mapHistory),
    );
  }

  decide(
    instance: ApprovalInstance,
    decision: ApprovalDecision,
    reason: string | undefined,
    signal?: AbortSignal,
  ) {
    return this.execute(
      this.approvals.postApiV2ApprovalInstancesIdStepsLevelDecision(
        instance.id,
        instance.currentLevel,
        { decision, reason: reason?.trim() || null },
        { headers: changeHeaders(instance) },
      ),
      signal,
      mapInstance,
    );
  }

  delegates(instance: ApprovalInstance, query: string, signal?: AbortSignal) {
    return this.execute(
      this.approvals.getApiV2ApprovalInstancesIdStepsLevelDelegates(
        instance.id,
        instance.currentLevel,
        {
          q: query.trim() || undefined,
          page: 1,
          pageSize: 20,
        },
      ),
      signal,
      (value) =>
        mapPage(value, (item): ApprovalDelegate => ({
          userId: item.userId ?? '',
          userName: item.userName ?? '',
          displayName: item.displayName ?? undefined,
          eligible: item.eligible ?? false,
          ineligibleReason: item.ineligibleReason ?? undefined,
        })),
    );
  }

  delegate(
    instance: ApprovalInstance,
    delegateUserId: string,
    reason: string,
    signal?: AbortSignal,
  ) {
    return this.execute(
      this.approvals.postApiV2ApprovalInstancesIdStepsLevelDelegate(
        instance.id,
        instance.currentLevel,
        { delegateUserId, reason: reason.trim() },
        { headers: changeHeaders(instance) },
      ),
      signal,
      mapInstance,
    );
  }

  cancel(instance: ApprovalInstance, reason: string, signal?: AbortSignal) {
    return this.execute(
      this.approvals.postApiV2ApprovalInstancesIdCancel(
        instance.id,
        { reason: reason.trim() },
        { headers: changeHeaders(instance) },
      ),
      signal,
      mapInstance,
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

function changeHeaders(instance: ApprovalInstance) {
  return { 'If-Match': `"${instance.version}"`, 'Idempotency-Key': crypto.randomUUID() };
}

function money(value: MoneyView | null | undefined): Money {
  return { amount: value?.amount ?? '0.00', currency: value?.currency ?? '' };
}

function status(value: string | null | undefined): ApprovalStatus {
  return STATUSES.find((candidate) => candidate === value) ?? 'Pending';
}

export function mapSummary(value: ApprovalSummaryView): ApprovalSummary {
  return {
    id: value.id ?? '',
    version: value.version ?? '',
    status: status(value.status),
    requisitionId: value.requisitionId ?? '',
    requisitionNumber: value.requisitionNumber ?? '',
    amount: money(value.amountEvaluated),
    currentLevel: value.currentLevel ?? 1,
    finalLevel: value.finalLevel ?? 1,
    assigneeUserId: value.assigneeUserId ?? undefined,
    assigneeRole: value.assigneeRole ?? undefined,
    dueAt: value.dueAt ?? undefined,
    escalated: value.escalated ?? false,
    startedAt: value.startedAt ?? '',
  };
}

function mapStep(value: ApprovalStepView): ApprovalStep {
  return {
    level: value.level ?? 0,
    resolver: value.resolver ?? '',
    status: STEP_STATUSES.find((candidate) => candidate === value.status) ?? 'Waiting',
    assigneeUserId: value.assigneeUserId ?? undefined,
    assigneeName: value.assigneeName ?? undefined,
    assigneeRole: value.assigneeRole ?? undefined,
    assignedAt: value.assignedAt ?? undefined,
    dueAt: value.dueAt ?? undefined,
    reminderDueAt: value.reminderDueAt ?? undefined,
    remindedAt: value.remindedAt ?? undefined,
    escalationCount: value.escalationCount ?? 0,
    decision:
      value.decision === 'Approved' || value.decision === 'Rejected' ? value.decision : undefined,
    decidedByName: value.decidedByName ?? undefined,
    decidedAt: value.decidedAt ?? undefined,
    reason: value.reason ?? undefined,
  };
}

function mapAward(value: ApprovalAwardView | undefined): ApprovalAward | undefined {
  if (!value) return undefined;
  return {
    rfqId: value.rfqId ?? '',
    rfqNumber: value.rfqNumber ?? '',
    supplierName: value.supplierName ?? '',
    rank: value.rank ?? 0,
    awarded: money(value.awarded),
    normalized: money(value.normalized),
    justification: value.justification ?? undefined,
    waivers: value.waivers ?? [],
    comparison: (value.comparison ?? []).map((entry) => ({
      supplierId: entry.supplierId ?? '',
      supplierName: entry.supplierName ?? '',
      rank: entry.rank ?? undefined,
      valid: entry.isValid ?? false,
      quoted: money(entry.quoted),
      normalized: entry.normalized ? money(entry.normalized) : undefined,
      deliveryDays: entry.deliveryDays ?? 0,
      awarded: entry.awarded ?? false,
    })),
  };
}

export function mapInstance(value: ApprovalInstanceView): ApprovalInstance {
  return {
    id: value.id ?? '',
    version: value.version ?? '',
    status: status(value.status),
    requisitionId: value.requisitionId ?? '',
    requisitionNumber: value.requisitionNumber ?? '',
    requesterName: value.requesterName ?? undefined,
    amount: money(value.amountEvaluated),
    requisitionAmount: money(value.requisitionAmount),
    policy: value.policy ?? '',
    currentLevel: value.currentLevel ?? 1,
    finalLevel: value.finalLevel ?? 1,
    nextAction: value.nextAction ?? '',
    startedAt: value.startedAt ?? '',
    endedAt: value.endedAt ?? undefined,
    endCause: value.endCause ?? undefined,
    endReason: value.endReason ?? undefined,
    steps: (value.steps ?? []).map(mapStep).sort((left, right) => left.level - right.level),
    award: mapAward(value.award),
    allowedActions: (value.allowedActions ?? []).filter((action): action is ApprovalAction =>
      ACTIONS.includes(action as ApprovalAction),
    ),
  };
}

function mapHistory(value: ApprovalHistoryEntryView): ApprovalHistoryEntry {
  return {
    sequence: value.sequence ?? 0,
    action: value.action ?? '',
    level: value.level ?? undefined,
    actorName: value.actorName ?? undefined,
    assigneeRole: value.assigneeRole ?? undefined,
    at: value.at ?? '',
    reason: value.reason ?? undefined,
  };
}
