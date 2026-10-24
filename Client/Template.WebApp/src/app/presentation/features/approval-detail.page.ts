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
  template: `
    <header class="page-title">
      <p>
        {{ 'shell.breadcrumbHome' | transloco }} /
        <a routerLink="/approvals/inbox">{{ 'nav.approvals' | transloco }}</a>
      </p>
      <h1>
        {{ 'approvals.title' | transloco: { number: instance()?.requisitionNumber ?? '' } }}
      </h1>
    </header>
    <p class="sr-only" aria-live="polite" role="status">{{ announcement() }}</p>
    @if (stale()) {
      <div class="feedback" role="alert">
        <strong>{{ 'approvals.stale' | transloco }}</strong>
      </div>
    }
    @if (problemKey(); as key) {
      <div class="feedback" role="alert">
        <strong>{{ key | transloco }}</strong>
        <span>{{ error()?.code }}</span>
      </div>
    } @else {
      <app-problem-feedback [error]="error()" (reload)="load()" />
    }

    @if (instance(); as current) {
      <section class="panel" aria-labelledby="approval-summary">
        <h2 id="approval-summary">{{ 'approvals.summary' | transloco }}</h2>
        <dl class="summary-grid">
          <dt>{{ 'fields.status' | transloco }}</dt>
          <dd>
            <span class="status-badge" [attr.data-status]="current.status">{{
              'approvals.statuses.' + current.status | transloco
            }}</span>
          </dd>
          <dt>{{ 'approvals.requisition' | transloco }}</dt>
          <dd>
            <a [routerLink]="['/requisitions', current.requisitionId]">{{
              current.requisitionNumber
            }}</a>
          </dd>
          @if (current.requesterName) {
            <dt>{{ 'approvals.requester' | transloco }}</dt>
            <dd>{{ current.requesterName }}</dd>
          }
          <dt>{{ 'approvals.amount' | transloco }}</dt>
          <dd>{{ current.amount | money }}</dd>
          <dt>{{ 'approvals.level' | transloco }}</dt>
          <dd>{{ current.currentLevel }}/{{ current.finalLevel }}</dd>
          <dt>{{ 'approvals.policy' | transloco }}</dt>
          <dd>{{ current.policy }}</dd>
          @if (current.endedAt) {
            <dt>{{ 'approvals.endedAt' | transloco }}</dt>
            <dd>
              {{ instant(current.endedAt) }}
              @if (current.endReason) {
                · “{{ current.endReason }}”
              }
            </dd>
          }
        </dl>
      </section>

      @if (current.award; as award) {
        <section class="panel" aria-labelledby="approval-award">
          <h2 id="approval-award">{{ 'approvals.award' | transloco }}</h2>
          <p>
            {{ award.rfqNumber }} · <strong>{{ award.supplierName }}</strong> ·
            {{ award.awarded | money }} ({{ award.normalized | money }}) ·
            {{ 'approvals.rank' | transloco: { rank: award.rank } }}
          </p>
          @if (award.justification) {
            <p class="notice">{{ award.justification }}</p>
          }
          @for (waiver of award.waivers; track waiver) {
            <p class="notice">{{ 'approvals.waiver' | transloco: { type: waiver } }}</p>
          }
          @if (award.comparison.length > 0) {
            <div
              class="table-scroll"
              role="region"
              tabindex="0"
              [attr.aria-label]="'approvals.comparison' | transloco"
            >
              <table class="responsive-table">
                <thead>
                  <tr>
                    <th>{{ 'approvals.rankColumn' | transloco }}</th>
                    <th>{{ 'approvals.supplier' | transloco }}</th>
                    <th>{{ 'approvals.quoted' | transloco }}</th>
                    <th>{{ 'approvals.normalized' | transloco }}</th>
                    <th>{{ 'approvals.deliveryDays' | transloco }}</th>
                  </tr>
                </thead>
                <tbody>
                  @for (entry of award.comparison; track entry.supplierId) {
                    <tr [attr.data-awarded]="entry.awarded">
                      <td [attr.data-label]="'approvals.rankColumn' | transloco">
                        {{ entry.valid ? entry.rank : ('approvals.invalidQuote' | transloco) }}
                      </td>
                      <td [attr.data-label]="'approvals.supplier' | transloco">
                        {{ entry.supplierName }}
                        @if (entry.awarded) {
                          <strong> · {{ 'approvals.awarded' | transloco }}</strong>
                        }
                      </td>
                      <td [attr.data-label]="'approvals.quoted' | transloco">
                        {{ entry.quoted | money }}
                      </td>
                      <td [attr.data-label]="'approvals.normalized' | transloco">
                        {{ entry.normalized | money }}
                      </td>
                      <td [attr.data-label]="'approvals.deliveryDays' | transloco">
                        {{ entry.deliveryDays }}
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>
      }

      <section class="panel" aria-labelledby="approval-steps">
        <h2 id="approval-steps">{{ 'approvals.steps' | transloco }}</h2>
        <ol class="timeline">
          @for (step of current.steps; track step.level) {
            <li
              [attr.data-step]="step.level"
              [attr.aria-current]="isCurrent(step.level) ? 'step' : null"
            >
              <strong
                >{{ 'approvals.levelLabel' | transloco: { level: step.level } }} ·
                {{ 'approvals.resolvers.' + step.resolver | transloco }}</strong
              >
              · {{ 'approvals.stepStatuses.' + step.status | transloco }}
              @if (step.assigneeName || step.assigneeRole) {
                <br />{{ 'approvals.assignee' | transloco }}:
                {{
                  step.assigneeName ??
                    ('approvals.roleGroup' | transloco: { role: step.assigneeRole })
                }}
              }
              @if (step.status === 'Pending' && step.dueAt) {
                <br />{{ 'approvals.dueAt' | transloco }}: {{ instant(step.dueAt) }}
                @if (due(step.dueAt); as state) {
                  @if (state === 'overdue' || state === 'due-soon') {
                    <span class="warning">{{ 'approvals.due.' + state | transloco }}</span>
                  }
                }
                @if (step.remindedAt) {
                  · {{ 'approvals.remindedAt' | transloco }} {{ instant(step.remindedAt) }}
                }
              }
              @if (step.escalationCount > 0) {
                <br /><span class="notice">{{
                  'approvals.escalations' | transloco: { count: step.escalationCount }
                }}</span>
              }
              @if (step.decidedAt) {
                <br /><small
                  >{{ step.decidedByName ?? '—' }} · {{ instant(step.decidedAt) }}
                  @if (step.reason) {
                    · “{{ step.reason }}”
                  }
                </small>
              }
            </li>
          }
        </ol>
      </section>

      @if (hasActions()) {
        <section class="panel" aria-labelledby="approval-actions">
          <h2 id="approval-actions">{{ 'approvals.decision' | transloco }}</h2>
          <div class="actions">
            @if (can('approve')) {
              <button hlmBtn type="button" (click)="open('Approved')">
                {{ 'approvals.approve' | transloco }}
              </button>
            }
            @if (can('reject')) {
              <button hlmBtn variant="destructive" type="button" (click)="open('Rejected')">
                {{ 'approvals.reject' | transloco }}
              </button>
            }
            @if (can('delegate')) {
              <button hlmBtn variant="outline" type="button" (click)="open('delegate')">
                {{ 'approvals.delegate' | transloco }}
              </button>
            }
            @if (can('cancel')) {
              <button hlmBtn variant="outline" type="button" (click)="open('cancel')">
                {{ 'approvals.cancel' | transloco }}
              </button>
            }
          </div>

          @if (mode(); as active) {
            <form class="form-grid" (submit)="$event.preventDefault(); submit()" novalidate>
              @if (active === 'delegate') {
                <label for="delegateSearch"
                  >{{ 'approvals.delegateSearch' | transloco }}
                  <input
                    id="delegateSearch"
                    hlmInput
                    [value]="delegateQuery()"
                    (input)="delegateQuery.set($any($event.target).value)"
                    (keydown.enter)="$event.preventDefault(); searchDelegates()"
                  />
                </label>
                <button
                  hlmBtn
                  variant="outline"
                  size="sm"
                  type="button"
                  (click)="searchDelegates()"
                >
                  {{ 'common.search' | transloco }}
                </button>
                <label for="delegateUser"
                  >{{ 'approvals.delegateTo' | transloco }}
                  <select
                    id="delegateUser"
                    class="select"
                    [value]="delegateUserId()"
                    (change)="delegateUserId.set($any($event.target).value)"
                  >
                    <option value="">—</option>
                    @for (candidate of delegates(); track candidate.userId) {
                      <option [value]="candidate.userId" [disabled]="!candidate.eligible">
                        {{ candidate.displayName ?? candidate.userName }}
                        @if (candidate.ineligibleReason) {
                          ({{ 'approvals.ineligible.' + candidate.ineligibleReason | transloco }})
                        }
                      </option>
                    }
                  </select>
                </label>
              }
              <label for="approvalReason"
                >{{ reasonLabel() | transloco }}
                <textarea
                  id="approvalReason"
                  class="textarea"
                  rows="3"
                  [attr.maxlength]="reasonMax"
                  [attr.aria-invalid]="showReasonError() ? 'true' : null"
                  [attr.aria-describedby]="
                    showReasonError() ? 'approvalReasonError' : 'approvalReasonHint'
                  "
                  [value]="reason()"
                  (input)="reason.set($any($event.target).value)"
                ></textarea>
              </label>
              @if (showReasonError(); as problem) {
                <p id="approvalReasonError" class="warning">
                  {{
                    'approvals.reasonErrors.' + problem
                      | transloco: { min: reasonMin, max: reasonMax }
                  }}
                </p>
              } @else {
                <p id="approvalReasonHint" class="hint">
                  {{ 'approvals.reasonHint' | transloco: { min: reasonMin, max: reasonMax } }}
                </p>
              }
              <div class="actions">
                <button hlmBtn variant="outline" type="button" (click)="close()">
                  {{ 'common.cancel' | transloco }}
                </button>
                <button hlmBtn type="submit" [disabled]="busy()">
                  {{ 'approvals.confirm.' + active | transloco }}
                </button>
              </div>
            </form>
          }
        </section>
      }

      <section class="panel" aria-labelledby="approval-history">
        <h2 id="approval-history">{{ 'requisition.timeline' | transloco }}</h2>
        <ol class="timeline">
          @for (entry of history(); track entry.sequence) {
            <li>
              <strong>{{ 'approvals.history.' + entry.action | transloco }}</strong>
              @if (entry.level) {
                · {{ 'approvals.levelLabel' | transloco: { level: entry.level } }}
              }
              @if (entry.reason) {
                · “{{ entry.reason }}”
              }
              <br /><small
                >{{ instant(entry.at) }} ·
                {{ entry.actorName ?? ('requisition.systemActor' | transloco) }}</small
              >
            </li>
          }
        </ol>
      </section>
    }
  `,
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
