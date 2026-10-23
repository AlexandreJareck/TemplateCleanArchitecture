import type { Result } from '../common/result';
import type { Page } from '../master-data/master-data';

export type ReportType =
  'RequisitionPdf' | 'QuoteComparisonPdf' | 'BudgetConsumptionXlsx' | 'SupplierQuotesXlsx';

export type ReportFormat = 'Pdf' | 'Xlsx';

export type ReportJobStatus = 'Pending' | 'Running' | 'Ready' | 'Failed' | 'Expired' | 'Cancelled';

export type ReportJobAction = 'cancel' | 'download' | 'retry';

export const REPORT_TYPES: readonly ReportType[] = [
  'RequisitionPdf',
  'QuoteComparisonPdf',
  'BudgetConsumptionXlsx',
  'SupplierQuotesXlsx',
];

/** Report parameters; the server pins source versions and rejects parameters of other types (RPT-001/009). */
export interface ReportParameters {
  readonly requisitionId?: string;
  readonly rfqId?: string;
  readonly comparisonSequence?: number;
  readonly fiscalPeriodId?: string;
  readonly costCenterId?: string;
  readonly currency?: string;
  readonly from?: string;
  readonly to?: string;
  readonly supplierId?: string;
}

/** Server job state; progress, safe failure codes and allowed actions are copied, never derived locally. */
export interface ReportJob {
  readonly id: string;
  readonly reportType: ReportType;
  readonly format: ReportFormat;
  readonly status: ReportJobStatus;
  readonly parameters: ReportParameters;
  readonly requestedAt: string;
  readonly asOf: string;
  readonly timeZone: string;
  readonly attempts: number;
  readonly maxAttempts: number;
  readonly progress: number;
  readonly stage?: string;
  readonly failureCode?: string;
  readonly completedAt?: string;
  readonly expiresAt?: string;
  readonly fileName?: string;
  readonly mediaType?: string;
  readonly length?: number;
  /** API path of the output while downloadable; never a storage URL. */
  readonly contentUrl?: string;
  readonly allowedActions: readonly ReportJobAction[];
  readonly version: string;
}

/** Catalog entry for the current user (RPT-009); `available` reflects `reports.request` plus the data permission. */
export interface ReportTypeInfo {
  readonly type: ReportType;
  readonly format: ReportFormat;
  readonly requiredPermissions: readonly string[];
  readonly parameters: readonly string[];
  readonly available: boolean;
}

export interface ReportRequest {
  readonly reportType: ReportType;
  readonly parameters: ReportParameters;
}

export interface ReportJobQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly status?: string;
}

/** Client needs of the report center; the adapter performs the authenticated download itself. */
export interface ReportPort {
  types(signal?: AbortSignal): Promise<Result<readonly ReportTypeInfo[]>>;
  list(query: ReportJobQuery, signal?: AbortSignal): Promise<Result<Page<ReportJob>>>;
  get(id: string, signal?: AbortSignal): Promise<Result<ReportJob>>;
  request(request: ReportRequest, signal?: AbortSignal): Promise<Result<ReportJob>>;
  cancel(job: ReportJob, signal?: AbortSignal): Promise<Result<ReportJob>>;
  /** Streams the output through the API and hands it to the browser as an attachment. */
  download(job: ReportJob, signal?: AbortSignal): Promise<Result<void>>;
}

/** Client polling policy (api-guidelines §8): 2 s doubling up to 30 s, then "still running" after 10 minutes. */
export const POLLING = { firstDelayMs: 2_000, maxDelayMs: 30_000, giveUpAfterMs: 600_000 } as const;

export type PollOutcome =
  | { readonly kind: 'finished'; readonly job: ReportJob }
  | { readonly kind: 'still-running'; readonly job: ReportJob }
  | { readonly kind: 'error'; readonly result: Result<ReportJob> }
  | { readonly kind: 'cancelled' };

/** Framework-free time source so the policy is testable without timers. */
export interface PollingClock {
  now(): number;
  sleep(ms: number, signal?: AbortSignal): Promise<void>;
}

/** Report presentation rules: state and permission checks only; every rule is decided by the API. */
export class ReportUseCases {
  static isActive(job: ReportJob): boolean {
    return job.status === 'Pending' || job.status === 'Running';
  }

  static can(job: ReportJob | undefined, action: ReportJobAction): boolean {
    return job?.allowedActions.includes(action) ?? false;
  }

  /** Delay before poll number `attempt` (0-based): 2 s, 4 s, 8 s, 16 s, 30 s, 30 s, ... */
  static pollDelay(attempt: number): number {
    return Math.min(POLLING.firstDelayMs * 2 ** Math.max(0, attempt), POLLING.maxDelayMs);
  }

  /** The same request again (retry of a failed, expired or cancelled job); server-pinned versions are not resent. */
  static retryRequest(job: ReportJob): ReportRequest {
    const { comparisonSequence: _pinned, ...parameters } = job.parameters;
    void _pinned;
    return { reportType: job.reportType, parameters: ReportUseCases.compact(parameters) };
  }

  /** Parameters accepted by a type; values for other types are dropped before sending (the API rejects them). */
  static parametersFor(type: ReportType, values: ReportParameters): ReportParameters {
    const allowed: Record<ReportType, readonly (keyof ReportParameters)[]> = {
      RequisitionPdf: ['requisitionId'],
      QuoteComparisonPdf: ['rfqId'],
      BudgetConsumptionXlsx: ['fiscalPeriodId', 'currency', 'costCenterId'],
      SupplierQuotesXlsx: ['from', 'to', 'supplierId'],
    };
    const result: Record<string, unknown> = {};
    for (const key of allowed[type]) {
      const value = values[key];
      if (typeof value === 'string' ? value.trim() !== '' : value !== undefined)
        result[key] = typeof value === 'string' ? value.trim() : value;
    }
    return result as ReportParameters;
  }

  /** Polls a job with bounded backoff until it leaves Pending/Running, the time budget ends or the signal aborts. */
  static async poll(
    port: Pick<ReportPort, 'get'>,
    job: ReportJob,
    clock: PollingClock,
    onUpdate: (job: ReportJob) => void,
    signal?: AbortSignal,
  ): Promise<PollOutcome> {
    const started = clock.now();
    let current = job;
    for (let attempt = 0; ReportUseCases.isActive(current); attempt++) {
      if (clock.now() - started >= POLLING.giveUpAfterMs)
        return { kind: 'still-running', job: current };
      try {
        await clock.sleep(ReportUseCases.pollDelay(attempt), signal);
      } catch {
        return { kind: 'cancelled' };
      }
      if (signal?.aborted) return { kind: 'cancelled' };
      const next = await port.get(current.id, signal);
      if (!next.ok) {
        if (next.error.kind === 'cancelled') return { kind: 'cancelled' };
        return { kind: 'error', result: next };
      }
      current = next.value;
      onUpdate(current);
    }
    return { kind: 'finished', job: current };
  }

  /** Translation key of a safe failure code (RPT-005); unknown codes fall back to a generic message. */
  static failureKey(code: string | undefined): string {
    const known = [
      'report.source-not-found',
      'report.source-changed',
      'report.too-large',
      'report.renderer-unavailable',
      'report.storage-unavailable',
      'report.timeout',
      'report.attempts-exhausted',
      'report.output-invalid',
    ];
    return code && known.includes(code)
      ? `reports.failures.${code.slice('report.'.length)}`
      : 'reports.failures.unexpected';
  }

  private static compact(parameters: ReportParameters): ReportParameters {
    return Object.fromEntries(
      Object.entries(parameters).filter(
        ([, value]) => value !== undefined && value !== null && value !== '',
      ),
    ) as ReportParameters;
  }
}
