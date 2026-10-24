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
  templateUrl: './receipt-editor.page.html',
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
