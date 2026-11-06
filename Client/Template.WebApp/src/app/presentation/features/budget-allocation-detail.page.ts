import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { form, FormField, pattern, required } from '@angular/forms/signals';
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
import { HlmTableImports } from '@presentation/shared-ui/table';
import { InstantPipe } from '../shared/display';

/** Allocation detail: totals, amount history (BUD-003), reservation visibility and reasoned amount change / close. */
/** Amounts are typed as plain decimals with a dot and up to two decimals, the format the API accepts. */
const DECIMAL_INPUT = /^\d{1,15}(\.\d{1,2})?$/;

@Component({
  selector: 'app-budget-allocation-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    InstantPipe,
    ...HlmTableImports,
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
  templateUrl: './budget-allocation-detail.page.html',
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
    pattern(path.amount, DECIMAL_INPUT);
  });
  /** Localized reading of the typed amount, so `1800000.00` is confirmed as `R$ 1.800.000,00` before saving. */
  readonly amountPreview = computed(() => {
    const amount = this.model().amount.trim();
    const currency = this.allocation()?.amount.currency;
    return DECIMAL_INPUT.test(amount) && currency ? { amount, currency } : undefined;
  });
  readonly amountInvalid = computed(() => {
    const state = this.amountForm.amount();
    return (state.touched() || state.dirty()) && state.errors().some((e) => e.kind === 'pattern');
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
