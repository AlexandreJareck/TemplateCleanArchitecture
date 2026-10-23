import { inject, Injectable } from '@angular/core';
import { failure, success, type Result } from '@application/common/result';
import type { Money } from '@application/procurement/procurement';
import type {
  AwardInput,
  Comparison,
  QuoteInput,
  QuoteRevision,
  QuoteSource,
  Rfq,
  RfqAction,
  RfqHistoryEntry,
  RfqQuery,
  RfqStatus,
  RfqSummary,
  SourcingMethod,
  SourcingPort,
} from '@application/sourcing/sourcing';
import { firstValueFrom, fromEvent, takeUntil, type Observable } from 'rxjs';
import type { ComparisonView } from '../generated/models/comparisonView';
import type { MoneyView } from '../generated/models/moneyView';
import type { QuoteBody } from '../generated/models/quoteBody';
import type { QuoteRevisionView } from '../generated/models/quoteRevisionView';
import type { RfqHistoryView } from '../generated/models/rfqHistoryView';
import type { RfqSummaryView } from '../generated/models/rfqSummaryView';
import type { RfqView } from '../generated/models/rfqView';
import { RequestsForQuotationService } from '../generated/requests-for-quotation/requests-for-quotation.service';
import { mapProblem } from '../../http/problem-mapper';
import { mapPage } from './procurement-api-adapter';

const ACTIONS: readonly RfqAction[] = [
  'invite',
  'remove-invitation',
  'publish',
  'extend-deadline',
  'remind',
  'enter-quote',
  'revise-quote',
  'withdraw-quote',
  'close',
  'close-early',
  'cancel',
  'record-rate',
  'award',
];

const STATUSES: readonly RfqStatus[] = [
  'Draft',
  'Published',
  'Closed',
  'AwardPending',
  'Awarded',
  'Cancelled',
];

/**
 * Generated-client adapter of the Phase 29 sourcing contract: quoted `If-Match` on every change, a fresh
 * `Idempotency-Key` on every POST, Problem Details mapped to Application errors. Totals, ranks and normalized amounts
 * are copied from the server representation; nothing is computed here.
 */
@Injectable({ providedIn: 'root' })
export class SourcingApiAdapter implements SourcingPort {
  private readonly rfqs = inject(RequestsForQuotationService);

  list(query: RfqQuery, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.getApiV2RequestsForQuotation({
        page: query.page,
        pageSize: query.pageSize,
        status: query.status || undefined,
        requisitionId: query.requisitionId || undefined,
        q: query.q || undefined,
      }),
      signal,
      (value) => mapPage(value, mapRfqSummary),
    );
  }

  get(id: string, signal?: AbortSignal) {
    return this.execute(this.rfqs.getApiV2RequestsForQuotationId(id), signal, mapRfq);
  }

  history(id: string, signal?: AbortSignal) {
    return this.execute(this.rfqs.getApiV2RequestsForQuotationIdHistory(id), signal, mapHistory);
  }

  create(requisitionId: string, method: SourcingMethod, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.postApiV2RequestsForQuotation(
        { requisitionId, method },
        { headers: idempotency() },
      ),
      signal,
      mapRfq,
    );
  }

  invite(rfq: Rfq, supplierId: string, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.postApiV2RequestsForQuotationIdInvitations(
        rfq.id,
        { supplierId },
        { headers: change(rfq) },
      ),
      signal,
      mapRfq,
    );
  }

  removeInvitation(rfq: Rfq, supplierId: string, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.deleteApiV2RequestsForQuotationIdInvitationsSupplierId(rfq.id, supplierId, {
        headers: ifMatch(rfq.version),
      }),
      signal,
      mapRfq,
    );
  }

  publish(rfq: Rfq, deadline: string, waiverReason?: string, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.postApiV2RequestsForQuotationIdPublish(
        rfq.id,
        { deadline, waiverReason: waiverReason?.trim() || null },
        { headers: change(rfq) },
      ),
      signal,
      mapRfq,
    );
  }

  extendDeadline(rfq: Rfq, deadline: string, reason: string, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.postApiV2RequestsForQuotationIdExtendDeadline(
        rfq.id,
        { deadline, reason },
        { headers: change(rfq) },
      ),
      signal,
      mapRfq,
    );
  }

  remind(rfq: Rfq, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.postApiV2RequestsForQuotationIdReminders(rfq.id, { headers: change(rfq) }),
      signal,
      mapRfq,
    );
  }

  enterQuote(rfq: Rfq, quote: QuoteInput, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.postApiV2RequestsForQuotationIdQuotes(rfq.id, quoteBody(quote), {
        headers: change(rfq),
      }),
      signal,
      mapRfq,
    );
  }

  reviseQuote(rfq: Rfq, quoteId: string, quote: QuoteInput, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.postApiV2RequestsForQuotationIdQuotesQuoteIdRevisions(
        rfq.id,
        quoteId,
        quoteBody(quote),
        {
          headers: change(rfq),
        },
      ),
      signal,
      mapRfq,
    );
  }

  withdrawQuote(rfq: Rfq, quoteId: string, reason: string, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.postApiV2RequestsForQuotationIdQuotesQuoteIdWithdraw(
        rfq.id,
        quoteId,
        { reason },
        { headers: change(rfq) },
      ),
      signal,
      mapRfq,
    );
  }

  close(rfq: Rfq, reason?: string, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.postApiV2RequestsForQuotationIdClose(
        rfq.id,
        { reason: reason?.trim() || null },
        { headers: change(rfq) },
      ),
      signal,
      mapRfq,
    );
  }

  cancel(rfq: Rfq, reason: string, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.postApiV2RequestsForQuotationIdCancel(rfq.id, { reason }, { headers: change(rfq) }),
      signal,
      mapRfq,
    );
  }

  recordRate(
    rfq: Rfq,
    rate: { currency: string; rate: string; quoteDate: string; reason: string },
    signal?: AbortSignal,
  ) {
    return this.execute(
      this.rfqs.postApiV2RequestsForQuotationIdExchangeRates(rfq.id, rate, {
        headers: change(rfq),
      }),
      signal,
      mapRfq,
    );
  }

  award(rfq: Rfq, award: AwardInput, signal?: AbortSignal) {
    return this.execute(
      this.rfqs.postApiV2RequestsForQuotationIdAward(
        rfq.id,
        {
          quoteRevisionId: award.quoteRevisionId,
          justification: award.justification?.trim() || null,
          insufficientQuotationsReason: award.insufficientQuotationsReason?.trim() || null,
          nonLeadingReason: award.nonLeadingReason?.trim() || null,
        },
        { headers: change(rfq) },
      ),
      signal,
      mapRfq,
    );
  }

  private async execute<T, U>(
    source: Observable<T>,
    signal: AbortSignal | undefined,
    map: (value: T) => U,
  ): Promise<Result<U>> {
    try {
      const bounded = signal ? source.pipe(takeUntil(fromEvent(signal, 'abort'))) : source;
      return success(map(await firstValueFrom(bounded)));
    } catch (error) {
      return failure<U>(mapProblem(error, signal));
    }
  }
}

function money(value: MoneyView | null | undefined): Money {
  return { amount: value?.amount ?? '0.00', currency: value?.currency ?? '' };
}

function optionalMoney(value: MoneyView | null | undefined): Money | undefined {
  return value ? money(value) : undefined;
}

function status(value: string | null | undefined): RfqStatus {
  return STATUSES.find((candidate) => candidate === value) ?? 'Draft';
}

export function mapRfqSummary(value: RfqSummaryView): RfqSummary {
  return {
    id: value.id ?? '',
    number: value.number ?? '',
    requisitionId: value.requisitionId ?? '',
    requisitionNumber: value.requisitionNumber ?? '',
    roundNumber: value.roundNumber ?? 1,
    method: value.method === 'Direct' ? 'Direct' : 'Competitive',
    status: status(value.status),
    deadline: value.deadline ?? undefined,
    invitationCount: value.invitationCount ?? 0,
    version: value.version ?? '',
  };
}

function mapRevision(value: QuoteRevisionView): QuoteRevision {
  const revisionStatus =
    value.status === 'Superseded' || value.status === 'Withdrawn' ? value.status : 'Current';
  return {
    id: value.id ?? '',
    revisionNumber: value.revisionNumber ?? 1,
    status: revisionStatus,
    currency: value.currency ?? '',
    lines: (value.lines ?? []).map((line) => ({
      rfqLineNumber: line.rfqLineNumber ?? 0,
      quantityOffered: line.quantityOffered ?? '0',
      unitPrice: money(line.unitPrice),
      lineTotal: money(line.lineTotal),
    })),
    subtotal: money(value.subtotal),
    freight: money(value.freight),
    discount: money(value.discount),
    taxesIncluded: money(value.taxesIncluded),
    total: money(value.total),
    validUntil: value.validUntil ?? '',
    deliveryDays: value.deliveryDays ?? 0,
    paymentTerms: value.paymentTerms ?? '',
    notes: value.notes ?? undefined,
    source: (value.source ?? 'Other') as QuoteSource,
    receivedAt: value.receivedAt ?? '',
    enteredBy: value.enteredBy ?? undefined,
    enteredAt: value.enteredAt ?? '',
    withdrawalReason: value.withdrawalReason ?? undefined,
  };
}

function mapComparison(value: ComparisonView | undefined): Comparison | undefined {
  if (!value) return undefined;
  return {
    sequence: value.sequence ?? 1,
    status: value.status === 'Incomplete' ? 'Incomplete' : 'Complete',
    baseCurrency: value.baseCurrency ?? '',
    closeLocalDate: value.closeLocalDate ?? '',
    validQuoteCount: value.validQuoteCount ?? 0,
    requiredValidQuotes: value.requiredValidQuotes ?? 1,
    warnings: value.warnings ?? [],
    missingRateCurrencies: value.missingRateCurrencies ?? [],
    recommendedQuoteRevisionId: value.recommendedQuoteRevisionId ?? undefined,
    entries: (value.entries ?? []).map((entry) => ({
      quoteId: entry.quoteId ?? '',
      quoteRevisionId: entry.quoteRevisionId ?? '',
      supplierId: entry.supplierId ?? '',
      supplierLegalName: entry.supplierLegalName ?? '',
      revisionNumber: entry.revisionNumber ?? 1,
      rawTotal: money(entry.rawTotal),
      normalizedTotal: optionalMoney(entry.normalizedTotal),
      rate: entry.rate ?? undefined,
      deliveryDays: entry.deliveryDays ?? 0,
      receivedAt: entry.receivedAt ?? '',
      validUntil: entry.validUntil ?? '',
      coveredLines: entry.coveredLines ?? 0,
      totalLines: entry.totalLines ?? 0,
      isValid: entry.isValid ?? false,
      invalidReasons: entry.invalidReasons ?? [],
      rank: entry.rank ?? undefined,
    })),
  };
}

export function mapRfq(value: RfqView): Rfq {
  return {
    id: value.id ?? '',
    number: value.number ?? '',
    requisitionId: value.requisition?.id ?? '',
    requisitionNumber: value.requisition?.number ?? '',
    roundNumber: value.roundNumber ?? 1,
    method: value.method === 'Direct' ? 'Direct' : 'Competitive',
    status: status(value.status),
    deadline: value.deadline ?? undefined,
    invitationCount: (value.invitations ?? []).filter((invitation) => invitation.isActive).length,
    version: value.version ?? '',
    requisition: {
      id: value.requisition?.id ?? '',
      number: value.requisition?.number ?? '',
      revision: value.requisition?.revision ?? 1,
      estimatedTotal: money(value.requisition?.estimatedTotal),
      neededBy: value.requisition?.neededBy ?? '',
    },
    policyVersion: value.policy?.version ?? 1,
    baseCurrency: value.baseCurrency ?? '',
    estimateInBaseCurrency: optionalMoney(value.estimateInBaseCurrency),
    aboveThreshold: value.aboveThreshold ?? true,
    requiredInvitations: value.requiredInvitations ?? 1,
    requiredPublishWaiver: value.requiredPublishWaiver ?? undefined,
    closedEarly: value.closedEarly ?? false,
    closeReason: value.closeReason ?? undefined,
    cancellationReason: value.cancellationReason ?? value.cancellationReasonCode ?? undefined,
    lines: (value.lines ?? []).map((line) => ({
      lineNumber: line.lineNumber ?? 0,
      description: line.description ?? '',
      categoryCode: line.categoryCode ?? '',
      unitCode: line.unitCode ?? '',
      quantity: line.quantity ?? '0',
      estimatedUnitPrice: money(line.estimatedUnitPrice),
    })),
    invitations: (value.invitations ?? []).map((invitation) => ({
      supplierId: invitation.supplierId ?? '',
      supplierLegalName: invitation.supplierLegalName ?? '',
      supplierTaxId: invitation.supplierTaxId ?? '',
      supplierStatusAtInvitation: invitation.supplierStatusAtInvitation ?? '',
      invitedAt: invitation.invitedAt ?? '',
      removedAt: invitation.removedAt ?? undefined,
      isActive: invitation.isActive ?? false,
    })),
    commercialDetailsVisible: value.commercialDetailsVisible ?? false,
    quotes: (value.quotes ?? []).map((quote) => ({
      id: quote.id ?? '',
      supplierId: quote.supplierId ?? '',
      supplierLegalName: quote.supplierLegalName ?? '',
      latestStatus:
        quote.latestStatus === 'Superseded' || quote.latestStatus === 'Withdrawn'
          ? quote.latestStatus
          : 'Current',
      revisions: (quote.revisions ?? []).map(mapRevision),
    })),
    comparison: mapComparison(value.comparison),
    awards: (value.awards ?? []).map((award) => ({
      id: award.id ?? '',
      status: award.status ?? '',
      supplierId: award.supplierId ?? '',
      supplierLegalName: award.supplierLegalName ?? '',
      quoteRevisionId: award.quoteRevisionId ?? undefined,
      rank: award.rank ?? undefined,
      awardedTotal: optionalMoney(award.awardedTotal),
      normalizedTotal: optionalMoney(award.normalizedTotal),
      requisitionCurrencyTotal: optionalMoney(award.requisitionCurrencyTotal),
      justification: award.justification ?? undefined,
      submittedAt: award.submittedAt ?? '',
    })),
    waivers: (value.waivers ?? []).map((waiver) => ({
      type: waiver.type ?? '',
      ruleId: waiver.ruleId ?? '',
      reason: waiver.reason ?? '',
      decidedAt: waiver.decidedAt ?? '',
    })),
    exchangeRates: (value.exchangeRates ?? []).map((rate) => ({
      purpose: rate.purpose === 'Estimate' ? 'Estimate' : 'Comparison',
      fromCurrency: rate.fromCurrency ?? '',
      toCurrency: rate.toCurrency ?? '',
      rate: rate.rate ?? '',
      source: rate.source === 'Manual' ? 'Manual' : 'BcbPtax',
      quoteDate: rate.quoteDate ?? '',
      retrievedAt: rate.retrievedAt ?? '',
    })),
    allowedActions: (value.allowedActions ?? []).filter((action): action is RfqAction =>
      ACTIONS.includes(action as RfqAction),
    ),
  };
}

function mapHistory(value: RfqHistoryView): readonly RfqHistoryEntry[] {
  return (value.entries ?? []).map((entry) => ({
    sequence: entry.sequence ?? 0,
    action: entry.action ?? '',
    from: entry.from ?? undefined,
    to: entry.to ?? '',
    actorId: entry.actorId ?? undefined,
    at: entry.at ?? '',
    reason: entry.reason ?? undefined,
    eventType: entry.eventType ?? undefined,
  }));
}

function quoteBody(quote: QuoteInput): QuoteBody {
  return {
    supplierId: quote.supplierId ?? null,
    currency: quote.currency,
    lines: quote.lines.map((line) => ({
      rfqLineNumber: line.rfqLineNumber,
      quantityOffered: line.quantityOffered.trim(),
      unitPrice: line.unitPrice.trim(),
    })),
    freight: quote.freight.trim() || null,
    discount: quote.discount.trim() || null,
    taxesIncluded: quote.taxesIncluded.trim() || null,
    validUntil: quote.validUntil,
    deliveryDays: quote.deliveryDays,
    paymentTerms: quote.paymentTerms,
    notes: quote.notes.trim() || null,
    source: quote.source,
    receivedAt: quote.receivedAt,
    attachments: [],
  };
}

function ifMatch(version: string) {
  return { 'If-Match': `"${version}"` };
}

function change(rfq: Rfq) {
  return { ...ifMatch(rfq.version), 'Idempotency-Key': crypto.randomUUID() };
}

function idempotency() {
  return { 'Idempotency-Key': crypto.randomUUID() };
}
