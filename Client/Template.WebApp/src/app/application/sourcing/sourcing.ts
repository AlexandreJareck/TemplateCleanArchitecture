import type { Result } from '../common/result';
import type { Page } from '../master-data/master-data';
import type { Money } from '../procurement/procurement';

export type RfqStatus = 'Draft' | 'Published' | 'Closed' | 'AwardPending' | 'Awarded' | 'Cancelled';

export type SourcingMethod = 'Competitive' | 'Direct';

export type QuoteSource = 'Email' | 'Phone' | 'Document' | 'Other';

export type RfqAction =
  | 'invite'
  | 'remove-invitation'
  | 'publish'
  | 'extend-deadline'
  | 'remind'
  | 'enter-quote'
  | 'revise-quote'
  | 'withdraw-quote'
  | 'close'
  | 'close-early'
  | 'cancel'
  | 'record-rate'
  | 'award';

export interface RfqSummary {
  readonly id: string;
  readonly number: string;
  readonly requisitionId: string;
  readonly requisitionNumber: string;
  readonly roundNumber: number;
  readonly method: SourcingMethod;
  readonly status: RfqStatus;
  readonly deadline?: string;
  readonly invitationCount: number;
  readonly version: string;
}

export interface RfqLine {
  readonly lineNumber: number;
  readonly description: string;
  readonly categoryCode: string;
  readonly unitCode: string;
  readonly quantity: string;
  readonly estimatedUnitPrice: Money;
}

/** Invitation with the supplier snapshot taken when it was created (SRC-003). */
export interface RfqInvitation {
  readonly supplierId: string;
  readonly supplierLegalName: string;
  readonly supplierTaxId: string;
  readonly supplierStatusAtInvitation: string;
  readonly invitedAt: string;
  readonly removedAt?: string;
  readonly isActive: boolean;
}

export interface QuoteLine {
  readonly rfqLineNumber: number;
  readonly quantityOffered: string;
  readonly unitPrice: Money;
  readonly lineTotal: Money;
}

export interface QuoteRevision {
  readonly id: string;
  readonly revisionNumber: number;
  readonly status: 'Current' | 'Superseded' | 'Withdrawn';
  readonly currency: string;
  readonly lines: readonly QuoteLine[];
  readonly subtotal: Money;
  readonly freight: Money;
  readonly discount: Money;
  readonly taxesIncluded: Money;
  readonly total: Money;
  readonly validUntil: string;
  readonly deliveryDays: number;
  readonly paymentTerms: string;
  readonly notes?: string;
  readonly source: QuoteSource;
  readonly receivedAt: string;
  readonly enteredBy?: string;
  readonly enteredAt: string;
  readonly withdrawalReason?: string;
}

export interface Quote {
  readonly id: string;
  readonly supplierId: string;
  readonly supplierLegalName: string;
  readonly latestStatus: QuoteRevision['status'];
  readonly revisions: readonly QuoteRevision[];
}

/** One entry of the server comparison; ranks and normalized totals are never computed in the browser. */
export interface ComparisonEntry {
  readonly quoteId: string;
  readonly quoteRevisionId: string;
  readonly supplierId: string;
  readonly supplierLegalName: string;
  readonly revisionNumber: number;
  readonly rawTotal: Money;
  readonly normalizedTotal?: Money;
  readonly rate?: string;
  readonly deliveryDays: number;
  readonly receivedAt: string;
  readonly validUntil: string;
  readonly coveredLines: number;
  readonly totalLines: number;
  readonly isValid: boolean;
  readonly invalidReasons: readonly string[];
  readonly rank?: number;
}

export interface Comparison {
  readonly sequence: number;
  readonly status: 'Complete' | 'Incomplete';
  readonly baseCurrency: string;
  readonly closeLocalDate: string;
  readonly validQuoteCount: number;
  readonly requiredValidQuotes: number;
  readonly warnings: readonly string[];
  readonly missingRateCurrencies: readonly string[];
  readonly recommendedQuoteRevisionId?: string;
  readonly entries: readonly ComparisonEntry[];
}

export interface RfqAward {
  readonly id: string;
  readonly status: string;
  readonly supplierId: string;
  readonly supplierLegalName: string;
  readonly quoteRevisionId?: string;
  readonly rank?: number;
  readonly awardedTotal?: Money;
  readonly normalizedTotal?: Money;
  readonly requisitionCurrencyTotal?: Money;
  readonly justification?: string;
  readonly submittedAt: string;
}

export interface RfqWaiver {
  readonly type: string;
  readonly ruleId: string;
  readonly reason: string;
  readonly decidedAt: string;
}

export interface ExchangeRate {
  readonly purpose: 'Estimate' | 'Comparison';
  readonly fromCurrency: string;
  readonly toCurrency: string;
  readonly rate: string;
  readonly source: 'BcbPtax' | 'Manual';
  readonly quoteDate: string;
  readonly retrievedAt: string;
}

export interface Rfq extends RfqSummary {
  readonly requisition: {
    readonly id: string;
    readonly number: string;
    readonly revision: number;
    readonly estimatedTotal: Money;
    readonly neededBy: string;
  };
  readonly policyVersion: number;
  readonly baseCurrency: string;
  readonly estimateInBaseCurrency?: Money;
  readonly aboveThreshold: boolean;
  readonly requiredInvitations: number;
  readonly requiredPublishWaiver?: string;
  readonly closedEarly: boolean;
  readonly closeReason?: string;
  readonly cancellationReason?: string;
  readonly lines: readonly RfqLine[];
  readonly invitations: readonly RfqInvitation[];
  readonly commercialDetailsVisible: boolean;
  readonly quotes: readonly Quote[];
  readonly comparison?: Comparison;
  readonly awards: readonly RfqAward[];
  readonly waivers: readonly RfqWaiver[];
  readonly exchangeRates: readonly ExchangeRate[];
  readonly allowedActions: readonly RfqAction[];
}

export interface RfqHistoryEntry {
  readonly sequence: number;
  readonly action: string;
  readonly from?: string;
  readonly to: string;
  readonly actorId?: string;
  readonly at: string;
  readonly reason?: string;
  readonly eventType?: string;
}

export interface QuoteLineInput {
  readonly rfqLineNumber: number;
  readonly quantityOffered: string;
  readonly unitPrice: string;
}

/** Buyer-entered quote; totals are computed by the server (CALC-002). */
export interface QuoteInput {
  readonly supplierId?: string;
  readonly currency: string;
  readonly lines: readonly QuoteLineInput[];
  readonly freight: string;
  readonly discount: string;
  readonly taxesIncluded: string;
  readonly validUntil: string;
  readonly deliveryDays: number;
  readonly paymentTerms: string;
  readonly notes: string;
  readonly source: QuoteSource;
  readonly receivedAt: string;
}

export interface AwardInput {
  readonly quoteRevisionId: string;
  readonly justification?: string;
  readonly insufficientQuotationsReason?: string;
  readonly nonLeadingReason?: string;
}

export interface RfqQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly status?: string;
  readonly requisitionId?: string;
  readonly q?: string;
}

/** Client use-case needs for sourcing; every change carries the version it was read with (If-Match). */
export interface SourcingPort {
  list(query: RfqQuery, signal?: AbortSignal): Promise<Result<Page<RfqSummary>>>;
  get(id: string, signal?: AbortSignal): Promise<Result<Rfq>>;
  history(id: string, signal?: AbortSignal): Promise<Result<readonly RfqHistoryEntry[]>>;
  create(requisitionId: string, method: SourcingMethod, signal?: AbortSignal): Promise<Result<Rfq>>;
  invite(rfq: Rfq, supplierId: string, signal?: AbortSignal): Promise<Result<Rfq>>;
  removeInvitation(rfq: Rfq, supplierId: string, signal?: AbortSignal): Promise<Result<Rfq>>;
  publish(
    rfq: Rfq,
    deadline: string,
    waiverReason?: string,
    signal?: AbortSignal,
  ): Promise<Result<Rfq>>;
  extendDeadline(
    rfq: Rfq,
    deadline: string,
    reason: string,
    signal?: AbortSignal,
  ): Promise<Result<Rfq>>;
  remind(rfq: Rfq, signal?: AbortSignal): Promise<Result<Rfq>>;
  enterQuote(rfq: Rfq, quote: QuoteInput, signal?: AbortSignal): Promise<Result<Rfq>>;
  reviseQuote(
    rfq: Rfq,
    quoteId: string,
    quote: QuoteInput,
    signal?: AbortSignal,
  ): Promise<Result<Rfq>>;
  withdrawQuote(
    rfq: Rfq,
    quoteId: string,
    reason: string,
    signal?: AbortSignal,
  ): Promise<Result<Rfq>>;
  close(rfq: Rfq, reason?: string, signal?: AbortSignal): Promise<Result<Rfq>>;
  cancel(rfq: Rfq, reason: string, signal?: AbortSignal): Promise<Result<Rfq>>;
  recordRate(
    rfq: Rfq,
    rate: { currency: string; rate: string; quoteDate: string; reason: string },
    signal?: AbortSignal,
  ): Promise<Result<Rfq>>;
  award(rfq: Rfq, award: AwardInput, signal?: AbortSignal): Promise<Result<Rfq>>;
}

/** Sourcing presentation helpers: state and permission checks only; every rule is decided by the API. */
export class SourcingUseCases {
  static can(rfq: Rfq | undefined, action: RfqAction): boolean {
    return rfq?.allowedActions.includes(action) ?? false;
  }

  /** Server comparison entries in their server rank order (unranked entries last, as received). */
  static rankedEntries(comparison: Comparison | undefined): readonly ComparisonEntry[] {
    return comparison?.entries ?? [];
  }

  static recommended(rfq: Rfq | undefined): ComparisonEntry | undefined {
    const id = rfq?.comparison?.recommendedQuoteRevisionId;
    return id ? rfq?.comparison?.entries.find((entry) => entry.quoteRevisionId === id) : undefined;
  }

  /** Waiver reasons the server asked for in a previous award answer (SRC-017/018); never inferred locally. */
  static waiverForProblem(code: string | undefined): 'insufficient' | 'non-leading' | undefined {
    if (code === 'award.insufficient-quotations') return 'insufficient';
    if (code === 'award.non-leading-requires-waiver') return 'non-leading';
    return undefined;
  }

  static activeInvitations(rfq: Rfq | undefined): readonly RfqInvitation[] {
    return rfq?.invitations.filter((invitation) => invitation.isActive) ?? [];
  }

  static supplierWithoutQuote(rfq: Rfq | undefined): readonly RfqInvitation[] {
    const quoted = new Set(
      (rfq?.quotes ?? [])
        .filter((quote) => quote.latestStatus === 'Current')
        .map((quote) => quote.supplierId),
    );
    return SourcingUseCases.activeInvitations(rfq).filter(
      (invitation) => !quoted.has(invitation.supplierId),
    );
  }

  /** Validates a local `YYYY-MM-DDTHH:mm` deadline and converts it to an ISO UTC instant (window checked by the API). */
  static deadlineInstant(local: string): string | undefined {
    if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(local)) return undefined;
    const instant = new Date(local);
    return Number.isNaN(instant.getTime()) ? undefined : instant.toISOString();
  }
}
