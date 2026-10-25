import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  ApprovalUseCases,
  REASON,
  type ApprovalAction,
  type ApprovalDecision,
  type ApprovalDelegate,
  type ApprovalHistoryEntry,
  type ApprovalInstance,
  type DueState,
} from '@application/approvals/approvals';
import type { ApplicationError } from '@application/common/application-error';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { MoneyPipe, ProblemFeedback } from '../shared/procurement-ui';
import { APPROVAL_PORT } from '../state/ports';

type Mode = 'Approved' | 'Rejected' | 'delegate' | 'cancel';

/**
 * Approval detail (Phase 31, `/approvals/:id`): the frozen award and comparison, every step with its assignee, due
 * time and escalation, the timeline, and the decision, delegation and cancellation forms offered only when the server
 * lists the action. A stale version or an already-decided step reloads the current state instead of retrying, and
 * nothing in the browser decides eligibility, amounts or deadlines.
 */
@Component({
  selector: 'app-approval-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, RouterLink, HlmButton, HlmInput, MoneyPipe, ProblemFeedback],
  templateUrl: './approval-detail.page.html',
  styleUrl: './data-pages.css',
})
export class ApprovalDetailPage {
  private readonly approvals = inject(APPROVAL_PORT);
  private readonly transloco = inject(TranslocoService);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly reasonMin = REASON.min;
  protected readonly reasonMax = REASON.max;

  readonly instance = signal<ApprovalInstance | undefined>(undefined);
  readonly history = signal<readonly ApprovalHistoryEntry[]>([]);
  readonly delegates = signal<readonly ApprovalDelegate[]>([]);
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly stale = signal(false);
  readonly announcement = signal('');
  readonly busy = signal(false);
  readonly mode = signal<Mode | undefined>(undefined);
  readonly reason = signal('');
  readonly touched = signal(false);
  readonly delegateQuery = signal('');
  readonly delegateUserId = signal('');

  readonly hasActions = computed(() => (this.instance()?.allowedActions.length ?? 0) > 0);
  readonly reasonError = computed(() => {
    const mode = this.mode();
    return mode ? ApprovalUseCases.reasonError(mode, this.reason()) : undefined;
  });
  readonly showReasonError = computed(() => (this.touched() ? this.reasonError() : undefined));
  readonly reasonLabel = computed(() =>
    this.mode() === 'Approved' ? 'approvals.comment' : 'common.reason',
  );
  readonly problemKey = computed(() => {
    const error = this.error();
    return error ? ApprovalUseCases.problemKey(error) : undefined;
  });

  constructor() {
    void this.load();
  }

  async load() {
    const [instance, history] = await Promise.all([
      this.approvals.get(this.id),
      this.approvals.history(this.id),
    ]);
    if (!instance.ok) return this.error.set(instance.error);
    this.error.set(undefined);
    this.instance.set(instance.value);
    if (history.ok) this.history.set(history.value);
  }

  can(action: ApprovalAction) {
    return ApprovalUseCases.can(this.instance(), action);
  }

  isCurrent(level: number) {
    const current = this.instance();
    return current?.status === 'Pending' && current.currentLevel === level;
  }

  open(mode: Mode) {
    this.mode.set(mode);
    this.reason.set('');
    this.touched.set(false);
    this.stale.set(false);
    if (mode === 'delegate') void this.searchDelegates();
  }

  close() {
    this.mode.set(undefined);
  }

  async searchDelegates() {
    const current = this.instance();
    if (!current) return;
    const result = await this.approvals.delegates(current, this.delegateQuery());
    if (!result.ok) return this.error.set(result.error);
    this.delegates.set(result.value.items);
  }

  async submit() {
    const current = this.instance();
    const mode = this.mode();
    this.touched.set(true);
    if (!current || !mode || this.reasonError()) return;
    if (mode === 'delegate' && !this.delegateUserId()) return;
    this.busy.set(true);
    const reason = this.reason();
    const result =
      mode === 'delegate'
        ? await this.approvals.delegate(current, this.delegateUserId(), reason)
        : mode === 'cancel'
          ? await this.approvals.cancel(current, reason)
          : await this.approvals.decide(current, mode as ApprovalDecision, reason || undefined);
    this.busy.set(false);
    if (!result.ok) {
      this.error.set(result.error);
      if (ApprovalUseCases.isStale(result.error)) {
        this.stale.set(true);
        this.mode.set(undefined);
        await this.load();
        this.error.set(result.error);
      }
      return;
    }
    this.error.set(undefined);
    this.mode.set(undefined);
    this.instance.set(result.value);
    const history = await this.approvals.history(this.id);
    if (history.ok) this.history.set(history.value);
    this.announcement.set(
      this.transloco.translate('approvals.announce', {
        status: this.transloco.translate(`approvals.statuses.${result.value.status}`),
      }),
    );
  }

  due(dueAt: string | undefined): DueState {
    return ApprovalUseCases.dueState(dueAt, Date.now());
  }

  instant(value: string | undefined) {
    if (!value) return '—';
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? value
      : new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
          dateStyle: 'short',
          timeStyle: 'short',
        }).format(date);
  }
}
