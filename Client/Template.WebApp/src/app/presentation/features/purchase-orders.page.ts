import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import type { Page } from '@application/master-data/master-data';
import type { PurchaseOrderSummary } from '@application/purchasing/purchasing';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { MoneyPipe, ProblemFeedback } from '../shared/procurement-ui';
import { PURCHASING_PORT } from '../state/ports';
import { HlmNativeSelectImports } from '@presentation/shared-ui/native-select';
import { HlmTableImports } from '@presentation/shared-ui/table';

const EMPTY: Page<PurchaseOrderSummary> = {
  items: [],
  page: 1,
  pageSize: 20,
  totalItems: 0,
  totalPages: 0,
};

const PURCHASING_FILTERS = [
  'Draft',
  'Issued,PartiallyReceived',
  'Completed',
  'Cancelled',
  '',
] as const;
const RECEIVING_FILTERS = ['Issued,PartiallyReceived'] as const;

/**
 * Purchase-order work queues (Phase 32). `/purchasing/orders` opens on the approved-order queue (Draft orders created
 * from approved awards) with the other states one filter away; `/receiving/queue` lists issued orders awaiting
 * receipt. Prices appear only when the server returns them (PUR-011); every action is decided on the detail screen
 * from the server's `allowedActions`.
 */
@Component({
  selector: 'app-purchase-orders-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ...HlmTableImports,
    ...HlmNativeSelectImports,
    TranslocoPipe,
    RouterLink,
    HlmButton,
    HlmInput,
    MoneyPipe,
    ProblemFeedback,
  ],
  templateUrl: './purchase-orders.page.html',
  styleUrl: './data-pages.css',
})
export class PurchaseOrdersPage {
  private readonly purchasing = inject(PURCHASING_PORT);

  /** Route data `receiving: true` selects the receiving-queue variant. */
  readonly receiving = signal(inject(ActivatedRoute).snapshot.data['receiving'] === true);
  readonly filters = PURCHASING_FILTERS;
  readonly items = signal<Page<PurchaseOrderSummary>>(EMPTY);
  readonly status = signal<string>('Draft');
  readonly query = signal('');
  readonly error = signal<ApplicationError | undefined>(undefined);

  constructor() {
    void this.load();
  }

  async load() {
    const status = this.receiving() ? RECEIVING_FILTERS[0] : this.status();
    const result = await this.purchasing.list({ page: 1, pageSize: 50, status, q: this.query() });
    if (!result.ok) return this.error.set(result.error);
    this.error.set(undefined);
    this.items.set(result.value);
  }

  changeStatus(value: string) {
    this.status.set(value);
    void this.load();
  }
}
