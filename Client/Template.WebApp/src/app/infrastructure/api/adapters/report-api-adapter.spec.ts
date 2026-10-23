import { HttpClient, HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import type { ReportJob } from '@application/reports/reports';
import { of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ReportJobsService } from '../generated/report-jobs/report-jobs.service';
import { BrowserFileSaver } from '../../runtime/browser-file-saver';
import { mapJob, ReportApiAdapter } from './report-api-adapter';

interface Options {
  headers?: Record<string, string>;
}

const view = {
  id: '11111111-2222-3333-4444-555555555555',
  reportType: 'BudgetConsumptionXlsx',
  format: 'Xlsx',
  status: 'Ready',
  parameters: { fiscalPeriodId: 'fp-1', currency: 'BRL', requisitionId: null },
  requestedAt: '2027-03-01T13:00:00Z',
  asOf: '2027-03-01T13:00:00Z',
  timeZone: 'America/Sao_Paulo',
  attempts: 1,
  maxAttempts: 3,
  progress: 100,
  fileName: 'orcamento-FY27.xlsx',
  mediaType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  contentUrl: '/api/v2/report-jobs/11111111-2222-3333-4444-555555555555/content',
  allowedActions: ['download', 'unknown-action'],
  version: '4',
};

function adapter(service: object, http: object = {}, saver = { save: vi.fn() }) {
  TestBed.configureTestingModule({
    providers: [
      ReportApiAdapter,
      { provide: ReportJobsService, useValue: service },
      { provide: HttpClient, useValue: http },
      { provide: BrowserFileSaver, useValue: saver },
    ],
  });
  return { adapter: TestBed.inject(ReportApiAdapter), saver };
}

describe('ReportApiAdapter', () => {
  it('maps the job representation without inventing values', () => {
    const job = mapJob(view);
    expect(job.reportType).toBe('BudgetConsumptionXlsx');
    expect(job.status).toBe('Ready');
    expect(job.parameters).toEqual({ fiscalPeriodId: 'fp-1', currency: 'BRL' });
    expect(job.allowedActions).toEqual(['download']);
    expect(mapJob({ status: 'Weird', reportType: 'Nope' })).toMatchObject({
      status: 'Pending',
      reportType: 'RequisitionPdf',
      maxAttempts: 3,
    });
  });

  it('sends a fresh Idempotency-Key per request and If-Match on cancel', async () => {
    const calls: Options[] = [];
    const { adapter: subject } = adapter({
      postApiV2ReportJobs: (body: Record<string, unknown>, options: Options) => {
        calls.push(options);
        expect(body).toMatchObject({
          reportType: 'RequisitionPdf',
          requisitionId: 'req-1',
          rfqId: null,
        });
        return of(view);
      },
      postApiV2ReportJobsIdCancel: (_id: string, options: Options) => {
        calls.push(options);
        return of({ ...view, status: 'Cancelled' });
      },
    });
    await subject.request({ reportType: 'RequisitionPdf', parameters: { requisitionId: 'req-1' } });
    await subject.request({ reportType: 'RequisitionPdf', parameters: { requisitionId: 'req-1' } });
    const cancelled = await subject.cancel(mapJob(view));
    expect(calls[0]!.headers!['Idempotency-Key']).not.toBe(calls[1]!.headers!['Idempotency-Key']);
    expect(calls[2]!.headers!['If-Match']).toBe('"4"');
    expect(cancelled.ok && cancelled.value.status).toBe('Cancelled');
  });

  it('downloads only the API content path and hands the Blob to the browser', async () => {
    const blob = new Blob(['PK']);
    const get = vi.fn(() => of(new HttpResponse({ body: blob, status: 200 })));
    const { adapter: subject, saver } = adapter({}, { get });
    const job = mapJob(view);
    expect((await subject.download(job)).ok).toBe(true);
    expect(get).toHaveBeenCalledWith(view.contentUrl, {
      observe: 'response',
      responseType: 'blob',
    });
    expect(saver.save).toHaveBeenCalledWith(blob, 'orcamento-FY27.xlsx');

    const hostile: ReportJob = { ...job, contentUrl: 'https://storage.example.invalid/blob?sig=x' };
    const refused = await subject.download(hostile);
    expect(refused.ok).toBe(false);
    expect(get).toHaveBeenCalledTimes(1);
  });

  it('reads Problem Details from a Blob error body (410 report.expired, 503 file.unavailable)', async () => {
    const problem = (status: number, code: string) =>
      new HttpErrorResponse({
        status,
        headers: new HttpHeaders(),
        error: new Blob([JSON.stringify({ code, ruleId: 'RPT-006' })], {
          type: 'application/problem+json',
        }),
      });
    let status = 410;
    let code = 'report.expired';
    const { adapter: subject } = adapter(
      {},
      { get: () => throwError(() => problem(status, code)) },
    );
    const expired = await subject.download(mapJob(view));
    expect(expired.ok).toBe(false);
    if (!expired.ok)
      expect(expired.error).toMatchObject({
        kind: 'gone',
        code: 'report.expired',
        ruleId: 'RPT-006',
      });
    status = 503;
    code = 'file.unavailable';
    const unavailable = await subject.download(mapJob(view));
    if (!unavailable.ok)
      expect(unavailable.error).toMatchObject({ kind: 'unavailable', code: 'file.unavailable' });
  });

  it('keeps only catalog types the client knows', async () => {
    const { adapter: subject } = adapter({
      getApiV2ReportJobsTypes: () =>
        of([
          {
            type: 'RequisitionPdf',
            format: 'Pdf',
            available: true,
            requiredPermissions: ['reports.request'],
            parameters: ['requisitionId'],
          },
          { type: 'PurchaseOrderPdf', format: 'Pdf', available: true },
        ]),
    });
    const types = await subject.types();
    expect(types.ok && types.value.map((item) => item.type)).toEqual(['RequisitionPdf']);
  });
});
