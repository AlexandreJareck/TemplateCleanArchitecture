import { describe, expect, it } from 'vitest';
import { failure, success } from '../common/result';
import { POLLING, ReportUseCases, type PollingClock, type ReportJob } from './reports';

function job(overrides: Partial<ReportJob> = {}): ReportJob {
  return {
    id: 'job-1',
    reportType: 'RequisitionPdf',
    format: 'Pdf',
    status: 'Pending',
    parameters: { requisitionId: 'req-1' },
    requestedAt: '2027-03-01T13:00:00Z',
    asOf: '2027-03-01T13:00:00Z',
    timeZone: 'America/Sao_Paulo',
    attempts: 0,
    maxAttempts: 3,
    progress: 0,
    allowedActions: ['cancel'],
    version: '1',
    ...overrides,
  };
}

/** Deterministic clock: sleeping advances virtual time and records every delay. */
function clock() {
  const delays: number[] = [];
  let now = 0;
  const value: PollingClock & { delays: number[] } = {
    delays,
    now: () => now,
    sleep: async (ms, signal) => {
      if (signal?.aborted) throw new Error('aborted');
      delays.push(ms);
      now += ms;
    },
  };
  return value;
}

describe('ReportUseCases', () => {
  it('backs off from 2 s doubling up to 30 s', () => {
    expect([0, 1, 2, 3, 4, 5, 9].map((attempt) => ReportUseCases.pollDelay(attempt))).toEqual([
      2_000, 4_000, 8_000, 16_000, 30_000, 30_000, 30_000,
    ]);
  });

  it('polls until the job leaves Pending/Running and reports every update', async () => {
    const states = [
      job({ status: 'Running', progress: 40 }),
      job({ status: 'Ready', progress: 100, allowedActions: ['download'] }),
    ];
    const updates: ReportJob[] = [];
    const time = clock();
    const outcome = await ReportUseCases.poll(
      { get: async () => success(states.shift()!) },
      job(),
      time,
      (next) => updates.push(next),
    );
    expect(outcome.kind).toBe('finished');
    expect(updates.map((item) => item.status)).toEqual(['Running', 'Ready']);
    expect(time.delays).toEqual([2_000, 4_000]);
  });

  it('gives up with "still running" after ten minutes without stopping the job', async () => {
    const time = clock();
    const outcome = await ReportUseCases.poll(
      { get: async () => success(job({ status: 'Running' })) },
      job(),
      time,
      () => {},
    );
    expect(outcome.kind).toBe('still-running');
    expect(time.now()).toBeGreaterThanOrEqual(POLLING.giveUpAfterMs);
    expect(time.delays.length).toBeLessThan(30);
  });

  it('stops on abort and surfaces errors', async () => {
    const controller = new AbortController();
    controller.abort();
    expect(
      (
        await ReportUseCases.poll(
          { get: async () => success(job()) },
          job(),
          clock(),
          () => {},
          controller.signal,
        )
      ).kind,
    ).toBe('cancelled');
    const error = await ReportUseCases.poll(
      { get: async () => failure({ kind: 'not-found', code: 'not-found' }) },
      job(),
      clock(),
      () => {},
    );
    expect(error.kind).toBe('error');
    const cancelled = await ReportUseCases.poll(
      { get: async () => failure({ kind: 'cancelled', code: 'request.cancelled' }) },
      job(),
      clock(),
      () => {},
    );
    expect(cancelled.kind).toBe('cancelled');
    expect(
      (
        await ReportUseCases.poll(
          { get: async () => success(job()) },
          job({ status: 'Ready' }),
          clock(),
          () => {},
        )
      ).kind,
    ).toBe('finished');
  });

  it('keeps only the parameters of the chosen type and never resends pinned versions', () => {
    const values = {
      requisitionId: ' req-1 ',
      rfqId: 'rfq-1',
      fiscalPeriodId: '',
      currency: 'BRL',
      from: '2027-01-01',
    };
    expect(ReportUseCases.parametersFor('RequisitionPdf', values)).toEqual({
      requisitionId: 'req-1',
    });
    expect(ReportUseCases.parametersFor('BudgetConsumptionXlsx', values)).toEqual({
      currency: 'BRL',
    });
    expect(ReportUseCases.parametersFor('SupplierQuotesXlsx', values)).toEqual({
      from: '2027-01-01',
    });
    const failed = job({
      reportType: 'QuoteComparisonPdf',
      status: 'Failed',
      parameters: { rfqId: 'rfq-1', comparisonSequence: 2 },
    });
    expect(ReportUseCases.retryRequest(failed)).toEqual({
      reportType: 'QuoteComparisonPdf',
      parameters: { rfqId: 'rfq-1' },
    });
  });

  it('maps safe failure codes and server actions', () => {
    expect(ReportUseCases.failureKey('report.too-large')).toBe('reports.failures.too-large');
    expect(ReportUseCases.failureKey('System.Exception: boom')).toBe('reports.failures.unexpected');
    expect(ReportUseCases.failureKey(undefined)).toBe('reports.failures.unexpected');
    expect(ReportUseCases.can(job(), 'cancel')).toBe(true);
    expect(ReportUseCases.can(job(), 'download')).toBe(false);
    expect(ReportUseCases.can(undefined, 'retry')).toBe(false);
    expect(ReportUseCases.isActive(job({ status: 'Running' }))).toBe(true);
    expect(ReportUseCases.isActive(job({ status: 'Expired' }))).toBe(false);
  });
});
