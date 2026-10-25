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
import { HlmNativeSelectImports } from '@presentation/shared-ui/native-select';
import { HlmTableImports } from '@presentation/shared-ui/table';

/** Allocations with server-computed reserved/committed/available totals (CALC-003) and creation (BUD-002). */
@Component({
  selector: 'app-budget-allocations-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ...HlmTableImports,
    ...HlmNativeSelectImports,
    FormField,
    RouterLink,
    TranslocoPipe,
    HlmButton,
    HlmInput,
    MoneyPipe,
    ProblemFeedback,
    StatusBadge,
  ],
  templateUrl: './budget-allocations.page.html',
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
