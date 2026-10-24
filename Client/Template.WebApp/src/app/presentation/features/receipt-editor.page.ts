import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import {
  DISCREPANCY_NOTES_MAX,
  DISCREPANCY_TYPES,
  PurchasingUseCases,
  type LineIssue,
  type PurchaseOrder,
  type PurchaseOrderLine,
  type ReceiptLineDraft,
} from '@application/purchasing/purchasing';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { ProblemFeedback } from '../shared/procurement-ui';
import { PURCHASING_PORT } from '../state/ports';

/**
 * Partial receipt editor (Phase 32, `/receiving/orders/:id/receipts/new`): one row per open order line with the
 * server's remaining quantity and cap, received/accepted/rejected inputs, and the discrepancy type and notes required
 * for a rejected quantity (RCV-004). The form mirrors RCV-002 and flags a quantity above the remaining one before
 * posting; the server decides the cap (RCV-003), the receipt date window (RCV-005) and completion (PUR-009). The
 * unsaved draft lives in this route's signals only.
 */
@Component({
  selector: 'app-receipt-editor-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, RouterLink, HlmButton, HlmInput, ProblemFeedback],
  template: `
    <header class="page-title">
      <p>
        {{ 'shell.breadcrumbHome' | transloco }} /
        <a routerLink="/receiving/queue">{{ 'nav.receiving' | transloco }}</a>
      </p>
      <h1>{{ 'purchasing.newReceipt' | transloco: { number: order()?.number ?? '' } }}</h1>
    </header>
    @if (problemKey(); as key) {
      <div class="feedback" role="alert">
        <strong>{{ key | transloco }}</strong>
        <span>{{ error()?.code }}</span>
      </div>
    } @else {
      <app-problem-feedback [error]="error()" (reload)="load()" />
    }

    @if (order(); as current) {
      <form
        class="panel form-grid"
        aria-labelledby="receipt-form"
        (submit)="$event.preventDefault(); post()"
        novalidate
      >
        <h2 id="receipt-form">{{ 'purchasing.receiptHeader' | transloco }}</h2>
        <label for="receivedOn"
          >{{ 'purchasing.receivedOn' | transloco }}
          <input
            id="receivedOn"
            hlmInput
            type="date"
            [value]="receivedOn()"
            (change)="receivedOn.set($any($event.target).value)"
          />
        </label>
        <label for="location"
          >{{ 'purchasing.location' | transloco }}
          <input
            id="location"
            hlmInput
            maxlength="200"
            [value]="location()"
            (input)="location.set($any($event.target).value)"
          />
        </label>
        <label for="deliveryReference"
          >{{ 'purchasing.deliveryReference' | transloco }}
          <input
            id="deliveryReference"
            hlmInput
            maxlength="100"
            [value]="reference()"
            (input)="reference.set($any($event.target).value)"
          />
        </label>

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
                <th>{{ 'purchasing.remaining' | transloco }}</th>
                <th>{{ 'purchasing.received' | transloco }}</th>
                <th>{{ 'purchasing.accepted' | transloco }}</th>
                <th>{{ 'purchasing.rejected' | transloco }}</th>
                <th>{{ 'purchasing.discrepancy' | transloco }}</th>
              </tr>
            </thead>
            <tbody>
              @for (line of openLines(); track line.lineNumber) {
                @let draft = drafts()[line.lineNumber];
                @let issues = issuesFor(line);
                <tr [attr.data-line]="line.lineNumber">
                  <td data-label="#">{{ line.lineNumber }}</td>
                  <td [attr.data-label]="'purchasing.description' | transloco">
                    {{ line.description }} ({{ line.unitCode }})
                  </td>
                  <td [attr.data-label]="'purchasing.remaining' | transloco">
                    {{ line.remainingQuantity }} / {{ line.quantity }}
                  </td>
                  <td [attr.data-label]="'purchasing.received' | transloco">
                    <label class="sr-only" [attr.for]="'received-' + line.lineNumber">{{
                      'purchasing.receivedLine' | transloco: { line: line.lineNumber }
                    }}</label>
                    <input
                      hlmInput
                      inputmode="decimal"
                      [id]="'received-' + line.lineNumber"
                      [value]="draft.received"
                      [attr.aria-invalid]="issues.length > 0 ? 'true' : null"
                      [attr.aria-describedby]="
                        issues.length > 0 ? 'issues-' + line.lineNumber : null
                      "
                      (input)="update(line.lineNumber, { received: $any($event.target).value })"
                    />
                  </td>
                  <td [attr.data-label]="'purchasing.accepted' | transloco">
                    <label class="sr-only" [attr.for]="'accepted-' + line.lineNumber">{{
                      'purchasing.acceptedLine' | transloco: { line: line.lineNumber }
                    }}</label>
                    <input
                      hlmInput
                      inputmode="decimal"
                      [id]="'accepted-' + line.lineNumber"
                      [value]="draft.accepted"
                      (input)="update(line.lineNumber, { accepted: $any($event.target).value })"
                    />
                  </td>
                  <td [attr.data-label]="'purchasing.rejected' | transloco">
                    <label class="sr-only" [attr.for]="'rejected-' + line.lineNumber">{{
                      'purchasing.rejectedLine' | transloco: { line: line.lineNumber }
                    }}</label>
                    <input
                      hlmInput
                      inputmode="decimal"
                      [id]="'rejected-' + line.lineNumber"
                      [value]="draft.rejected"
                      (input)="update(line.lineNumber, { rejected: $any($event.target).value })"
                    />
                  </td>
                  <td [attr.data-label]="'purchasing.discrepancy' | transloco">
                    <label class="sr-only" [attr.for]="'type-' + line.lineNumber">{{
                      'purchasing.discrepancyTypeLine' | transloco: { line: line.lineNumber }
                    }}</label>
                    <select
                      class="select"
                      [id]="'type-' + line.lineNumber"
                      [value]="draft.discrepancyType ?? ''"
                      (change)="
                        update(line.lineNumber, {
                          discrepancyType: $any($event.target).value || undefined,
                        })
                      "
                    >
                      <option value="">—</option>
                      @for (type of types; track type) {
                        <option [value]="type">
                          {{ 'purchasing.discrepancyTypes.' + type | transloco }}
                        </option>
                      }
                    </select>
                    <label class="sr-only" [attr.for]="'notes-' + line.lineNumber">{{
                      'purchasing.discrepancyNotesLine' | transloco: { line: line.lineNumber }
                    }}</label>
                    <input
                      hlmInput
                      [id]="'notes-' + line.lineNumber"
                      [attr.maxlength]="notesMax"
                      [value]="draft.discrepancyNotes ?? ''"
                      [attr.placeholder]="'purchasing.discrepancyNotes' | transloco"
                      (input)="
                        update(line.lineNumber, { discrepancyNotes: $any($event.target).value })
                      "
                    />
                    @if (issues.length > 0) {
                      <p class="warning" [id]="'issues-' + line.lineNumber">
                        @for (issue of issues; track issue) {
                          <span>{{
                            'purchasing.lineIssues.' + issue
                              | transloco
                                : { remaining: line.remainingQuantity, decimals: line.unitDecimals }
                          }}</span>
                        }
                      </p>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (submitted() && active().length === 0) {
          <p class="warning" role="alert">{{ 'purchasing.noLines' | transloco }}</p>
        }
        <p class="hint">
          {{ 'purchasing.receiptHint' | transloco: { policy: current.receivingPolicy } }}
        </p>
        <div class="actions">
          <a hlmBtn variant="outline" [routerLink]="['/purchasing/orders', current.id]">{{
            'common.cancel' | transloco
          }}</a>
          <button hlmBtn type="submit" [disabled]="busy()">
            {{ 'purchasing.postReceipt' | transloco }}
          </button>
        </div>
      </form>
    }
  `,
  styleUrl: './data-pages.css',
})
export class ReceiptEditorPage {
  private readonly purchasing = inject(PURCHASING_PORT);
  private readonly router = inject(Router);
  private readonly orderId = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly types = DISCREPANCY_TYPES;
  protected readonly notesMax = DISCREPANCY_NOTES_MAX;

  readonly order = signal<PurchaseOrder | undefined>(undefined);
  readonly drafts = signal<Record<number, ReceiptLineDraft>>({});
  readonly receivedOn = signal(localDate());
  readonly location = signal('');
  readonly reference = signal('');
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly busy = signal(false);
  readonly submitted = signal(false);

  readonly openLines = computed(() =>
    (this.order()?.lines ?? []).filter((line) => !line.shortClosed),
  );
  readonly active = computed(() => PurchasingUseCases.activeLines(Object.values(this.drafts())));
  readonly problemKey = computed(() => {
    const error = this.error();
    return error ? PurchasingUseCases.problemKey(error) : undefined;
  });

  constructor() {
    void this.load();
  }

  async load() {
    const result = await this.purchasing.get(this.orderId);
    if (!result.ok) return this.error.set(result.error);
    this.error.set(undefined);
    this.order.set(result.value);
    const drafts: Record<number, ReceiptLineDraft> = {};
    for (const line of result.value.lines)
      drafts[line.lineNumber] = {
        poLineNumber: line.lineNumber,
        received: '',
        accepted: '',
        rejected: '',
      };
    this.drafts.set(drafts);
  }

  update(lineNumber: number, change: Partial<ReceiptLineDraft>) {
    this.drafts.update((current) => {
      const previous = current[lineNumber]!;
      const next = { ...previous, ...change };
      // Typing only the received quantity accepts it all, until the receiver splits it.
      if (
        change.received !== undefined &&
        (previous.accepted === '' || previous.accepted === previous.received)
      )
        next.accepted = change.received;
      return { ...current, [lineNumber]: next };
    });
  }

  issuesFor(line: PurchaseOrderLine): readonly LineIssue[] {
    const draft = this.drafts()[line.lineNumber];
    if (!draft || PurchasingUseCases.activeLines([draft]).length === 0) return [];
    return PurchasingUseCases.lineIssues(draft, line);
  }

  async post() {
    const order = this.order();
    this.submitted.set(true);
    if (!order) return;
    const lines = this.active();
    if (lines.length === 0) return;
    if (this.openLines().some((line) => this.issuesFor(line).length > 0)) return;
    this.busy.set(true);
    const result = await this.purchasing.postReceipt({
      purchaseOrderId: order.id,
      receivedOn: this.receivedOn(),
      location: this.location(),
      deliveryReference: this.reference(),
      lines,
    });
    this.busy.set(false);
    if (!result.ok) {
      this.error.set(result.error);
      if (PurchasingUseCases.isStale(result.error)) await this.load();
      return;
    }
    await this.router.navigate(['/receiving/receipts', result.value.receipt.id], {
      state: { orderStatus: result.value.order.status },
    });
  }
}

function localDate() {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${now.getFullYear()}-${month}-${day}`;
}
