import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import type { Result } from '@application/common/result';
import {
  PurchasingUseCases,
  REASON,
  type PurchaseOrder,
  type PurchaseOrderAction,
  type PurchaseOrderHistoryEntry,
  type Receipt,
} from '@application/purchasing/purchasing';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { MoneyPipe, ProblemFeedback } from '../shared/procurement-ui';
import { PURCHASING_PORT } from '../state/ports';

type Confirmation = 'issue' | 'cancel' | 'short-close';

/**
 * Purchase-order detail (Phase 32, `/purchasing/orders/:id`): the immutable commercial snapshot, lines with promised
 * dates and received/accepted/remaining quantities, the document and supplier-notification sub-statuses with retry,
 * the draft-only delivery fields, the issue/cancel/short-close confirmations and the timeline with receipts. Every
 * action is offered only when the server lists it; a stale version reloads the current state instead of retrying.
 */
@Component({
  selector: 'app-purchase-order-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, RouterLink, HlmButton, HlmInput, MoneyPipe, ProblemFeedback],
  template: `
    <header class="page-title">
      <p>
        {{ 'shell.breadcrumbHome' | transloco }} /
        <a routerLink="/purchasing/orders">{{ 'nav.purchaseOrders' | transloco }}</a>
      </p>
      <h1>
        {{
          'purchasing.title'
            | transloco: { number: order()?.number ?? ('purchasing.draftNumber' | transloco) }
        }}
      </h1>
    </header>
    <p class="sr-only" aria-live="polite" role="status">{{ announcement() }}</p>
    @if (stale()) {
      <div class="feedback" role="alert">
        <strong>{{ 'purchasing.stale' | transloco }}</strong>
      </div>
    }
    @if (problemKey(); as key) {
      <div class="feedback" role="alert">
        <strong>{{ key | transloco }}</strong>
        <span>{{ error()?.code }}</span>
        @if (error()?.fieldErrors; as fields) {
          @for (entry of fieldList(fields); track entry) {
            <span>{{ entry }}</span>
          }
        }
      </div>
    } @else {
      <app-problem-feedback [error]="error()" (reload)="load()" />
    }

    @if (order(); as current) {
      <section class="panel" aria-labelledby="order-summary">
        <h2 id="order-summary">{{ 'purchasing.summary' | transloco }}</h2>
        <dl class="summary-grid">
          <dt>{{ 'fields.status' | transloco }}</dt>
          <dd>
            <span class="status-badge" [attr.data-status]="current.status">{{
              'purchasing.statuses.' + current.status | transloco
            }}</span>
            @if (current.shortClosed) {
              <span class="notice">{{ 'purchasing.shortClosedBadge' | transloco }}</span>
            }
          </dd>
          <dt>{{ 'purchasing.requisition' | transloco }}</dt>
          <dd>
            <a [routerLink]="['/requisitions', current.requisitionId]">{{
              current.requisitionNumber
            }}</a>
          </dd>
          <dt>{{ 'purchasing.rfq' | transloco }}</dt>
          <dd>{{ current.rfqNumber }}</dd>
          <dt>{{ 'purchasing.supplier' | transloco }}</dt>
          <dd>
            <strong>{{ current.supplier.legalName }}</strong> · {{ current.supplier.taxId }}
            @if (current.supplier.address) {
              <br />{{ current.supplier.address }}
            }
            @if (current.supplier.contactName) {
              <br />{{ current.supplier.contactName }} · {{ current.supplier.contactEmail ?? '' }}
            }
          </dd>
          @if (current.buyerName) {
            <dt>{{ 'purchasing.buyer' | transloco }}</dt>
            <dd>{{ current.buyerName }}</dd>
          }
          <dt>{{ 'purchasing.paymentTerms' | transloco }}</dt>
          <dd>{{ current.paymentTerms }}</dd>
          @if (current.pricesVisible) {
            <dt>{{ 'purchasing.total' | transloco }}</dt>
            <dd>
              <strong>{{ current.total | money }}</strong>
              ({{ 'purchasing.freight' | transloco }} {{ current.freight | money }} ·
              {{ 'purchasing.discount' | transloco }} {{ current.discount | money }} ·
              {{ 'purchasing.taxesIncluded' | transloco }} {{ current.taxesIncluded | money }})
            </dd>
          }
          <dt>{{ 'purchasing.deliveryAddress' | transloco }}</dt>
          <dd>{{ current.deliveryAddress ?? '—' }}</dd>
          @if (current.issueDate) {
            <dt>{{ 'purchasing.issuedOn' | transloco }}</dt>
            <dd>{{ current.issueDate }}</dd>
          }
          <dt>{{ 'purchasing.receivingPolicy' | transloco }}</dt>
          <dd>{{ current.receivingPolicy }}</dd>
          @if (current.cancellationReason) {
            <dt>{{ 'purchasing.cancellationReason' | transloco }}</dt>
            <dd>“{{ current.cancellationReason }}”</dd>
          }
        </dl>
      </section>

      @if (current.status !== 'Draft') {
        <section class="panel" aria-labelledby="order-dispatch">
          <h2 id="order-dispatch">{{ 'purchasing.dispatch' | transloco }}</h2>
          <dl class="summary-grid">
            <dt>{{ 'purchasing.document' | transloco }}</dt>
            <dd [attr.data-document]="current.documentStatus">
              {{ 'purchasing.documentStatuses.' + current.documentStatus | transloco }}
              @if (current.documentFailureCode) {
                <span class="warning">{{ current.documentFailureCode }}</span>
              }
              @if (current.documentTemplateVersion) {
                · {{ current.documentTemplateVersion }}
              }
              · {{ 'purchasing.attempts' | transloco: { count: current.documentAttempts } }}
            </dd>
            <dt>{{ 'purchasing.notification' | transloco }}</dt>
            <dd [attr.data-notification]="current.notificationStatus">
              {{ 'purchasing.notificationStatuses.' + current.notificationStatus | transloco }}
              @if (current.notificationFailureCode) {
                <span class="warning">{{ current.notificationFailureCode }}</span>
              }
              · {{ 'purchasing.attempts' | transloco: { count: current.notificationAttempts } }}
            </dd>
          </dl>
          <div class="actions">
            @if (can('download-document')) {
              <button hlmBtn variant="outline" type="button" (click)="download()">
                {{ 'purchasing.downloadDocument' | transloco }}
              </button>
            }
            @if (can('retry-document')) {
              <button
                hlmBtn
                variant="outline"
                type="button"
                [disabled]="busy()"
                (click)="retryDocument()"
              >
                {{ 'purchasing.retryDocument' | transloco }}
              </button>
            }
            @if (can('retry-notification')) {
              <button
                hlmBtn
                variant="outline"
                type="button"
                [disabled]="busy()"
                (click)="retryNotification()"
              >
                {{ 'purchasing.retryNotification' | transloco }}
              </button>
            }
          </div>
        </section>
      }

      <section class="panel" aria-labelledby="order-lines">
        <h2 id="order-lines">{{ 'purchasing.lines' | transloco }}</h2>
        <div
          class="table-scroll"
          role="region"
          tabindex="0"
          [attr.aria-label]="'purchasing.linesTable' | transloco"
        >
          <table class="responsive-table">
            <thead>
              <tr>
                <th>#</th>
                <th>{{ 'purchasing.description' | transloco }}</th>
                <th>{{ 'purchasing.ordered' | transloco }}</th>
                @if (current.pricesVisible) {
                  <th>{{ 'purchasing.unitPrice' | transloco }}</th>
                  <th>{{ 'purchasing.lineTotal' | transloco }}</th>
                }
                <th>{{ 'purchasing.accepted' | transloco }}</th>
                <th>{{ 'purchasing.remaining' | transloco }}</th>
                <th>{{ 'purchasing.promisedDate' | transloco }}</th>
              </tr>
            </thead>
            <tbody>
              @for (line of current.lines; track line.lineNumber) {
                <tr [attr.data-line]="line.lineNumber">
                  <td data-label="#">{{ line.lineNumber }}</td>
                  <td [attr.data-label]="'purchasing.description' | transloco">
                    {{ line.description }}
                    @if (line.shortClosed) {
                      <span class="notice">{{ 'purchasing.shortClosedLine' | transloco }}</span>
                    }
                  </td>
                  <td [attr.data-label]="'purchasing.ordered' | transloco">
                    {{ line.quantity }} {{ line.unitCode }}
                  </td>
                  @if (current.pricesVisible) {
                    <td [attr.data-label]="'purchasing.unitPrice' | transloco">
                      {{ line.unitPrice | money }}
                    </td>
                    <td [attr.data-label]="'purchasing.lineTotal' | transloco">
                      {{ line.lineTotal | money }}
                    </td>
                  }
                  <td [attr.data-label]="'purchasing.accepted' | transloco">
                    {{ line.acceptedQuantity }}
                  </td>
                  <td [attr.data-label]="'purchasing.remaining' | transloco">
                    {{ line.remainingQuantity }}
                  </td>
                  <td [attr.data-label]="'purchasing.promisedDate' | transloco">
                    @if (can('update')) {
                      <label class="sr-only" [attr.for]="'promised-' + line.lineNumber"
                        >{{ 'purchasing.promisedDate' | transloco }} {{ line.lineNumber }}</label
                      >
                      <input
                        hlmInput
                        type="date"
                        [id]="'promised-' + line.lineNumber"
                        [value]="promised()[line.lineNumber] ?? line.promisedDeliveryDate"
                        (change)="setPromised(line.lineNumber, $any($event.target).value)"
                      />
                    } @else {
                      {{ line.promisedDeliveryDate }}
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      @if (can('update')) {
        <section class="panel" aria-labelledby="order-draft">
          <h2 id="order-draft">{{ 'purchasing.draftFields' | transloco }}</h2>
          <form class="form-grid" (submit)="$event.preventDefault(); saveDraft()" novalidate>
            <label for="deliveryAddress"
              >{{ 'purchasing.deliveryAddress' | transloco }}
              <textarea
                id="deliveryAddress"
                class="textarea"
                rows="2"
                maxlength="500"
                [value]="address()"
                (input)="address.set($any($event.target).value)"
              ></textarea>
            </label>
            <label for="orderNotes"
              >{{ 'purchasing.notes' | transloco }}
              <textarea
                id="orderNotes"
                class="textarea"
                rows="2"
                maxlength="2000"
                [value]="notes()"
                (input)="notes.set($any($event.target).value)"
              ></textarea>
            </label>
            <p class="hint">{{ 'purchasing.draftHint' | transloco }}</p>
            <div class="actions">
              <button hlmBtn variant="outline" type="submit" [disabled]="busy()">
                {{ 'purchasing.saveDraft' | transloco }}
              </button>
            </div>
          </form>
        </section>
      }

      @if (can('issue') || can('cancel') || can('short-close') || can('receive')) {
        <section class="panel" aria-labelledby="order-actions">
          <h2 id="order-actions">{{ 'common.actions' | transloco }}</h2>
          <div class="actions">
            @if (can('issue')) {
              <button hlmBtn type="button" (click)="open('issue')">
                {{ 'purchasing.issue' | transloco }}
              </button>
            }
            @if (can('receive')) {
              <a
                hlmBtn
                variant="outline"
                [routerLink]="['/receiving/orders', current.id, 'receipts', 'new']"
                >{{ 'purchasing.receive' | transloco }}</a
              >
            }
            @if (can('short-close')) {
              <button hlmBtn variant="outline" type="button" (click)="open('short-close')">
                {{ 'purchasing.shortClose' | transloco }}
              </button>
            }
            @if (can('cancel')) {
              <button hlmBtn variant="destructive" type="button" (click)="open('cancel')">
                {{ 'purchasing.cancel' | transloco }}
              </button>
            }
          </div>
          @if (confirming(); as active) {
            <form
              class="form-grid"
              (submit)="$event.preventDefault(); confirm()"
              novalidate
              [attr.aria-label]="'purchasing.confirm.' + active | transloco"
            >
              <p class="notice">{{ 'purchasing.confirmText.' + active | transloco }}</p>
              @if (active !== 'issue') {
                <label for="orderReason"
                  >{{ 'common.reason' | transloco }}
                  <textarea
                    id="orderReason"
                    class="textarea"
                    rows="3"
                    [attr.maxlength]="reasonMax"
                    [attr.aria-invalid]="showReasonError() ? 'true' : null"
                    [attr.aria-describedby]="
                      showReasonError() ? 'orderReasonError' : 'orderReasonHint'
                    "
                    [value]="reason()"
                    (input)="reason.set($any($event.target).value)"
                  ></textarea>
                </label>
                @if (showReasonError(); as problem) {
                  <p id="orderReasonError" class="warning">
                    {{
                      'purchasing.reasonErrors.' + problem
                        | transloco: { min: reasonMin, max: reasonMax }
                    }}
                  </p>
                } @else {
                  <p id="orderReasonHint" class="hint">
                    {{ 'purchasing.reasonHint' | transloco: { min: reasonMin, max: reasonMax } }}
                  </p>
                }
              }
              <div class="actions">
                <button hlmBtn variant="outline" type="button" (click)="confirming.set(undefined)">
                  {{ 'common.cancel' | transloco }}
                </button>
                <button hlmBtn type="submit" [disabled]="busy()">
                  {{ 'purchasing.confirm.' + active | transloco }}
                </button>
              </div>
            </form>
          }
        </section>
      }

      @if (receipts().length > 0) {
        <section class="panel" aria-labelledby="order-receipts">
          <h2 id="order-receipts">{{ 'purchasing.receipts' | transloco }}</h2>
          <ul class="timeline">
            @for (receipt of receipts(); track receipt.id) {
              <li [attr.data-receipt]="receipt.number">
                <a [routerLink]="['/receiving/receipts', receipt.id]">{{ receipt.number }}</a> ·
                {{ receipt.receivedOn }}
                @if (receipt.hasDiscrepancy) {
                  <span class="warning">{{ 'purchasing.discrepancy' | transloco }}</span>
                }
                @if (receipt.corrections.length > 0) {
                  ·
                  {{
                    'purchasing.correctionsCount' | transloco: { count: receipt.corrections.length }
                  }}
                }
              </li>
            }
          </ul>
        </section>
      }

      <section class="panel" aria-labelledby="order-history">
        <h2 id="order-history">{{ 'requisition.timeline' | transloco }}</h2>
        <ol class="timeline">
          @for (entry of history(); track entry.sequence) {
            <li>
              <strong>{{ 'purchasing.history.' + entry.action | transloco }}</strong>
              @if (entry.reason) {
                · “{{ entry.reason }}”
              } @else if (entry.reasonCode) {
                · {{ entry.reasonCode }}
              }
              <br /><small>{{ instant(entry.at) }}</small>
            </li>
          }
        </ol>
      </section>
    }
  `,
  styleUrl: './data-pages.css',
})
export class PurchaseOrderDetailPage {
  private readonly purchasing = inject(PURCHASING_PORT);
  private readonly transloco = inject(TranslocoService);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly reasonMin = REASON.min;
  protected readonly reasonMax = REASON.max;

  readonly order = signal<PurchaseOrder | undefined>(undefined);
  readonly history = signal<readonly PurchaseOrderHistoryEntry[]>([]);
  readonly receipts = signal<readonly Receipt[]>([]);
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly stale = signal(false);
  readonly busy = signal(false);
  readonly announcement = signal('');
  readonly confirming = signal<Confirmation | undefined>(undefined);
  readonly reason = signal('');
  readonly touched = signal(false);
  readonly address = signal('');
  readonly notes = signal('');
  readonly promised = signal<Record<number, string>>({});

  readonly reasonError = computed(() => PurchasingUseCases.reasonError(this.reason()));
  readonly showReasonError = computed(() => (this.touched() ? this.reasonError() : undefined));
  readonly problemKey = computed(() => {
    const error = this.error();
    return error ? PurchasingUseCases.problemKey(error) : undefined;
  });

  constructor() {
    void this.load();
  }

  async load() {
    const [order, history] = await Promise.all([
      this.purchasing.get(this.id),
      this.purchasing.history(this.id),
    ]);
    if (!order.ok) return this.error.set(order.error);
    this.error.set(undefined);
    this.show(order.value);
    if (history.ok) this.history.set(history.value);
    if (order.value.status !== 'Draft' && order.value.status !== 'Cancelled') {
      const receipts = await this.purchasing.receipts(this.id);
      if (receipts.ok) this.receipts.set(receipts.value.items);
    }
  }

  can(action: PurchaseOrderAction) {
    return PurchasingUseCases.can(this.order(), action);
  }

  setPromised(lineNumber: number, date: string) {
    this.promised.update((current) => ({ ...current, [lineNumber]: date }));
  }

  open(mode: Confirmation) {
    this.confirming.set(mode);
    this.reason.set('');
    this.touched.set(false);
    this.stale.set(false);
  }

  async saveDraft() {
    const current = this.order();
    if (!current) return;
    const promisedDates = Object.entries(this.promised()).map(([lineNumber, date]) => ({
      lineNumber: Number(lineNumber),
      date,
    }));
    await this.run(
      () =>
        this.purchasing.update(current, {
          deliveryAddress: this.address().trim() || undefined,
          notes: this.notes(),
          promisedDates: promisedDates.length > 0 ? promisedDates : undefined,
        }),
      'purchasing.announce.saved',
    );
  }

  async confirm() {
    const current = this.order();
    const mode = this.confirming();
    this.touched.set(true);
    if (!current || !mode) return;
    if (mode !== 'issue' && this.reasonError()) return;
    const reason = this.reason();
    const ok = await this.run(
      () =>
        mode === 'issue'
          ? this.purchasing.issue(current)
          : mode === 'cancel'
            ? this.purchasing.cancel(current, reason)
            : this.purchasing.shortClose(current, reason),
      `purchasing.announce.${mode}`,
    );
    if (ok) this.confirming.set(undefined);
  }

  retryDocument() {
    const current = this.order();
    if (current)
      void this.run(() => this.purchasing.retryDocument(current), 'purchasing.announce.retried');
  }

  retryNotification() {
    const current = this.order();
    if (current)
      void this.run(
        () => this.purchasing.retryNotification(current),
        'purchasing.announce.retried',
      );
  }

  async download() {
    const current = this.order();
    if (!current) return;
    const result = await this.purchasing.downloadDocument(current);
    if (!result.ok) this.error.set(result.error);
  }

  fieldList(fields: Readonly<Record<string, readonly string[]>>) {
    return Object.entries(fields).flatMap(([field, messages]) =>
      messages.map((message) => `${field}: ${message}`),
    );
  }

  instant(value: string | undefined) {
    if (!value) return '—';
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? value
      : new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
          dateStyle: 'short',
          timeStyle: 'short',
        }).format(date);
  }

  private show(order: PurchaseOrder) {
    this.order.set(order);
    this.address.set(order.deliveryAddress ?? '');
    this.notes.set(order.notes ?? '');
    this.promised.set({});
  }

  private async run(
    action: () => Promise<Result<PurchaseOrder>>,
    announce: string,
  ): Promise<boolean> {
    this.busy.set(true);
    const result = await action();
    this.busy.set(false);
    if (!result.ok) {
      this.error.set(result.error);
      if (PurchasingUseCases.isStale(result.error)) {
        this.stale.set(true);
        this.confirming.set(undefined);
        await this.load();
        this.error.set(result.error);
      }
      return false;
    }
    this.error.set(undefined);
    this.stale.set(false);
    this.show(result.value);
    const history = await this.purchasing.history(this.id);
    if (history.ok) this.history.set(history.value);
    this.announcement.set(
      this.transloco.translate(announce, {
        status: this.transloco.translate(`purchasing.statuses.${result.value.status}`),
        number: result.value.number ?? '',
      }),
    );
    return true;
  }
}
