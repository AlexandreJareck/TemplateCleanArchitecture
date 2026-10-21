import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';
import type { ApplicationError } from '@application/common/application-error';
import type { Page } from '@application/master-data/master-data';
import type { FiscalPeriod } from '@application/procurement/procurement';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { ActionConfirmation, type ActionConfirmationRequest } from '../shared/action-confirmation';
import { ProblemFeedback, StatusBadge } from '../shared/procurement-ui';
import { SessionFacade } from '../state/session.facade';
import { BUDGET_PORT } from '../state/ports';

/** Fiscal periods (BUD-001, BUD-011): list for `budget.read`; create/open/close for `budget.periods.manage`. */
@Component({
  selector: 'app-budget-periods-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormField,
    TranslocoPipe,
    HlmButton,
    HlmInput,
    ActionConfirmation,
    ProblemFeedback,
    StatusBadge,
  ],
  template: `
    <header class="page-title">
      <p>{{ 'shell.breadcrumbHome' | transloco }} / {{ 'nav.budgetPeriods' | transloco }}</p>
      <h1>{{ 'nav.budgetPeriods' | transloco }}</h1>
    </header>
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
        [attr.aria-label]="'nav.budgetPeriods' | transloco"
      >
        <table>
          <thead>
            <tr>
              <th>{{ 'fields.code' | transloco }}</th>
              <th>{{ 'fields.name' | transloco }}</th>
              <th>{{ 'budget.range' | transloco }}</th>
              <th>{{ 'fields.status' | transloco }}</th>
              <th>{{ 'common.actions' | transloco }}</th>
            </tr>
          </thead>
          <tbody>
            @for (period of page().items; track period.id) {
              <tr>
                <td [attr.data-label]="'fields.code' | transloco">{{ period.code }}</td>
                <td [attr.data-label]="'fields.name' | transloco">{{ period.name }}</td>
                <td [attr.data-label]="'budget.range' | transloco">
                  {{ period.startDate }} – {{ period.endDate }}
                </td>
                <td [attr.data-label]="'fields.status' | transloco">
                  <app-status-badge [status]="period.status" />
                </td>
                <td>
                  @if (canManage && period.status === 'Planned') {
                    <button
                      hlmBtn
                      size="sm"
                      variant="outline"
                      type="button"
                      (click)="ask(period, 'open')"
                    >
                      {{ 'budget.open' | transloco }}
                    </button>
                  }
                  @if (canManage && period.status === 'Open') {
                    <button
                      hlmBtn
                      size="sm"
                      variant="outline"
                      type="button"
                      (click)="ask(period, 'close')"
                    >
                      {{ 'budget.close' | transloco }}
                    </button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
    @if (canManage) {
      <section class="panel" aria-labelledby="new-period-title">
        <h2 id="new-period-title">{{ 'budget.newPeriod' | transloco }}</h2>
        <form class="form-grid" (submit)="create($event)" novalidate>
          <label
            >{{ 'fields.code' | transloco }}<input hlmInput [formField]="periodForm.code"
          /></label>
          <label
            >{{ 'fields.name' | transloco }}<input hlmInput [formField]="periodForm.name"
          /></label>
          <label
            >{{ 'budget.startDate' | transloco
            }}<input hlmInput type="date" [formField]="periodForm.startDate"
          /></label>
          <label
            >{{ 'budget.endDate' | transloco
            }}<input hlmInput type="date" [formField]="periodForm.endDate"
          /></label>
          <div class="actions">
            <button hlmBtn type="submit" [disabled]="periodForm().invalid()">
              {{ 'common.create' | transloco }}
            </button>
          </div>
        </form>
      </section>
    }
    <app-action-confirmation
      [request]="confirmation()"
      (confirmed)="confirm()"
      (cancelled)="pending.set(undefined)"
    />
  `,
  styleUrl: './data-pages.css',
})
export class BudgetPeriodsPage {
  private readonly port = inject(BUDGET_PORT);
  private readonly transloco = inject(TranslocoService);
  readonly canManage = inject(SessionFacade).can('budget.periods.manage');
  readonly page = signal<Page<FiscalPeriod>>({
    items: [],
    page: 1,
    pageSize: 50,
    totalItems: 0,
    totalPages: 0,
  });
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly pending = signal<{ period: FiscalPeriod; transition: 'open' | 'close' } | undefined>(
    undefined,
  );
  readonly model = signal({ code: '', name: '', startDate: '', endDate: '' });
  readonly periodForm = form(this.model, (path) => {
    required(path.code);
    required(path.name);
    required(path.startDate);
    required(path.endDate);
  });
  readonly confirmation = computed<ActionConfirmationRequest | undefined>(() => {
    const pending = this.pending();
    if (!pending) return undefined;
    return {
      title: this.transloco.translate(
        pending.transition === 'open' ? 'budget.confirmOpen' : 'budget.confirmClose',
        {
          code: pending.period.code,
        },
      ),
      actionLabel: this.transloco.translate('common.confirm'),
      requireReason: false,
    };
  });

  constructor() {
    void this.load();
  }

  async load() {
    const result = await this.port.listPeriods({ page: 1, pageSize: 50 });
    if (result.ok) {
      this.page.set(result.value);
      this.error.set(undefined);
    } else this.error.set(result.error);
  }

  ask(period: FiscalPeriod, transition: 'open' | 'close') {
    this.pending.set({ period, transition });
  }

  async confirm() {
    const pending = this.pending();
    this.pending.set(undefined);
    if (!pending) return;
    const result = await this.port.changePeriodStatus(pending.period, pending.transition);
    if (result.ok) await this.load();
    else this.error.set(result.error);
  }

  async create(event: Event) {
    event.preventDefault();
    if (this.periodForm().invalid()) return;
    const result = await this.port.createPeriod(this.model());
    if (result.ok) {
      this.model.set({ code: '', name: '', startDate: '', endDate: '' });
      await this.load();
    } else this.error.set(result.error);
  }
}
