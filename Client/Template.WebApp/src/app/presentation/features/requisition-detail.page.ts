import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import {
  RequisitionUseCases,
  type BudgetAvailability,
  type Requisition,
  type TimelineEntry,
} from '@application/procurement/procurement';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { ActionConfirmation, type ActionConfirmationRequest } from '../shared/action-confirmation';
import { MoneyPipe, ProblemFeedback, StatusBadge } from '../shared/procurement-ui';
import { SessionFacade } from '../state/session.facade';
import { REQUISITION_PORT } from '../state/ports';

type PendingAction = 'withdraw' | 'cancel' | undefined;

/**
 * Requisition detail (REQ-011 readable): lines, totals, submission snapshots, the state and reservation timeline
 * (NFR-AUD-2), the closing reason of cancelled/rejected/expired requisitions and the actions the server allows.
 */
@Component({
  selector: 'app-requisition-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    TranslocoPipe,
    HlmButton,
    ActionConfirmation,
    MoneyPipe,
    ProblemFeedback,
    StatusBadge,
  ],
  template: `
    <header class="page-title">
      <p>
        {{ 'shell.breadcrumbHome' | transloco }} /
        <a routerLink="/requisitions/mine">{{ 'nav.myRequisitions' | transloco }}</a> /
        {{ requisition()?.number }}
      </p>
      <h1>{{ requisition()?.number ?? ('common.loading' | transloco) }}</h1>
    </header>
    <app-problem-feedback [error]="error()" (reload)="load()" />
    @if (requisition(); as current) {
      <section class="panel" aria-labelledby="summary-title">
        <h2 id="summary-title">{{ 'requisition.header' | transloco }}</h2>
        <dl class="summary-grid">
          <div>
            <dt>{{ 'fields.status' | transloco }}</dt>
            <dd><app-status-badge [status]="current.status" /></dd>
          </div>
          <div>
            <dt>{{ 'requisition.revision' | transloco }}</dt>
            <dd>{{ current.revision }}</dd>
          </div>
          <div>
            <dt>{{ 'requisition.costCenter' | transloco }}</dt>
            <dd>{{ current.costCenter.code }} — {{ current.costCenter.name }}</dd>
          </div>
          <div>
            <dt>{{ 'requisition.neededBy' | transloco }}</dt>
            <dd>{{ current.neededBy ?? '—' }}</dd>
          </div>
          <div>
            <dt>{{ 'requisition.serverTotal' | transloco }}</dt>
            <dd data-testid="server-total">{{ current.estimatedTotal | money }}</dd>
          </div>
        </dl>
        @if (current.purpose) {
          <p>{{ current.purpose }}</p>
        }
        @if (closing(); as reason) {
          <p class="warning" data-testid="closing-reason">
            {{ 'statuses.' + reason.to | transloco }}:
            {{ reason.reason ?? ('requisition.systemReason' | transloco) }}
          </p>
        }
        @if (current.sourceRequisitionId) {
          <p class="notice">
            <a [routerLink]="['/requisitions', current.sourceRequisitionId]">{{
              'requisition.copiedFrom' | transloco
            }}</a>
          </p>
        }
      </section>

      <section class="panel" aria-labelledby="detail-lines-title">
        <h2 id="detail-lines-title">{{ 'requisition.lines' | transloco }}</h2>
        <div
          class="table-scroll"
          role="region"
          tabindex="0"
          [attr.aria-label]="'requisition.linesTable' | transloco"
        >
          <table>
            <thead>
              <tr>
                <th>#</th>
                <th>{{ 'requisition.description' | transloco }}</th>
                <th>{{ 'requisition.quantity' | transloco }}</th>
                <th>{{ 'requisition.unitPrice' | transloco }}</th>
                <th>{{ 'requisition.lineTotal' | transloco }}</th>
              </tr>
            </thead>
            <tbody>
              @for (line of current.lines; track line.lineNumber) {
                <tr>
                  <td data-label="#">{{ line.lineNumber }}</td>
                  <td [attr.data-label]="'requisition.description' | transloco">
                    {{ line.description }}
                  </td>
                  <td [attr.data-label]="'requisition.quantity' | transloco">
                    {{ line.quantity }} {{ line.unitCode }}
                  </td>
                  <td [attr.data-label]="'requisition.unitPrice' | transloco">
                    {{ line.estimatedUnitPrice | money }}
                  </td>
                  <td [attr.data-label]="'requisition.lineTotal' | transloco">
                    {{ line.lineTotal | money }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (current.attachments.length > 0) {
          <h3>{{ 'requisition.attachments' | transloco }}</h3>
          <ul>
            @for (attachment of current.attachments; track attachment.id) {
              <li>{{ attachment.fileName }} ({{ attachment.status }})</li>
            }
          </ul>
        }
      </section>

      @if (availability(); as budget) {
        <section class="panel" aria-labelledby="detail-budget-title">
          <h2 id="detail-budget-title">{{ 'budget.availability' | transloco }}</h2>
          @if (budget.hasOpenAllocation) {
            <dl class="summary-grid">
              <div>
                <dt>{{ 'budget.reserved' | transloco }}</dt>
                <dd>{{ budget.reserved | money }}</dd>
              </div>
              <div>
                <dt>{{ 'budget.available' | transloco }}</dt>
                <dd>{{ budget.available | money }}</dd>
              </div>
            </dl>
          } @else {
            <p class="warning">{{ 'budget.noOpenAllocation' | transloco }}</p>
          }
        </section>
      }

      <section class="panel" aria-labelledby="timeline-title">
        <h2 id="timeline-title">{{ 'requisition.timeline' | transloco }}</h2>
        <ol class="timeline">
          @for (entry of timeline(); track $index) {
            <li>
              <strong>{{
                (entry.kind === 'state' ? 'requisition.stateEntry' : 'requisition.reservationEntry')
                  | transloco
              }}</strong>
              {{ entry.from ? ('statuses.' + entry.from | transloco) + ' → ' : ''
              }}{{ 'statuses.' + entry.to | transloco }}
              @if (entry.amount) {
                · {{ entry.amount | money }}
              }
              @if (entry.reasonCode) {
                · {{ entry.reasonCode }}
              }
              @if (entry.reason) {
                · “{{ entry.reason }}”
              }
              <br /><small
                >{{ entry.at }} ·
                {{ entry.actorId ?? ('requisition.systemActor' | transloco) }}</small
              >
            </li>
          }
        </ol>
      </section>

      <div class="actions panel">
        @if (can('edit')) {
          <a hlmBtn [routerLink]="['/requisitions', current.id, 'edit']">{{
            'common.edit' | transloco
          }}</a>
        }
        @if (can('withdraw')) {
          <button hlmBtn variant="outline" type="button" (click)="pending.set('withdraw')">
            {{ 'requisition.withdraw' | transloco }}
          </button>
        }
        @if (can('cancel')) {
          <button hlmBtn variant="outline" type="button" (click)="pending.set('cancel')">
            {{ 'requisition.cancel' | transloco }}
          </button>
        }
        @if (can('copy')) {
          <button hlmBtn variant="outline" type="button" (click)="copy()">
            {{ 'requisition.copy' | transloco }}
          </button>
        }
      </div>
    }
    <app-action-confirmation
      [request]="confirmation()"
      (confirmed)="confirm($event)"
      (cancelled)="pending.set(undefined)"
    />
  `,
  styleUrl: './data-pages.css',
})
export class RequisitionDetailPage {
  private readonly port = inject(REQUISITION_PORT);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly session = inject(SessionFacade);
  private readonly route = inject(ActivatedRoute);
  readonly requisition = signal<Requisition | undefined>(undefined);
  readonly timeline = signal<readonly TimelineEntry[]>([]);
  readonly availability = signal<BudgetAvailability | undefined>(undefined);
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly pending = signal<PendingAction>(undefined);
  readonly closing = computed(() => RequisitionUseCases.closingReason(this.timeline()));
  readonly confirmation = computed<ActionConfirmationRequest | undefined>(() => {
    const action = this.pending();
    if (!action) return undefined;
    return action === 'cancel'
      ? {
          title: this.transloco.translate('requisition.confirmCancel'),
          actionLabel: this.transloco.translate('requisition.cancel'),
          description: this.transloco.translate('requisition.reasonHelp'),
        }
      : {
          title: this.transloco.translate('requisition.confirmWithdraw'),
          actionLabel: this.transloco.translate('requisition.withdraw'),
          description: this.transloco.translate('requisition.withdrawHelp'),
          requireReason: false,
        };
  });

  constructor() {
    this.route.paramMap.subscribe(() => void this.load());
  }

  can(action: Parameters<typeof RequisitionUseCases.can>[1]) {
    return RequisitionUseCases.can(this.requisition(), action);
  }

  async load() {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    const [requisition, timeline] = await Promise.all([this.port.get(id), this.port.history(id)]);
    if (!requisition.ok) {
      this.error.set(requisition.error);
      return;
    }
    this.error.set(undefined);
    this.requisition.set(requisition.value);
    this.timeline.set(timeline.ok ? timeline.value : []);
    if (this.session.can('budget.read')) {
      const budget = await this.port.availability(id);
      this.availability.set(budget.ok ? budget.value : undefined);
    }
  }

  async confirm(reason: string) {
    const action = this.pending();
    const current = this.requisition();
    this.pending.set(undefined);
    if (!current || !action) return;
    const result =
      action === 'cancel'
        ? await this.port.cancel(current, reason)
        : await this.port.withdraw(current);
    if (!result.ok) {
      this.error.set(result.error);
      return;
    }
    await this.load();
  }

  async copy() {
    const current = this.requisition();
    if (!current) return;
    const result = await this.port.copy(current);
    if (result.ok) await this.router.navigate(['/requisitions', result.value.id, 'edit']);
    else this.error.set(result.error);
  }
}
