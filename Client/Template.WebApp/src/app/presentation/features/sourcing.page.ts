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
import { HlmNativeSelectImports } from '@presentation/shared-ui/native-select';
import { HlmTableImports } from '@presentation/shared-ui/table';

const RFQ_STATUSES = ['', 'Draft', 'Published', 'Closed', 'AwardPending', 'Cancelled'] as const;

const EMPTY = { items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 };

/**
 * Buyer work queue (Phase 29): submitted requisitions waiting for a sourcing round, with a start action per sourcing
 * method, and the RFQ list with status filter. Eligibility (SRC-001) and every rule are decided by the API.
 */
@Component({
  selector: 'app-sourcing-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ...HlmTableImports,
    ...HlmNativeSelectImports,
    RouterLink,
    TranslocoPipe,
    HlmButton,
    MoneyPipe,
    ProblemFeedback,
    StatusBadge,
  ],
  templateUrl: './sourcing.page.html',
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
