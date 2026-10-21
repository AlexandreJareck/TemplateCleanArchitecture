import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import type { Page } from '@application/master-data/master-data';
import type { BudgetAllocation, BudgetReservation } from '@application/procurement/procurement';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { ActionConfirmation, type ActionConfirmationRequest } from '../shared/action-confirmation';
import { MoneyPipe, ProblemFeedback, StatusBadge } from '../shared/procurement-ui';
import { SessionFacade } from '../state/session.facade';
import { BUDGET_PORT } from '../state/ports';

/** Allocation detail: totals, amount history (BUD-003), reservation visibility and reasoned amount change / close. */
@Component({
  selector: 'app-budget-allocation-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormField,
    RouterLink,
    TranslocoPipe,
    HlmButton,
    HlmInput,
    ActionConfirmation,
    MoneyPipe,
    ProblemFeedback,
    StatusBadge,
  ],
  template: `
    <header class="page-title">
      <p>
        {{ 'shell.breadcrumbHome' | transloco }} /
        <a routerLink="/budget/allocations">{{ 'nav.budgetAllocations' | transloco }}</a>
      </p>
      <h1>{{ 'budget.allocation' | transloco }}</h1>
    </header>
    <app-problem-feedback [error]="error()" (reload)="load()" />
    @if (allocation(); as current) {
      <section class="panel" aria-labelledby="allocation-summary">
        <h2 id="allocation-summary">{{ 'budget.summary' | transloco }}</h2>
        <dl class="summary-grid">
          <div>
            <dt>{{ 'fields.status' | transloco }}</dt>
            <dd><app-status-badge [status]="current.status" /></dd>
          </div>
          <div>
            <dt>{{ 'budget.amount' | transloco }}</dt>
            <dd>{{ current.amount | money }}</dd>
          </div>
          <div>
            <dt>{{ 'budget.reserved' | transloco }}</dt>
            <dd>{{ current.reserved | money }}</dd>
          </div>
          <div>
            <dt>{{ 'budget.committed' | transloco }}</dt>
            <dd>{{ current.committed | money }}</dd>
          </div>
          <div>
            <dt>{{ 'budget.available' | transloco }}</dt>
            <dd data-testid="allocation-available">{{ current.available | money }}</dd>
          </div>
        </dl>
      </section>
      @if (canManage && current.status === 'Active') {
        <section class="panel" aria-labelledby="amount-change-title">
          <h2 id="amount-change-title">{{ 'budget.changeAmount' | transloco }}</h2>
          <form class="form-grid" (submit)="changeAmount($event)" novalidate>
            <label
              >{{ 'budget.amount' | transloco
              }}<input hlmInput inputmode="decimal" [formField]="amountForm.amount"
            /></label>
            <label
              >{{ 'common.reason' | transloco }}<input hlmInput [formField]="amountForm.reason"
            /></label>
            <div class="actions">
              <button hlmBtn type="submit" [disabled]="amountForm().invalid()">
                {{ 'common.save' | transloco }}
              </button>
              <button hlmBtn variant="outline" type="button" (click)="closing.set(true)">
                {{ 'budget.close' | transloco }}
              </button>
            </div>
          </form>
        </section>
      }
      <section class="panel" aria-labelledby="amount-history-title">
        <h2 id="amount-history-title">{{ 'budget.amountHistory' | transloco }}</h2>
        <ol class="timeline">
          @for (change of current.amountHistory; track change.sequence) {
            <li>
              {{ change.previousAmount | money }} → {{ change.newAmount | money }}
              @if (change.reason) {
                · “{{ change.reason }}”
              }
              <br /><small>{{ change.at }}</small>
            </li>
          }
        </ol>
      </section>
      <section class="panel" aria-labelledby="reservations-title">
        <h2 id="reservations-title">{{ 'budget.reservations' | transloco }}</h2>
        @if (reservations().items.length === 0) {
          <p class="notice">{{ 'common.empty' | transloco }}</p>
        } @else {
          <div
            class="table-scroll"
            role="region"
            tabindex="0"
            [attr.aria-label]="'budget.reservationsTable' | transloco"
          >
            <table>
              <thead>
                <tr>
                  <th>{{ 'budget.requisition' | transloco }}</th>
                  <th>{{ 'requisition.revision' | transloco }}</th>
                  <th>{{ 'budget.amount' | transloco }}</th>
                  <th>{{ 'fields.status' | transloco }}</th>
                  <th>{{ 'budget.releaseReason' | transloco }}</th>
                </tr>
              </thead>
              <tbody>
                @for (reservation of reservations().items; track reservation.id) {
                  <tr>
                    <td [attr.data-label]="'budget.requisition' | transloco">
                      <a [routerLink]="['/requisitions', reservation.requisitionId]">{{
                        reservation.requisitionId.slice(0, 8)
                      }}</a>
                    </td>
                    <td [attr.data-label]="'requisition.revision' | transloco">
                      {{ reservation.requisitionRevision }}
                    </td>
                    <td [attr.data-label]="'budget.amount' | transloco">
                      {{ reservation.amount | money }}
                    </td>
                    <td [attr.data-label]="'fields.status' | transloco">
                      <app-status-badge [status]="reservation.status" />
                    </td>
                    <td [attr.data-label]="'budget.releaseReason' | transloco">
                      {{ reservation.releaseReason ?? '—' }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    }
    <app-action-confirmation
      [request]="closeRequest()"
      (confirmed)="close($event)"
      (cancelled)="closing.set(false)"
    />
  `,
  styleUrl: './data-pages.css',
})
export class BudgetAllocationDetailPage {
  private readonly port = inject(BUDGET_PORT);
  private readonly transloco = inject(TranslocoService);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  readonly canManage = inject(SessionFacade).can('budget.allocations.manage');
  readonly allocation = signal<BudgetAllocation | undefined>(undefined);
  readonly reservations = signal<Page<BudgetReservation>>({
    items: [],
    page: 1,
    pageSize: 100,
    totalItems: 0,
    totalPages: 0,
  });
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly closing = signal(false);
  readonly model = signal({ amount: '', reason: '' });
  readonly amountForm = form(this.model, (path) => {
    required(path.amount);
    required(path.reason);
  });
  readonly closeRequest = computed<ActionConfirmationRequest | undefined>(() =>
    this.closing()
      ? {
          title: this.transloco.translate('budget.confirmCloseAllocation'),
          actionLabel: this.transloco.translate('budget.close'),
        }
      : undefined,
  );

  constructor() {
    void this.load();
  }

  async load() {
    const [allocation, reservations] = await Promise.all([
      this.port.getAllocation(this.id),
      this.port.listReservations(this.id, { page: 1, pageSize: 100 }),
    ]);
    if (!allocation.ok) {
      this.error.set(allocation.error);
      return;
    }
    this.error.set(undefined);
    this.allocation.set(allocation.value);
    this.model.update((value) => ({ ...value, amount: allocation.value.amount.amount }));
    if (reservations.ok) this.reservations.set(reservations.value);
  }

  async changeAmount(event: Event) {
    event.preventDefault();
    const current = this.allocation();
    if (!current || this.amountForm().invalid()) return;
    const result = await this.port.changeAllocationAmount(
      current,
      this.model().amount,
      this.model().reason,
    );
    if (result.ok) {
      this.allocation.set(result.value);
      this.model.set({ amount: result.value.amount.amount, reason: '' });
      this.error.set(undefined);
    } else this.error.set(result.error);
  }

  async close(reason: string) {
    const current = this.allocation();
    this.closing.set(false);
    if (!current) return;
    const result = await this.port.closeAllocation(current, reason);
    if (result.ok) this.allocation.set(result.value);
    else this.error.set(result.error);
  }
}
