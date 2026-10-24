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

/**
 * Receipt detail (Phase 32, `/receiving/receipts/:id`): the posted quantities, discrepancies, corrections and the
 * pinned receiving policy. A posted receipt is never edited (RCV-006); `receiving.receipts.correct` holders append a
 * compensating signed delta with reason (RCV-007), and the recomputed order status is announced.
 */
@Component({
  selector: 'app-receipt-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, RouterLink, HlmButton, HlmInput, ProblemFeedback],
  template: `
    <header class="page-title">
      <p>
        {{ 'shell.breadcrumbHome' | transloco }} /
        <a routerLink="/receiving/queue">{{ 'nav.receiving' | transloco }}</a>
      </p>
      <h1>{{ 'purchasing.receiptTitle' | transloco: { number: receipt()?.number ?? '' } }}</h1>
    </header>
    <p class="sr-only" aria-live="polite" role="status">{{ announcement() }}</p>
    @if (problemKey(); as key) {
      <div class="feedback" role="alert">
        <strong>{{ key | transloco }}</strong>
        <span>{{ error()?.code }}</span>
      </div>
    } @else {
      <app-problem-feedback [error]="error()" (reload)="load()" />
    }

    @if (receipt(); as current) {
      <section class="panel" aria-labelledby="receipt-summary">
        <h2 id="receipt-summary">{{ 'purchasing.summary' | transloco }}</h2>
        <dl class="summary-grid">
          <dt>{{ 'purchasing.order' | transloco }}</dt>
          <dd>
            <a [routerLink]="['/purchasing/orders', current.purchaseOrderId]">{{
              current.purchaseOrderNumber
            }}</a>
          </dd>
          <dt>{{ 'purchasing.receivedOn' | transloco }}</dt>
          <dd>{{ current.receivedOn }}</dd>
          <dt>{{ 'purchasing.location' | transloco }}</dt>
          <dd>{{ current.location ?? '—' }}</dd>
          <dt>{{ 'purchasing.deliveryReference' | transloco }}</dt>
          <dd>{{ current.deliveryReference ?? '—' }}</dd>
          <dt>{{ 'purchasing.receivingPolicy' | transloco }}</dt>
          <dd>{{ current.policy }}</dd>
          @if (orderStatus(); as status) {
            <dt>{{ 'purchasing.orderStatus' | transloco }}</dt>
            <dd>
              <span class="status-badge" [attr.data-status]="status">{{
                'purchasing.statuses.' + status | transloco
              }}</span>
            </dd>
          }
        </dl>
      </section>

      <section class="panel" aria-labelledby="receipt-lines">
        <h2 id="receipt-lines">{{ 'purchasing.receiptLines' | transloco }}</h2>
        <div
          class="table-scroll"
          role="region"
          tabindex="0"
          [attr.aria-label]="'purchasing.receiptLinesTable' | transloco"
        >
          <table class="responsive-table">
            <thead>
              <tr>
                <th>#</th>
                <th>{{ 'purchasing.description' | transloco }}</th>
                <th>{{ 'purchasing.received' | transloco }}</th>
                <th>{{ 'purchasing.accepted' | transloco }}</th>
                <th>{{ 'purchasing.rejected' | transloco }}</th>
                <th>{{ 'purchasing.correctedAccepted' | transloco }}</th>
                <th>{{ 'purchasing.discrepancy' | transloco }}</th>
              </tr>
            </thead>
            <tbody>
              @for (line of current.lines; track line.poLineNumber) {
                <tr [attr.data-line]="line.poLineNumber">
                  <td data-label="#">{{ line.poLineNumber }}</td>
                  <td [attr.data-label]="'purchasing.description' | transloco">
                    {{ line.description }} ({{ line.unitCode }})
                  </td>
                  <td [attr.data-label]="'purchasing.received' | transloco">
                    {{ line.receivedQuantity }}
                  </td>
                  <td [attr.data-label]="'purchasing.accepted' | transloco">
                    {{ line.acceptedQuantity }}
                  </td>
                  <td [attr.data-label]="'purchasing.rejected' | transloco">
                    {{ line.rejectedQuantity }}
                  </td>
                  <td [attr.data-label]="'purchasing.correctedAccepted' | transloco">
                    {{ line.correctedAcceptedQuantity }}
                  </td>
                  <td [attr.data-label]="'purchasing.discrepancy' | transloco">
                    @if (line.discrepancyType) {
                      <span class="warning">{{
                        'purchasing.discrepancyTypes.' + line.discrepancyType | transloco
                      }}</span>
                      · {{ line.discrepancyNotes }}
                    } @else {
                      —
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      @if (current.corrections.length > 0) {
        <section class="panel" aria-labelledby="receipt-corrections">
          <h2 id="receipt-corrections">{{ 'purchasing.corrections' | transloco }}</h2>
          <ol class="timeline">
            @for (correction of current.corrections; track correction.sequence) {
              <li [attr.data-correction]="correction.sequence">
                {{
                  'purchasing.correctionEntry'
                    | transloco: { line: correction.poLineNumber, delta: correction.acceptedDelta }
                }}
                · “{{ correction.reason }}” <br /><small>{{ instant(correction.at) }}</small>
              </li>
            }
          </ol>
        </section>
      }

      @if (current.canCorrect) {
        <section class="panel" aria-labelledby="receipt-correct">
          <h2 id="receipt-correct">{{ 'purchasing.correct' | transloco }}</h2>
          <form class="form-grid" (submit)="$event.preventDefault(); correct()" novalidate>
            <label for="correctionLine"
              >{{ 'purchasing.correctionLine' | transloco }}
              <select
                id="correctionLine"
                class="select"
                [value]="line()"
                (change)="line.set(+$any($event.target).value)"
              >
                @for (item of current.lines; track item.poLineNumber) {
                  <option [value]="item.poLineNumber">
                    {{ item.poLineNumber }} · {{ item.description }}
                  </option>
                }
              </select>
            </label>
            <label for="correctionDelta"
              >{{ 'purchasing.correctionDelta' | transloco }}
              <input
                id="correctionDelta"
                hlmInput
                inputmode="decimal"
                [value]="delta()"
                (input)="delta.set($any($event.target).value)"
              />
            </label>
            <label for="correctionReason"
              >{{ 'common.reason' | transloco }}
              <textarea
                id="correctionReason"
                class="textarea"
                rows="3"
                [attr.maxlength]="reasonMax"
                [attr.aria-invalid]="showReasonError() ? 'true' : null"
                [attr.aria-describedby]="
                  showReasonError() ? 'correctionReasonError' : 'correctionReasonHint'
                "
                [value]="reason()"
                (input)="reason.set($any($event.target).value)"
              ></textarea>
            </label>
            @if (showReasonError(); as problem) {
              <p id="correctionReasonError" class="warning">
                {{
                  'purchasing.reasonErrors.' + problem
                    | transloco: { min: reasonMin, max: reasonMax }
                }}
              </p>
            } @else {
              <p id="correctionReasonHint" class="hint">
                {{ 'purchasing.correctionHint' | transloco }}
              </p>
            }
            <div class="actions">
              <button hlmBtn type="submit" [disabled]="busy()">
                {{ 'purchasing.postCorrection' | transloco }}
              </button>
            </div>
          </form>
        </section>
      }
    }
  `,
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
