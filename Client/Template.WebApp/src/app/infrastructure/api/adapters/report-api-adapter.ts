import { HttpClient, HttpErrorResponse, type HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { failure, success, type Result } from '@application/common/result';
import {
  REPORT_TYPES,
  type ReportFormat,
  type ReportJob,
  type ReportJobAction,
  type ReportJobQuery,
  type ReportJobStatus,
  type ReportParameters,
  type ReportPort,
  type ReportRequest,
  type ReportType,
  type ReportTypeInfo,
} from '@application/reports/reports';
import { firstValueFrom, fromEvent, takeUntil, type Observable } from 'rxjs';
import type { ReportJobView } from '../generated/models/reportJobView';
import type { ReportTypeView } from '../generated/models/reportTypeView';
import { ReportJobsService } from '../generated/report-jobs/report-jobs.service';
import { mapProblem } from '../../http/problem-mapper';
import { BrowserFileSaver } from '../../runtime/browser-file-saver';
import { mapPage } from './procurement-api-adapter';

const STATUSES: readonly ReportJobStatus[] = [
  'Pending',
  'Running',
  'Ready',
  'Failed',
  'Expired',
  'Cancelled',
];
const ACTIONS: readonly ReportJobAction[] = ['cancel', 'download', 'retry'];

/** Only same-origin API content paths of report jobs are ever fetched (never a server-supplied absolute URL). */
const CONTENT_PATH = /^\/api\/v2\/report-jobs\/[0-9a-f-]{36}\/content$/i;

/**
 * Generated-client adapter of the Phase 30 report contract: a fresh `Idempotency-Key` on every request, quoted
 * `If-Match` on cancel, Problem Details mapped to Application errors. The download streams the API content path with
 * the bearer token (interceptors) as a Blob and hands it to the browser as an attachment; no storage URL is involved.
 */
@Injectable({ providedIn: 'root' })
export class ReportApiAdapter implements ReportPort {
  private readonly reports = inject(ReportJobsService);
  private readonly http = inject(HttpClient);
  private readonly saver = inject(BrowserFileSaver);

  types(signal?: AbortSignal) {
    return this.execute(this.reports.getApiV2ReportJobsTypes(), signal, (values) =>
      values.map(mapType).filter((value): value is ReportTypeInfo => value !== undefined),
    );
  }

  list(query: ReportJobQuery, signal?: AbortSignal) {
    return this.execute(
      this.reports.getApiV2ReportJobs({
        page: query.page,
        pageSize: query.pageSize,
        status: query.status || undefined,
        sort: '-requestedAt',
      }),
      signal,
      (value) => mapPage(value, mapJob),
    );
  }

  get(id: string, signal?: AbortSignal) {
    return this.execute(this.reports.getApiV2ReportJobsId(id), signal, mapJob);
  }

  request(request: ReportRequest, signal?: AbortSignal) {
    const parameters = request.parameters;
    return this.execute(
      this.reports.postApiV2ReportJobs(
        {
          reportType: request.reportType,
          requisitionId: parameters.requisitionId ?? null,
          rfqId: parameters.rfqId ?? null,
          fiscalPeriodId: parameters.fiscalPeriodId ?? null,
          costCenterId: parameters.costCenterId ?? null,
          currency: parameters.currency ?? null,
          from: parameters.from ?? null,
          to: parameters.to ?? null,
          supplierId: parameters.supplierId ?? null,
        },
        { headers: { 'Idempotency-Key': crypto.randomUUID() } },
      ),
      signal,
      mapJob,
    );
  }

  cancel(job: ReportJob, signal?: AbortSignal) {
    return this.execute(
      this.reports.postApiV2ReportJobsIdCancel(job.id, {
        headers: { 'If-Match': `"${job.version}"` },
      }),
      signal,
      mapJob,
    );
  }

  async download(job: ReportJob, signal?: AbortSignal): Promise<Result<void>> {
    const path = job.contentUrl;
    if (!path || !CONTENT_PATH.test(path))
      return failure({ kind: 'conflict', code: 'report.not-ready' });
    try {
      const source = this.http.get(path, { observe: 'response', responseType: 'blob' });
      const response = await firstValueFrom(
        signal ? source.pipe(takeUntil(fromEvent(signal, 'abort'))) : source,
      );
      const blob = (response as HttpResponse<Blob>).body;
      if (!blob) return failure({ kind: 'unexpected', code: 'report.empty' });
      this.saver.save(blob, job.fileName ?? 'relatorio');
      return success(undefined);
    } catch (error) {
      return failure(mapProblem(await withJsonProblem(error), signal));
    }
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

/** Blob responses carry Problem Details as a Blob; parse it so the stable `code` reaches the Application error. */
async function withJsonProblem(error: unknown): Promise<unknown> {
  if (!(error instanceof HttpErrorResponse) || !(error.error instanceof Blob)) return error;
  try {
    return new HttpErrorResponse({
      error: JSON.parse(await error.error.text()) as unknown,
      headers: error.headers,
      status: error.status,
      statusText: error.statusText,
      url: error.url ?? undefined,
    });
  } catch {
    return new HttpErrorResponse({
      status: error.status,
      headers: error.headers,
      statusText: error.statusText,
    });
  }
}

function reportType(value: string | null | undefined): ReportType | undefined {
  return REPORT_TYPES.find((candidate) => candidate === value);
}

function format(value: string | null | undefined): ReportFormat {
  return value === 'Xlsx' ? 'Xlsx' : 'Pdf';
}

function mapType(value: ReportTypeView): ReportTypeInfo | undefined {
  const type = reportType(value.type);
  if (!type) return undefined;
  return {
    type,
    format: format(value.format),
    requiredPermissions: value.requiredPermissions ?? [],
    parameters: value.parameters ?? [],
    available: value.available ?? false,
  };
}

export function mapJob(value: ReportJobView): ReportJob {
  const parameters = value.parameters ?? {};
  const mapped: ReportParameters = {
    requisitionId: parameters.requisitionId ?? undefined,
    rfqId: parameters.rfqId ?? undefined,
    comparisonSequence: parameters.comparisonSequence ?? undefined,
    fiscalPeriodId: parameters.fiscalPeriodId ?? undefined,
    costCenterId: parameters.costCenterId ?? undefined,
    currency: parameters.currency ?? undefined,
    from: parameters.from ?? undefined,
    to: parameters.to ?? undefined,
    supplierId: parameters.supplierId ?? undefined,
  };
  return {
    id: value.id ?? '',
    reportType: reportType(value.reportType) ?? 'RequisitionPdf',
    format: format(value.format),
    status: STATUSES.find((candidate) => candidate === value.status) ?? 'Pending',
    parameters: Object.fromEntries(
      Object.entries(mapped).filter(([, item]) => item !== undefined),
    ) as ReportParameters,
    requestedAt: value.requestedAt ?? '',
    asOf: value.asOf ?? '',
    timeZone: value.timeZone ?? '',
    attempts: value.attempts ?? 0,
    maxAttempts: value.maxAttempts ?? 3,
    progress: value.progress ?? 0,
    stage: value.stage ?? undefined,
    failureCode: value.failureCode ?? undefined,
    completedAt: value.completedAt ?? undefined,
    expiresAt: value.expiresAt ?? undefined,
    fileName: value.fileName ?? undefined,
    mediaType: value.mediaType ?? undefined,
    length: value.length ?? undefined,
    contentUrl: value.contentUrl ?? undefined,
    allowedActions: (value.allowedActions ?? []).filter((action): action is ReportJobAction =>
      ACTIONS.includes(action as ReportJobAction),
    ),
    version: value.version ?? '',
  };
}
