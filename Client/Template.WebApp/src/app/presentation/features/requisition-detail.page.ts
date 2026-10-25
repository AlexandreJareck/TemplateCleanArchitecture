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
import { HlmTableImports } from '@presentation/shared-ui/table';

type PendingAction = 'withdraw' | 'cancel' | undefined;

/**
 * Requisition detail (REQ-011 readable): lines, totals, submission snapshots, the state and reservation timeline
 * (NFR-AUD-2), the closing reason of cancelled/rejected/expired requisitions and the actions the server allows.
 */
@Component({
  selector: 'app-requisition-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ...HlmTableImports,
    RouterLink,
    TranslocoPipe,
    HlmButton,
    ActionConfirmation,
    MoneyPipe,
    ProblemFeedback,
    StatusBadge,
  ],
  templateUrl: './requisition-detail.page.html',
  styleUrl: './data-pages.css',
})
export class RequisitionDetailPage {
  private readonly port = inject(REQUISITION_PORT);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  protected readonly session = inject(SessionFacade);
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
