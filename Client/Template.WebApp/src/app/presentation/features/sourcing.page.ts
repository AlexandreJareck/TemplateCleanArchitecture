import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import type { Page } from '@application/master-data/master-data';
import type { RequisitionSummary } from '@application/procurement/procurement';
import type { RfqSummary, SourcingMethod } from '@application/sourcing/sourcing';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { MoneyPipe, ProblemFeedback, StatusBadge } from '../shared/procurement-ui';
import { SessionFacade } from '../state/session.facade';
import { REQUISITION_PORT, SOURCING_PORT } from '../state/ports';

const RFQ_STATUSES = ['', 'Draft', 'Published', 'Closed', 'AwardPending', 'Cancelled'] as const;

const EMPTY = { items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 };

/**
 * Buyer work queue (Phase 29): submitted requisitions waiting for a sourcing round, with a start action per sourcing
 * method, and the RFQ list with status filter. Eligibility (SRC-001) and every rule are decided by the API.
 */
@Component({
  selector: 'app-sourcing-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslocoPipe, HlmButton, MoneyPipe, ProblemFeedback, StatusBadge],
  template: `
    <header class="page-title">
      <p>{{ 'shell.breadcrumbHome' | transloco }} / {{ 'nav.sourcing' | transloco }}</p>
      <h1>{{ 'nav.sourcing' | transloco }}</h1>
    </header>
    <app-problem-feedback [error]="error()" (reload)="load()" />

    @if (session.can('sourcing.rfqs.manage') && session.can('requisitions.read-all')) {
      <section class="panel" aria-labelledby="queue-title">
        <h2 id="queue-title">{{ 'sourcing.queue' | transloco }}</h2>
        @if (queue().items.length === 0) {
          <p class="empty">{{ 'sourcing.queueEmpty' | transloco }}</p>
        } @else {
          <div
            class="table-scroll"
            role="region"
            tabindex="0"
            [attr.aria-label]="'sourcing.queueTable' | transloco"
          >
            <table class="responsive-table">
              <thead>
                <tr>
                  <th>{{ 'requisition.number' | transloco }}</th>
                  <th>{{ 'requisition.costCenter' | transloco }}</th>
                  <th>{{ 'requisition.neededBy' | transloco }}</th>
                  <th>{{ 'requisition.total' | transloco }}</th>
                  <th>{{ 'common.actions' | transloco }}</th>
                </tr>
              </thead>
              <tbody>
                @for (item of queue().items; track item.id) {
                  <tr>
                    <td [attr.data-label]="'requisition.number' | transloco">
                      <a [routerLink]="['/requisitions', item.id]">{{ item.number }}</a>
                    </td>
                    <td [attr.data-label]="'requisition.costCenter' | transloco">
                      {{ item.costCenter.code }} — {{ item.costCenter.name }}
                    </td>
                    <td [attr.data-label]="'requisition.neededBy' | transloco">
                      {{ item.neededBy ?? '—' }}
                    </td>
                    <td [attr.data-label]="'requisition.total' | transloco">
                      {{ item.estimatedTotal | money }}
                    </td>
                    <td class="actions">
                      <button hlmBtn size="sm" type="button" (click)="start(item, 'Competitive')">
                        {{ 'sourcing.startCompetitive' | transloco }}
                      </button>
                      <button
                        hlmBtn
                        size="sm"
                        variant="outline"
                        type="button"
                        (click)="start(item, 'Direct')"
                      >
                        {{ 'sourcing.startDirect' | transloco }}
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    }

    <section class="panel" aria-labelledby="rfqs-title">
      <h2 id="rfqs-title">{{ 'sourcing.rfqs' | transloco }}</h2>
      <div class="toolbar">
        <label for="rfqStatus" class="sr-only">{{ 'fields.status' | transloco }}</label>
        <select
          id="rfqStatus"
          class="select"
          [value]="status()"
          (change)="filter($any($event.target).value)"
        >
          @for (option of statuses; track option) {
            <option [value]="option">
              {{ (option ? 'statuses.' + option : 'requisition.allStatuses') | transloco }}
            </option>
          }
        </select>
      </div>
      @if (rfqs().items.length === 0) {
        <p class="empty">{{ 'common.empty' | transloco }}</p>
      } @else {
        <div
          class="table-scroll"
          role="region"
          tabindex="0"
          [attr.aria-label]="'sourcing.rfqsTable' | transloco"
        >
          <table class="responsive-table">
            <thead>
              <tr>
                <th>{{ 'sourcing.number' | transloco }}</th>
                <th>{{ 'requisition.number' | transloco }}</th>
                <th>{{ 'sourcing.method' | transloco }}</th>
                <th>{{ 'sourcing.deadline' | transloco }}</th>
                <th>{{ 'sourcing.invitations' | transloco }}</th>
                <th>{{ 'fields.status' | transloco }}</th>
                <th>{{ 'common.actions' | transloco }}</th>
              </tr>
            </thead>
            <tbody>
              @for (item of rfqs().items; track item.id) {
                <tr>
                  <td [attr.data-label]="'sourcing.number' | transloco">
                    {{ item.number }} · R{{ item.roundNumber }}
                  </td>
                  <td [attr.data-label]="'requisition.number' | transloco">
                    {{ item.requisitionNumber }}
                  </td>
                  <td [attr.data-label]="'sourcing.method' | transloco">
                    {{ 'sourcing.methods.' + item.method | transloco }}
                  </td>
                  <td [attr.data-label]="'sourcing.deadline' | transloco">
                    {{ item.deadline ?? '—' }}
                  </td>
                  <td [attr.data-label]="'sourcing.invitations' | transloco">
                    {{ item.invitationCount }}
                  </td>
                  <td [attr.data-label]="'fields.status' | transloco">
                    <app-status-badge [status]="item.status" />
                  </td>
                  <td>
                    <a hlmBtn size="sm" variant="outline" [routerLink]="['/sourcing', item.id]">{{
                      'common.details' | transloco
                    }}</a>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  `,
  styleUrl: './data-pages.css',
})
export class SourcingPage {
  private readonly sourcing = inject(SOURCING_PORT);
  private readonly requisitions = inject(REQUISITION_PORT);
  private readonly router = inject(Router);
  protected readonly session = inject(SessionFacade);
  readonly statuses = RFQ_STATUSES;
  readonly queue = signal<Page<RequisitionSummary>>(EMPTY);
  readonly rfqs = signal<Page<RfqSummary>>(EMPTY);
  readonly status = signal('');
  readonly error = signal<ApplicationError | undefined>(undefined);

  constructor() {
    void this.load();
  }

  async load() {
    const [rfqs, queue] = await Promise.all([
      this.sourcing.list({ page: 1, pageSize: 50, status: this.status() }),
      this.session.can('requisitions.read-all')
        ? this.requisitions.list({
            scope: 'visible',
            page: 1,
            pageSize: 50,
            status: 'Submitted',
            sort: 'submittedAt',
          })
        : Promise.resolve(undefined),
    ]);
    if (!rfqs.ok) {
      this.error.set(rfqs.error);
      return;
    }
    this.error.set(undefined);
    this.rfqs.set(rfqs.value);
    if (queue?.ok) this.queue.set(queue.value);
  }

  filter(status: string) {
    this.status.set(status);
    void this.load();
  }

  async start(requisition: RequisitionSummary, method: SourcingMethod) {
    const result = await this.sourcing.create(requisition.id, method);
    if (result.ok) await this.router.navigate(['/sourcing', result.value.id]);
    else this.error.set(result.error);
  }
}
