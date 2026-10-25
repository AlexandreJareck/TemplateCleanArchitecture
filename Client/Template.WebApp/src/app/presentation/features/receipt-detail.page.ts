import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import {
  PurchasingUseCases,
  REASON,
  type PurchaseOrderStatus,
  type Receipt,
} from '@application/purchasing/purchasing';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { ProblemFeedback } from '../shared/procurement-ui';
import { PURCHASING_PORT } from '../state/ports';
import { HlmNativeSelectImports } from '@presentation/shared-ui/native-select';
import { HlmTableImports } from '@presentation/shared-ui/table';
import { HlmTextareaImports } from '@presentation/shared-ui/textarea';

/**
 * Receipt detail (Phase 32, `/receiving/receipts/:id`): the posted quantities, discrepancies, corrections and the
 * pinned receiving policy. A posted receipt is never edited (RCV-006); `receiving.receipts.correct` holders append a
 * compensating signed delta with reason (RCV-007), and the recomputed order status is announced.
 */
@Component({
  selector: 'app-receipt-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ...HlmTextareaImports,
    ...HlmTableImports,
    ...HlmNativeSelectImports,
    TranslocoPipe,
    RouterLink,
    HlmButton,
    HlmInput,
    ProblemFeedback,
  ],
  templateUrl: './receipt-detail.page.html',
  styleUrl: './data-pages.css',
})
export class ReceiptDetailPage {
  private readonly purchasing = inject(PURCHASING_PORT);
  private readonly transloco = inject(TranslocoService);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly reasonMin = REASON.min;
  protected readonly reasonMax = REASON.max;

  readonly receipt = signal<Receipt | undefined>(undefined);
  /** The recomputed order state handed over by the receipt editor (PUR-009), then updated by corrections. */
  readonly orderStatus = signal<PurchaseOrderStatus | undefined>(
    inject(Router).getCurrentNavigation()?.extras.state?.['orderStatus'] as
      PurchaseOrderStatus | undefined,
  );
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly busy = signal(false);
  readonly announcement = signal('');
  readonly line = signal(1);
  readonly delta = signal('');
  readonly reason = signal('');
  readonly touched = signal(false);

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
    const result = await this.purchasing.receipt(this.id);
    if (!result.ok) return this.error.set(result.error);
    this.error.set(undefined);
    this.receipt.set(result.value);
    this.line.set(result.value.lines[0]?.poLineNumber ?? 1);
  }

  async correct() {
    const current = this.receipt();
    this.touched.set(true);
    if (!current || this.reasonError() || !/^-?\d+(\.\d+)?$/.test(this.delta().trim())) return;
    this.busy.set(true);
    const result = await this.purchasing.correct(current, this.line(), this.delta(), this.reason());
    this.busy.set(false);
    if (!result.ok) {
      this.error.set(result.error);
      if (PurchasingUseCases.isStale(result.error)) await this.load();
      return;
    }
    this.error.set(undefined);
    this.receipt.set(result.value.receipt);
    this.orderStatus.set(result.value.order.status);
    this.delta.set('');
    this.reason.set('');
    this.touched.set(false);
    this.announcement.set(
      this.transloco.translate('purchasing.announce.corrected', {
        status: this.transloco.translate(`purchasing.statuses.${result.value.order.status}`),
      }),
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
}
