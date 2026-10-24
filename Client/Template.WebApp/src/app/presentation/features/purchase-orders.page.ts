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
  imports: [TranslocoPipe, RouterLink, HlmButton, HlmInput, MoneyPipe, ProblemFeedback],
  template: `
    <header class="page-title">
      <p>
        {{ 'shell.breadcrumbHome' | transloco }} /
        {{ (receiving() ? 'nav.receiving' : 'nav.purchaseOrders') | transloco }}
      </p>
      <h1>
        {{ (receiving() ? 'purchasing.receivingQueue' : 'purchasing.ordersTitle') | transloco }}
      </h1>
    </header>
    <app-problem-feedback [error]="error()" (reload)="load()" />

    <section class="panel" aria-labelledby="orders-title">
      <h2 id="orders-title">
        {{ (receiving() ? 'purchasing.awaitingReceipt' : 'purchasing.queue') | transloco }}
      </h2>
      <form class="toolbar" (submit)="$event.preventDefault(); load()">
        @if (!receiving()) {
          <label for="orderStatus"
            >{{ 'fields.status' | transloco }}
            <select
              id="orderStatus"
              class="select"
              [value]="status()"
              (change)="changeStatus($any($event.target).value)"
            >
              @for (option of filters; track option) {
                <option [value]="option">
                  {{ 'purchasing.filters.' + (option || 'all') | transloco }}
                </option>
              }
            </select>
          </label>
        }
        <label for="orderSearch"
          >{{ 'common.search' | transloco }}
          <input
            id="orderSearch"
            hlmInput
            [value]="query()"
            (input)="query.set($any($event.target).value)"
          />
        </label>
        <button hlmBtn variant="outline" size="sm" type="submit">
          {{ 'common.search' | transloco }}
        </button>
      </form>
      @if (items().items.length === 0) {
        <p class="empty">{{ 'purchasing.empty' | transloco }}</p>
      } @else {
        <div
          class="table-scroll"
          role="region"
          tabindex="0"
          [attr.aria-label]="'purchasing.ordersTable' | transloco"
        >
          <table class="responsive-table">
            <thead>
              <tr>
                <th>{{ 'purchasing.number' | transloco }}</th>
                <th>{{ 'purchasing.requisition' | transloco }}</th>
                <th>{{ 'purchasing.supplier' | transloco }}</th>
                <th>{{ 'fields.status' | transloco }}</th>
                @if (!receiving()) {
                  <th>{{ 'purchasing.total' | transloco }}</th>
                  <th>{{ 'purchasing.document' | transloco }}</th>
                } @else {
                  <th>{{ 'purchasing.promisedBy' | transloco }}</th>
                }
                <th>{{ 'common.actions' | transloco }}</th>
              </tr>
            </thead>
            <tbody>
              @for (item of items().items; track item.id) {
                <tr [attr.data-order]="item.id">
                  <td [attr.data-label]="'purchasing.number' | transloco">
                    {{ item.number ?? ('purchasing.draftNumber' | transloco) }}
                  </td>
                  <td [attr.data-label]="'purchasing.requisition' | transloco">
                    {{ item.requisitionNumber }}
                  </td>
                  <td [attr.data-label]="'purchasing.supplier' | transloco">
                    {{ item.supplierName }}
                  </td>
                  <td [attr.data-label]="'fields.status' | transloco">
                    <span class="status-badge" [attr.data-status]="item.status">{{
                      'purchasing.statuses.' + item.status | transloco
                    }}</span>
                  </td>
                  @if (!receiving()) {
                    <td [attr.data-label]="'purchasing.total' | transloco">
                      {{ item.total | money }}
                    </td>
                    <td [attr.data-label]="'purchasing.document' | transloco">
                      {{ 'purchasing.documentStatuses.' + item.documentStatus | transloco }}
                      @if (
                        item.documentStatus === 'Failed' || item.notificationStatus === 'Failed'
                      ) {
                        <span class="warning">{{ 'purchasing.attention' | transloco }}</span>
                      }
                    </td>
                  } @else {
                    <td [attr.data-label]="'purchasing.promisedBy' | transloco">
                      {{ item.earliestPromisedDate ?? '—' }}
                    </td>
                  }
                  <td class="actions">
                    @if (receiving()) {
                      <a
                        hlmBtn
                        size="sm"
                        [routerLink]="['/receiving/orders', item.id, 'receipts', 'new']"
                        >{{ 'purchasing.receive' | transloco }}</a
                      >
                    }
                    <a
                      hlmBtn
                      size="sm"
                      variant="outline"
                      [routerLink]="['/purchasing/orders', item.id]"
                      >{{ 'purchasing.open' | transloco }}</a
                    >
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
