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
import { HlmTableImports } from '@presentation/shared-ui/table';
import { HlmTextareaImports } from '@presentation/shared-ui/textarea';

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
  imports: [
    ...HlmTextareaImports,
    ...HlmTableImports,
    TranslocoPipe,
    RouterLink,
    HlmButton,
    HlmInput,
    MoneyPipe,
    ProblemFeedback,
  ],
  templateUrl: './purchase-order-detail.page.html',
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
