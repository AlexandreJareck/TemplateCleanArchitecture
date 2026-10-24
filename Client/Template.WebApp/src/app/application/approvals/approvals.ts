import type { ApplicationError } from '../common/application-error';
import type { Result } from '../common/result';
import type { Page } from '../master-data/master-data';
import type { Money } from '../procurement/procurement';

export type ApprovalStatus =
  'Pending' | 'Approved' | 'Rejected' | 'Cancelled' | 'Expired' | 'Invalidated';

export type ApprovalStepStatus =
  'Waiting' | 'Pending' | 'Approved' | 'Rejected' | 'Cancelled' | 'Expired';

export type ApprovalDecision = 'Approved' | 'Rejected';

export type ApprovalAction = 'approve' | 'reject' | 'delegate' | 'reassign' | 'cancel';

/** Inbox row; every value is the server's (amount in base currency, due time, escalation). */
export interface ApprovalSummary {
  readonly id: string;
  readonly version: string;
  readonly status: ApprovalStatus;
  readonly requisitionId: string;
  readonly requisitionNumber: string;
  readonly amount: Money;
  readonly currentLevel: number;
  readonly finalLevel: number;
  readonly assigneeUserId?: string;
  readonly assigneeRole?: string;
  readonly dueAt?: string;
  readonly escalated: boolean;
  readonly startedAt: string;
}

export interface ApprovalStep {
  readonly level: number;
  readonly resolver: string;
  readonly status: ApprovalStepStatus;
  readonly assigneeUserId?: string;
  readonly assigneeName?: string;
  readonly assigneeRole?: string;
  readonly assignedAt?: string;
  readonly dueAt?: string;
  readonly reminderDueAt?: string;
  readonly remindedAt?: string;
  readonly escalationCount: number;
  readonly decision?: ApprovalDecision;
  readonly decidedByName?: string;
  readonly decidedAt?: string;
  readonly reason?: string;
}

export interface ApprovalComparisonEntry {
  readonly supplierId: string;
  readonly supplierName: string;
  readonly rank?: number;
  readonly valid: boolean;
  readonly quoted: Money;
  readonly normalized?: Money;
  readonly deliveryDays: number;
  readonly awarded: boolean;
}

/** The decision subject frozen at award submission, with the comparison snapshot shown to participants. */
export interface ApprovalAward {
  readonly rfqId: string;
  readonly rfqNumber: string;
  readonly supplierName: string;
  readonly rank: number;
  readonly awarded: Money;
  readonly normalized: Money;
  readonly justification?: string;
  readonly waivers: readonly string[];
  readonly comparison: readonly ApprovalComparisonEntry[];
}

export interface ApprovalInstance {
  readonly id: string;
  readonly version: string;
  readonly status: ApprovalStatus;
  readonly requisitionId: string;
  readonly requisitionNumber: string;
  readonly requesterName?: string;
  readonly amount: Money;
  readonly requisitionAmount: Money;
  readonly policy: string;
  readonly currentLevel: number;
  readonly finalLevel: number;
  readonly nextAction: string;
  readonly startedAt: string;
  readonly endedAt?: string;
  readonly endCause?: string;
  readonly endReason?: string;
  readonly steps: readonly ApprovalStep[];
  readonly award?: ApprovalAward;
  /** Server-computed (state, assignment, permissions, separation of duties); the UI never derives eligibility. */
  readonly allowedActions: readonly ApprovalAction[];
}

export interface ApprovalHistoryEntry {
  readonly sequence: number;
  readonly action: string;
  readonly level?: number;
  readonly actorName?: string;
  readonly assigneeRole?: string;
  readonly at: string;
  readonly reason?: string;
}

export interface ApprovalDelegate {
  readonly userId: string;
  readonly userName: string;
  readonly displayName?: string;
  readonly eligible: boolean;
  readonly ineligibleReason?: string;
}

export interface ApprovalQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly status?: string;
  readonly escalated?: boolean;
}

/** Client needs of the approval inbox and detail; the browser talks to the v2 API only, never to the workflow host. */
export interface ApprovalPort {
  inbox(query: ApprovalQuery, signal?: AbortSignal): Promise<Result<Page<ApprovalSummary>>>;
  get(id: string, signal?: AbortSignal): Promise<Result<ApprovalInstance>>;
  history(id: string, signal?: AbortSignal): Promise<Result<readonly ApprovalHistoryEntry[]>>;
  decide(
    instance: ApprovalInstance,
    decision: ApprovalDecision,
    reason: string | undefined,
    signal?: AbortSignal,
  ): Promise<Result<ApprovalInstance>>;
  delegates(
    instance: ApprovalInstance,
    query: string,
    signal?: AbortSignal,
  ): Promise<Result<Page<ApprovalDelegate>>>;
  delegate(
    instance: ApprovalInstance,
    delegateUserId: string,
    reason: string,
    signal?: AbortSignal,
  ): Promise<Result<ApprovalInstance>>;
  cancel(
    instance: ApprovalInstance,
    reason: string,
    signal?: AbortSignal,
  ): Promise<Result<ApprovalInstance>>;
}

/** Reasons are 10–500 characters (VAL mirror of APR-009/010/015); the API validates again. */
export const REASON = { min: 10, max: 500 } as const;

/** A step due within this window is shown as "due soon" (display only; deadlines are enforced by the server). */
export const DUE_SOON_MS = 24 * 60 * 60 * 1000;

export type DueState = 'overdue' | 'due-soon' | 'on-time' | 'none';

/** Approval presentation rules: state and allowed actions only; every approval rule is decided by the API. */
export class ApprovalUseCases {
  static can(instance: ApprovalInstance | undefined, action: ApprovalAction): boolean {
    return instance?.allowedActions.includes(action) ?? false;
  }

  static currentStep(instance: ApprovalInstance): ApprovalStep | undefined {
    return instance.steps.find((step) => step.level === instance.currentLevel);
  }

  /** VAL-class mirror: rejection, delegation and cancellation need a 10–500 character reason; approval comments are optional. */
  static reasonError(
    decision: ApprovalDecision | 'delegate' | 'cancel',
    reason: string | undefined,
  ): 'required' | 'too-long' | undefined {
    const trimmed = reason?.trim() ?? '';
    if (trimmed.length > REASON.max) return 'too-long';
    if (decision === 'Approved') return undefined;
    return trimmed.length < REASON.min ? 'required' : undefined;
  }

  /** Errors that mean the screen shows an outdated state: reload instead of retrying the same action. */
  static isStale(error: ApplicationError): boolean {
    return (
      error.kind === 'version-mismatch' ||
      error.code === 'approval.step-already-decided' ||
      error.code === 'approval.instance-ended' ||
      error.code === 'approval.step-not-current'
    );
  }

  /** Display classification of a due time against the client clock. */
  static dueState(dueAt: string | undefined, now: number): DueState {
    if (!dueAt) return 'none';
    const due = Date.parse(dueAt);
    if (Number.isNaN(due)) return 'none';
    if (due <= now) return 'overdue';
    return due - now <= DUE_SOON_MS ? 'due-soon' : 'on-time';
  }

  /** Translation key of a stable approval problem code; others use the generic Problem Details message. */
  static problemKey(error: ApplicationError): string | undefined {
    const known = [
      'approval.not-assignee',
      'approval.self-approval',
      'approval.duplicate-approver',
      'approval.maker-checker',
      'approval.invalid-delegate',
      'approval.step-already-decided',
      'approval.instance-ended',
      'approval.step-not-current',
      'approval.workflow-unavailable',
    ];
    return known.includes(error.code)
      ? `approvals.problems.${error.code.slice('approval.'.length)}`
      : undefined;
  }
}
