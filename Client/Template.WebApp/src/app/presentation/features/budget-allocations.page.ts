import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import type { MasterDataItem, Page } from '@application/master-data/master-data';
import type { BudgetAllocation, FiscalPeriod } from '@application/procurement/procurement';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { MoneyPipe, ProblemFeedback, StatusBadge } from '../shared/procurement-ui';
import { SessionFacade } from '../state/session.facade';
import { BUDGET_PORT, MASTER_DATA_PORT } from '../state/ports';

/** Allocations with server-computed reserved/committed/available totals (CALC-003) and creation (BUD-002). */
@Component({
  selector: 'app-budget-allocations-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormField,
    RouterLink,
    TranslocoPipe,
    HlmButton,
    HlmInput,
    MoneyPipe,
    ProblemFeedback,
    StatusBadge,
  ],
  template: `
    <header class="page-title">
      <p>{{ 'shell.breadcrumbHome' | transloco }} / {{ 'nav.budgetAllocations' | transloco }}</p>
      <h1>{{ 'nav.budgetAllocations' | transloco }}</h1>
    </header>
    <div class="toolbar">
      <label for="periodFilter">{{ 'budget.period' | transloco }}</label>
      <select
        id="periodFilter"
        class="select"
        [value]="periodFilter()"
        (change)="filter($any($event.target).value)"
      >
        <option value="">{{ 'budget.allPeriods' | transloco }}</option>
        @for (period of periods(); track period.id) {
          <option [value]="period.id">{{ period.code }}</option>
        }
      </select>
    </div>
    <app-problem-feedback [error]="error()" (reload)="load()" />
    @if (page().items.length === 0) {
      <div class="empty">
        <h2>{{ 'common.empty' | transloco }}</h2>
      </div>
    } @else {
      <div
        class="table-scroll"
        role="region"
        tabindex="0"
        [attr.aria-label]="'nav.budgetAllocations' | transloco"
      >
        <table>
          <thead>
            <tr>
              <th>{{ 'budget.period' | transloco }}</th>
              <th>{{ 'requisition.costCenter' | transloco }}</th>
              <th>{{ 'budget.amount' | transloco }}</th>
              <th>{{ 'budget.reserved' | transloco }}</th>
              <th>{{ 'budget.available' | transloco }}</th>
              <th>{{ 'fields.status' | transloco }}</th>
              <th>{{ 'common.actions' | transloco }}</th>
            </tr>
          </thead>
          <tbody>
            @for (allocation of page().items; track allocation.id) {
              <tr>
                <td [attr.data-label]="'budget.period' | transloco">
                  {{ periodCode(allocation.fiscalPeriodId) }}
                </td>
                <td [attr.data-label]="'requisition.costCenter' | transloco">
                  {{ costCenterLabel(allocation.costCenterId) }}
                </td>
                <td [attr.data-label]="'budget.amount' | transloco">
                  {{ allocation.amount | money }}
                </td>
                <td [attr.data-label]="'budget.reserved' | transloco">
                  {{ allocation.reserved | money }}
                </td>
                <td [attr.data-label]="'budget.available' | transloco">
                  {{ allocation.available | money }}
                </td>
                <td [attr.data-label]="'fields.status' | transloco">
                  <app-status-badge [status]="allocation.status" />
                </td>
                <td>
                  <a
                    hlmBtn
                    size="sm"
                    variant="outline"
                    [routerLink]="['/budget/allocations', allocation.id]"
                    >{{ 'common.details' | transloco }}</a
                  >
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
    @if (canManage) {
      <section class="panel" aria-labelledby="new-allocation-title">
        <h2 id="new-allocation-title">{{ 'budget.newAllocation' | transloco }}</h2>
        <form class="form-grid" (submit)="create($event)" novalidate>
          <label>
            {{ 'budget.period' | transloco }}
            <select class="select" [formField]="allocationForm.fiscalPeriodId">
              <option value="">—</option>
              @for (period of periods(); track period.id) {
                @if (period.status !== 'Closed') {
                  <option [value]="period.id">{{ period.code }}</option>
                }
              }
            </select>
          </label>
          <label>
            {{ 'requisition.costCenter' | transloco }}
            <select class="select" [formField]="allocationForm.costCenterId">
              <option value="">—</option>
              @for (costCenter of costCenters(); track costCenter.id) {
                <option [value]="costCenter.id">
                  {{ costCenter.code }} — {{ costCenter.name }}
                </option>
              }
            </select>
          </label>
          <label>
            {{ 'requisition.currency' | transloco }}
            <select class="select" [formField]="allocationForm.currency">
              <option value="BRL">BRL</option>
              <option value="USD">USD</option>
              <option value="EUR">EUR</option>
            </select>
          </label>
          <label
            >{{ 'budget.amount' | transloco
            }}<input hlmInput inputmode="decimal" [formField]="allocationForm.amount"
          /></label>
          <div class="actions">
            <button hlmBtn type="submit" [disabled]="allocationForm().invalid()">
              {{ 'common.create' | transloco }}
            </button>
          </div>
        </form>
      </section>
    }
  `,
  styleUrl: './data-pages.css',
})
export class BudgetAllocationsPage {
  private readonly port = inject(BUDGET_PORT);
  private readonly masterData = inject(MASTER_DATA_PORT);
  readonly canManage = inject(SessionFacade).can('budget.allocations.manage');
  readonly page = signal<Page<BudgetAllocation>>({
    items: [],
    page: 1,
    pageSize: 100,
    totalItems: 0,
    totalPages: 0,
  });
  readonly periods = signal<readonly FiscalPeriod[]>([]);
  readonly costCenters = signal<readonly MasterDataItem[]>([]);
  readonly periodFilter = signal('');
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly model = signal({ fiscalPeriodId: '', costCenterId: '', currency: 'BRL', amount: '' });
  readonly allocationForm = form(this.model, (path) => {
    required(path.fiscalPeriodId);
    required(path.costCenterId);
    required(path.amount);
  });

  constructor() {
    void this.loadLookups();
    void this.load();
  }

  async load() {
    const result = await this.port.listAllocations({
      page: 1,
      pageSize: 100,
      fiscalPeriodId: this.periodFilter(),
    });
    if (result.ok) {
      this.page.set(result.value);
      this.error.set(undefined);
    } else this.error.set(result.error);
  }

  filter(periodId: string) {
    this.periodFilter.set(periodId);
    void this.load();
  }

  periodCode(id: string) {
    return this.periods().find((period) => period.id === id)?.code ?? id;
  }

  costCenterLabel(id: string) {
    const costCenter = this.costCenters().find((candidate) => candidate.id === id);
    return costCenter ? `${costCenter.code} — ${costCenter.name}` : id;
  }

  async create(event: Event) {
    event.preventDefault();
    if (this.allocationForm().invalid()) return;
    const result = await this.port.createAllocation(this.model());
    if (result.ok) {
      this.model.set({ fiscalPeriodId: '', costCenterId: '', currency: 'BRL', amount: '' });
      await this.load();
    } else this.error.set(result.error);
  }

  private async loadLookups() {
    const [periods, costCenters] = await Promise.all([
      this.port.listPeriods({ page: 1, pageSize: 100 }),
      this.masterData.list('cost-centers', { page: 1, pageSize: 100, sort: 'code' }),
    ]);
    if (periods.ok) this.periods.set(periods.value.items);
    if (costCenters.ok) this.costCenters.set(costCenters.value.items);
  }
}
