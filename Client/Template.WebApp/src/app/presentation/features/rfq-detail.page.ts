import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import type { SupplierSummary } from '@application/master-data/master-data';
import { RequisitionUseCases } from '@application/procurement/procurement';
import {
  SourcingUseCases,
  type ComparisonEntry,
  type QuoteInput,
  type QuoteSource,
  type Rfq,
  type RfqAction,
  type RfqHistoryEntry,
} from '@application/sourcing/sourcing';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { ActionConfirmation, type ActionConfirmationRequest } from '../shared/action-confirmation';
import { MoneyPipe, ProblemFeedback, StatusBadge } from '../shared/procurement-ui';
import { SessionFacade } from '../state/session.facade';
import { MASTER_DATA_PORT, SOURCING_PORT } from '../state/ports';

type Pending = 'cancel' | 'close-early' | 'withdraw-quote' | 'award' | undefined;

interface QuoteForm {
  supplierId: string;
  quoteId: string;
  currency: string;
  lines: { rfqLineNumber: number; quantityOffered: string; unitPrice: string }[];
  freight: string;
  discount: string;
  taxesIncluded: string;
  validUntil: string;
  deliveryDays: string;
  paymentTerms: string;
  notes: string;
  source: QuoteSource;
  receivedAt: string;
}

const SOURCES: readonly QuoteSource[] = ['Email', 'Phone', 'Document', 'Other'];

/**
 * RFQ workspace (SRC-001..022): requisition snapshot lines, invitations with supplier snapshots, publication with the
 * deadline and the Direct waiver reason, buyer-entered quote revisions, close, the server comparison (raw and
 * normalized totals, rate provenance, coverage, validity reasons, rank, warnings), manual rate, recommendation and
 * award confirmation with the waiver reasons the server asks for, history and conflict recovery. The browser formats
 * and previews only; ranks, conversions and totals come from the API.
 */
@Component({
  selector: 'app-rfq-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    TranslocoPipe,
    HlmButton,
    HlmInput,
    ActionConfirmation,
    MoneyPipe,
    ProblemFeedback,
    StatusBadge,
  ],
  templateUrl: './rfq-detail.page.html',
  styleUrl: './data-pages.css',
})
export class RfqDetailPage {
  private readonly port = inject(SOURCING_PORT);
  private readonly masterData = inject(MASTER_DATA_PORT);
  private readonly transloco = inject(TranslocoService);
  private readonly route = inject(ActivatedRoute);
  readonly sources = SOURCES;
  protected readonly session = inject(SessionFacade);
  readonly rfq = signal<Rfq | undefined>(undefined);
  readonly history = signal<readonly RfqHistoryEntry[]>([]);
  readonly suppliers = signal<readonly SupplierSummary[]>([]);
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly notice = signal<string | undefined>(undefined);
  readonly pending = signal<Pending>(undefined);
  readonly inviteSupplier = signal('');
  readonly deadline = signal('');
  readonly waiverReason = signal('');
  readonly reason = signal('');
  readonly quoteForm = signal<QuoteForm | undefined>(undefined);
  readonly withdrawQuoteId = signal('');
  readonly awardEntry = signal<ComparisonEntry | undefined>(undefined);
  readonly justification = signal('');
  readonly insufficientReason = signal('');
  readonly nonLeadingReason = signal('');
  readonly waiverNeeded = signal<readonly ('insufficient' | 'non-leading')[]>([]);
  readonly rateCurrency = signal('');
  readonly rateValue = signal('');
  readonly rateDate = signal('');
  readonly entries = computed(() => SourcingUseCases.rankedEntries(this.rfq()?.comparison));
  readonly pendingSuppliers = computed(() => SourcingUseCases.supplierWithoutQuote(this.rfq()));
  readonly eligibleSuppliers = computed(() => {
    const invited = new Set(
      SourcingUseCases.activeInvitations(this.rfq()).map((invitation) => invitation.supplierId),
    );
    return this.suppliers().filter((supplier) => supplier.eligible && !invited.has(supplier.id));
  });
  readonly quotePreview = computed(
    () =>
      RequisitionUseCases.preview(
        (this.quoteForm()?.lines ?? []).map((line) => ({
          quantity: line.quantityOffered,
          unitPrice: line.unitPrice,
        })),
      ).total,
  );
  readonly confirmation = computed<ActionConfirmationRequest | undefined>(() => {
    switch (this.pending()) {
      case 'cancel':
        return {
          title: this.transloco.translate('sourcing.confirmCancel'),
          actionLabel: this.transloco.translate('sourcing.cancel'),
        };
      case 'close-early':
        return {
          title: this.transloco.translate('sourcing.confirmCloseEarly'),
          actionLabel: this.transloco.translate('sourcing.closeEarly'),
          description: this.transloco.translate('sourcing.closeEarlyHelp'),
        };
      case 'withdraw-quote':
        return {
          title: this.transloco.translate('sourcing.confirmWithdrawQuote'),
          actionLabel: this.transloco.translate('sourcing.withdrawQuote'),
        };
      default:
        return undefined;
    }
  });

  constructor() {
    this.route.paramMap.subscribe(() => void this.load());
  }

  can(action: RfqAction) {
    return SourcingUseCases.can(this.rfq(), action);
  }

  async load() {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    const [rfq, history] = await Promise.all([this.port.get(id), this.port.history(id)]);
    if (!rfq.ok) {
      this.error.set(rfq.error);
      return;
    }
    this.error.set(undefined);
    this.apply(rfq.value);
    this.history.set(history.ok ? history.value : []);
    if (rfq.value.allowedActions.includes('invite') && this.suppliers().length === 0) {
      const suppliers = await this.masterData.listSuppliers({
        page: 1,
        pageSize: 100,
        sort: 'legalName',
      });
      if (suppliers.ok) this.suppliers.set(suppliers.value.items);
    }
  }

  async invite() {
    await this.run((rfq) => this.port.invite(rfq, this.inviteSupplier()), 'sourcing.invited');
    this.inviteSupplier.set('');
  }

  removeInvitation(supplierId: string) {
    return this.run((rfq) => this.port.removeInvitation(rfq, supplierId), 'sourcing.removedNotice');
  }

  publish() {
    const deadline = SourcingUseCases.deadlineInstant(this.deadline());
    if (!deadline) return;
    return this.run(
      (rfq) => this.port.publish(rfq, deadline, this.waiverReason()),
      'sourcing.published',
    );
  }

  extend() {
    const deadline = SourcingUseCases.deadlineInstant(this.deadline());
    if (!deadline) return;
    return this.run(
      (rfq) => this.port.extendDeadline(rfq, deadline, this.reason()),
      'sourcing.extended',
    );
  }

  remind() {
    return this.run((rfq) => this.port.remind(rfq), 'sourcing.reminded');
  }

  close() {
    return this.run((rfq) => this.port.close(rfq), 'sourcing.closedNotice');
  }

  editQuote(quoteId: string, supplierId: string) {
    this.resetQuote(quoteId, supplierId);
  }

  askWithdraw(quoteId: string) {
    this.withdrawQuoteId.set(quoteId);
    this.pending.set('withdraw-quote');
  }

  patch(change: Partial<QuoteForm>) {
    this.quoteForm.update((form) => (form ? { ...form, ...change } : form));
  }

  patchLine(index: number, change: Partial<QuoteForm['lines'][number]>) {
    this.quoteForm.update((form) =>
      form
        ? {
            ...form,
            lines: form.lines.map((line, position) =>
              position === index ? { ...line, ...change } : line,
            ),
          }
        : form,
    );
  }

  resetQuote(quoteId = '', supplierId = '') {
    const rfq = this.rfq();
    this.quoteForm.set(
      rfq && SourcingUseCases.can(rfq, 'enter-quote')
        ? {
            supplierId,
            quoteId,
            currency: rfq.baseCurrency || 'BRL',
            lines: rfq.lines.map((line) => ({
              rfqLineNumber: line.lineNumber,
              quantityOffered: line.quantity,
              unitPrice: '',
            })),
            freight: '',
            discount: '',
            taxesIncluded: '',
            validUntil: rfq.deadline?.slice(0, 10) ?? '',
            deliveryDays: '',
            paymentTerms: '',
            notes: '',
            source: 'Email',
            receivedAt: '',
          }
        : undefined,
    );
  }

  async saveQuote() {
    const form = this.quoteForm();
    if (!form) return;
    const receivedAt =
      SourcingUseCases.deadlineInstant(form.receivedAt) ?? new Date().toISOString();
    const input: QuoteInput = {
      supplierId: form.supplierId || undefined,
      currency: form.currency.trim().toUpperCase(),
      lines: form.lines.filter((line) => line.unitPrice.trim()),
      freight: form.freight,
      discount: form.discount,
      taxesIncluded: form.taxesIncluded,
      validUntil: form.validUntil,
      deliveryDays: Number.parseInt(form.deliveryDays, 10) || 0,
      paymentTerms: form.paymentTerms,
      notes: form.notes,
      source: form.source,
      receivedAt,
    };
    await this.run(
      (rfq) =>
        form.quoteId
          ? this.port.reviseQuote(rfq, form.quoteId, input)
          : this.port.enterQuote(rfq, input),
      'sourcing.quoteSaved',
    );
  }

  askAward(entry: ComparisonEntry) {
    this.awardEntry.set(entry);
    this.waiverNeeded.set([]);
    this.pending.set('award');
  }

  async award() {
    const entry = this.awardEntry();
    if (!entry) return;
    const result = await this.run(
      (rfq) =>
        this.port.award(rfq, {
          quoteRevisionId: entry.quoteRevisionId,
          justification: this.justification(),
          insufficientQuotationsReason: this.waiverNeeded().includes('insufficient')
            ? this.insufficientReason()
            : undefined,
          nonLeadingReason: this.waiverNeeded().includes('non-leading')
            ? this.nonLeadingReason()
            : undefined,
        }),
      'sourcing.awarded',
    );
    if (result) {
      this.pending.set(undefined);
      return;
    }
    const waiver = SourcingUseCases.waiverForProblem(this.error()?.code);
    if (waiver && !this.waiverNeeded().includes(waiver))
      this.waiverNeeded.update((list) => [...list, waiver]);
  }

  recordRate() {
    const currency = this.rateCurrency() || this.rfq()?.comparison?.missingRateCurrencies[0] || '';
    return this.run(
      (rfq) =>
        this.port.recordRate(rfq, {
          currency,
          rate: this.rateValue().trim(),
          quoteDate: this.rateDate(),
          reason: this.reason(),
        }),
      'sourcing.rateRecorded',
    );
  }

  async confirm(reason: string) {
    const action = this.pending();
    this.pending.set(undefined);
    if (action === 'cancel')
      await this.run((rfq) => this.port.cancel(rfq, reason), 'sourcing.cancelledNotice');
    if (action === 'close-early')
      await this.run((rfq) => this.port.close(rfq, reason), 'sourcing.closedNotice');
    if (action === 'withdraw-quote')
      await this.run(
        (rfq) => this.port.withdrawQuote(rfq, this.withdrawQuoteId(), reason),
        'sourcing.quoteWithdrawn',
      );
  }

  warningLabel(warning: string) {
    const [code, subject] = warning.split(':');
    return `${this.transloco.translate('sourcing.warnings.' + code)}${subject ? ` (${subject})` : ''}`;
  }

  private apply(rfq: Rfq) {
    this.rfq.set(rfq);
    if (!this.quoteForm() || !SourcingUseCases.can(rfq, 'enter-quote')) this.resetQuote();
  }

  /** Runs a versioned change; on success the server representation (with its new version) replaces the local one. */
  private async run(
    action: (
      rfq: Rfq,
    ) => Promise<{ ok: true; value: Rfq } | { ok: false; error: ApplicationError }>,
    notice: string,
  ) {
    const current = this.rfq();
    if (!current) return false;
    const result = await action(current);
    if (!result.ok) {
      this.error.set(result.error);
      this.notice.set(undefined);
      return false;
    }
    this.error.set(undefined);
    this.notice.set(notice);
    this.quoteForm.set(undefined);
    this.reason.set('');
    this.apply(result.value);
    const history = await this.port.history(result.value.id);
    if (history.ok) this.history.set(history.value);
    return true;
  }
}
