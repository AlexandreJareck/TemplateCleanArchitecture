import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import type { Page } from '@application/master-data/master-data';
import type { FiscalPeriod } from '@application/procurement/procurement';
import {
  ReportUseCases,
  REPORT_TYPES,
  type PollingClock,
  type ReportJob,
  type ReportParameters,
  type ReportType,
  type ReportTypeInfo,
} from '@application/reports/reports';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { ProblemFeedback, StatusBadge } from '../shared/procurement-ui';
import { SessionFacade } from '../state/session.facade';
import { BUDGET_PORT, REPORT_PORT } from '../state/ports';

const EMPTY: Page<ReportJob> = { items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 };

/** Browser timers for the Application polling policy; aborting a sleep rejects it. */
const BROWSER_CLOCK: PollingClock = {
  now: () => Date.now(),
  sleep: (ms, signal) =>
    new Promise<void>((resolve, reject) => {
      if (signal?.aborted) return reject(new Error('aborted'));
      const timer = setTimeout(resolve, ms);
      signal?.addEventListener(
        'abort',
        () => {
          clearTimeout(timer);
          reject(new Error('aborted'));
        },
        { once: true },
      );
    }),
};

/**
 * Report center (Phase 30, `/reports`): request a catalog report with the parameters of its type, follow the job with
 * bounded backoff polling (2 s doubling up to 30 s, "still running" after 10 minutes), download Ready outputs through
 * the API, cancel pending jobs and retry failed, expired or cancelled ones. Status changes are announced politely;
 * permissions, parameters and every rule are decided by the API.
 */
@Component({
  selector: 'app-reports-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, HlmButton, HlmInput, ProblemFeedback, StatusBadge],
  template: `
    <header class="page-title">
      <p>{{ 'shell.breadcrumbHome' | transloco }} / {{ 'nav.reports' | transloco }}</p>
      <h1>{{ 'nav.reports' | transloco }}</h1>
    </header>
    <p class="sr-only" aria-live="polite" role="status">{{ announcement() }}</p>
    <app-problem-feedback [error]="error()" (reload)="load()" />

    <section class="panel" aria-labelledby="request-title">
      <h2 id="request-title">{{ 'reports.request' | transloco }}</h2>
      @if (availableTypes().length === 0) {
        <p class="empty">{{ 'reports.noTypes' | transloco }}</p>
      } @else {
        <form class="form-grid" (submit)="$event.preventDefault(); submit()">
          <label for="reportType"
            >{{ 'reports.type' | transloco }}
            <select
              id="reportType"
              class="select"
              [value]="type()"
              (change)="type.set($any($event.target).value)"
            >
              @for (option of availableTypes(); track option.type) {
                <option [value]="option.type">
                  {{ 'reports.types.' + option.type | transloco }} ({{ option.format }})
                </option>
              }
            </select>
          </label>
          @switch (type()) {
            @case ('RequisitionPdf') {
              <label for="requisitionId"
                >{{ 'reports.requisitionId' | transloco }}
                <input
                  id="requisitionId"
                  hlmInput
                  [value]="values().requisitionId ?? ''"
                  (input)="set('requisitionId', $any($event.target).value)"
                />
              </label>
            }
            @case ('QuoteComparisonPdf') {
              <label for="rfqId"
                >{{ 'reports.rfqId' | transloco }}
                <input
                  id="rfqId"
                  hlmInput
                  [value]="values().rfqId ?? ''"
                  (input)="set('rfqId', $any($event.target).value)"
                />
              </label>
            }
            @case ('BudgetConsumptionXlsx') {
              <label for="fiscalPeriodId"
                >{{ 'reports.fiscalPeriod' | transloco }}
                <select
                  id="fiscalPeriodId"
                  class="select"
                  [value]="values().fiscalPeriodId ?? ''"
                  (change)="set('fiscalPeriodId', $any($event.target).value)"
                >
                  <option value="">—</option>
                  @for (period of periods(); track period.id) {
                    <option [value]="period.id">{{ period.code }} — {{ period.name }}</option>
                  }
                </select>
              </label>
              <label for="currency"
                >{{ 'reports.currency' | transloco }}
                <input
                  id="currency"
                  hlmInput
                  maxlength="3"
                  [value]="values().currency ?? ''"
                  (input)="set('currency', $any($event.target).value)"
                />
              </label>
              <label for="costCenterId"
                >{{ 'reports.costCenterId' | transloco }}
                <input
                  id="costCenterId"
                  hlmInput
                  [value]="values().costCenterId ?? ''"
                  (input)="set('costCenterId', $any($event.target).value)"
                />
              </label>
            }
            @case ('SupplierQuotesXlsx') {
              <label for="from"
                >{{ 'reports.from' | transloco }}
                <input
                  id="from"
                  hlmInput
                  type="date"
                  [value]="values().from ?? ''"
                  (input)="set('from', $any($event.target).value)"
                />
              </label>
              <label for="to"
                >{{ 'reports.to' | transloco }}
                <input
                  id="to"
                  hlmInput
                  type="date"
                  [value]="values().to ?? ''"
                  (input)="set('to', $any($event.target).value)"
                />
              </label>
              <label for="supplierId"
                >{{ 'reports.supplierId' | transloco }}
                <input
                  id="supplierId"
                  hlmInput
                  [value]="values().supplierId ?? ''"
                  (input)="set('supplierId', $any($event.target).value)"
                />
              </label>
            }
          }
          <button hlmBtn type="submit" [disabled]="busy()">
            {{ 'reports.submit' | transloco }}
          </button>
        </form>
        <p class="hint">{{ 'reports.asOfHelp' | transloco }}</p>
      }
    </section>

    <section class="panel" aria-labelledby="jobs-title">
      <h2 id="jobs-title">{{ 'reports.jobs' | transloco }}</h2>
      <div class="toolbar">
        <button hlmBtn variant="outline" size="sm" type="button" (click)="load()">
          {{ 'common.refresh' | transloco }}
        </button>
      </div>
      @if (jobs().items.length === 0) {
        <p class="empty">{{ 'reports.noJobs' | transloco }}</p>
      } @else {
        <div
          class="table-scroll"
          role="region"
          tabindex="0"
          [attr.aria-label]="'reports.jobsTable' | transloco"
        >
          <table class="responsive-table">
            <thead>
              <tr>
                <th>{{ 'reports.type' | transloco }}</th>
                <th>{{ 'reports.requestedAt' | transloco }}</th>
                <th>{{ 'fields.status' | transloco }}</th>
                <th>{{ 'reports.progress' | transloco }}</th>
                <th>{{ 'reports.details' | transloco }}</th>
                <th>{{ 'common.actions' | transloco }}</th>
              </tr>
            </thead>
            <tbody>
              @for (job of jobs().items; track job.id) {
                <tr [attr.data-job]="job.id">
                  <td [attr.data-label]="'reports.type' | transloco">
                    {{ 'reports.types.' + job.reportType | transloco }}
                  </td>
                  <td [attr.data-label]="'reports.requestedAt' | transloco">
                    {{ instant(job.requestedAt) }}
                  </td>
                  <td [attr.data-label]="'fields.status' | transloco">
                    <app-status-badge [status]="job.status" />
                  </td>
                  <td [attr.data-label]="'reports.progress' | transloco">
                    @if (isActive(job)) {
                      <progress
                        max="100"
                        [value]="job.progress"
                        [attr.aria-label]="'reports.progress' | transloco"
                      ></progress>
                      {{ job.progress }}%
                      @if (stillRunning().has(job.id)) {
                        <span class="notice">{{ 'reports.stillRunning' | transloco }}</span>
                      }
                    } @else {
                      {{ job.attempts }}/{{ job.maxAttempts }}
                    }
                  </td>
                  <td [attr.data-label]="'reports.details' | transloco">
                    @if (job.status === 'Failed') {
                      <span class="warning">{{ failureKey(job.failureCode) | transloco }}</span>
                      <small> ({{ job.failureCode }})</small>
                    } @else if (job.status === 'Ready') {
                      {{ job.fileName }} · {{ 'reports.expiresAt' | transloco }}
                      {{ instant(job.expiresAt) }}
                    } @else if (job.status === 'Expired') {
                      {{ 'reports.expired' | transloco }}
                    } @else {
                      {{ 'reports.asOf' | transloco }} {{ instant(job.asOf) }}
                    }
                  </td>
                  <td class="actions">
                    @if (can(job, 'download')) {
                      <button hlmBtn size="sm" type="button" (click)="download(job)">
                        {{ 'reports.download' | transloco }}
                      </button>
                    }
                    @if (can(job, 'cancel')) {
                      <button
                        hlmBtn
                        size="sm"
                        variant="outline"
                        type="button"
                        (click)="cancel(job)"
                      >
                        {{ 'common.cancel' | transloco }}
                      </button>
                    }
                    @if (can(job, 'retry')) {
                      <button hlmBtn size="sm" variant="outline" type="button" (click)="retry(job)">
                        {{ 'reports.retry' | transloco }}
                      </button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  `,
  styleUrl: './data-pages.css',
})
export class ReportsPage {
  private readonly reports = inject(REPORT_PORT);
  private readonly budget = inject(BUDGET_PORT);
  private readonly transloco = inject(TranslocoService);
  private readonly route = inject(ActivatedRoute);
  private readonly pollers = new Map<string, AbortController>();
  protected readonly session = inject(SessionFacade);

  readonly catalog = signal<readonly ReportTypeInfo[]>([]);
  readonly availableTypes = computed(() => this.catalog().filter((item) => item.available));
  readonly type = signal<ReportType>('RequisitionPdf');
  readonly values = signal<ReportParameters>({});
  readonly periods = signal<readonly FiscalPeriod[]>([]);
  readonly jobs = signal<Page<ReportJob>>(EMPTY);
  readonly stillRunning = signal<ReadonlySet<string>>(new Set());
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly announcement = signal('');
  readonly busy = signal(false);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopPolling());
    const query = this.route.snapshot.queryParamMap;
    const requested = query.get('type');
    if (requested && (REPORT_TYPES as readonly string[]).includes(requested))
      this.type.set(requested as ReportType);
    this.values.set({
      requisitionId: query.get('requisitionId') ?? undefined,
      rfqId: query.get('rfqId') ?? undefined,
    });
    void this.load();
  }

  async load() {
    const [types, jobs] = await Promise.all([
      this.reports.types(),
      this.reports.list({ page: 1, pageSize: 50 }),
    ]);
    if (!types.ok) return this.error.set(types.error);
    if (!jobs.ok) return this.error.set(jobs.error);
    this.error.set(undefined);
    this.catalog.set(types.value);
    const available = types.value.filter((item) => item.available);
    if (!available.some((item) => item.type === this.type()) && available[0])
      this.type.set(available[0].type);
    this.jobs.set(jobs.value);
    if (available.some((item) => item.type === 'BudgetConsumptionXlsx')) {
      const periods = await this.budget.listPeriods({ page: 1, pageSize: 100 });
      if (periods.ok) this.periods.set(periods.value.items);
    }
    for (const job of jobs.value.items) if (ReportUseCases.isActive(job)) this.follow(job);
  }

  set(key: keyof ReportParameters, value: string) {
    this.values.update((current) => ({ ...current, [key]: value }));
  }

  async submit() {
    this.busy.set(true);
    const result = await this.reports.request({
      reportType: this.type(),
      parameters: ReportUseCases.parametersFor(this.type(), this.values()),
    });
    this.busy.set(false);
    this.accept(result);
  }

  async retry(job: ReportJob) {
    this.accept(await this.reports.request(ReportUseCases.retryRequest(job)));
  }

  async cancel(job: ReportJob) {
    const result = await this.reports.cancel(job);
    if (!result.ok) return this.error.set(result.error);
    this.error.set(undefined);
    this.replace(result.value);
    this.announce(result.value);
  }

  async download(job: ReportJob) {
    const result = await this.reports.download(job);
    if (!result.ok) {
      this.error.set(result.error);
      if (result.error.kind === 'gone') void this.refresh(job.id);
      return;
    }
    this.error.set(undefined);
    this.announcement.set(
      this.transloco.translate('reports.downloaded', { name: job.fileName ?? '' }),
    );
  }

  can(job: ReportJob, action: 'cancel' | 'download' | 'retry') {
    return ReportUseCases.can(job, action);
  }

  isActive(job: ReportJob) {
    return ReportUseCases.isActive(job);
  }

  failureKey(code: string | undefined) {
    return ReportUseCases.failureKey(code);
  }

  instant(value: string | undefined) {
    if (!value) return '—';
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? value
      : new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
          dateStyle: 'short',
          timeStyle: 'medium',
        }).format(date);
  }

  private accept(result: Awaited<ReturnType<typeof this.reports.request>>) {
    if (!result.ok) return this.error.set(result.error);
    this.error.set(undefined);
    const job = result.value;
    this.jobs.update((page) =>
      page.items.some((item) => item.id === job.id)
        ? { ...page, items: page.items.map((item) => (item.id === job.id ? job : item)) }
        : { ...page, items: [job, ...page.items], totalItems: page.totalItems + 1 },
    );
    this.announce(job);
    this.follow(job);
  }

  private follow(job: ReportJob) {
    if (!ReportUseCases.isActive(job) || this.pollers.has(job.id)) return;
    const controller = new AbortController();
    this.pollers.set(job.id, controller);
    void ReportUseCases.poll(
      this.reports,
      job,
      BROWSER_CLOCK,
      (next) => this.replace(next),
      controller.signal,
    ).then((outcome) => {
      this.pollers.delete(job.id);
      if (outcome.kind === 'finished') this.announce(outcome.job);
      if (outcome.kind === 'still-running')
        this.stillRunning.update((ids) => new Set([...ids, outcome.job.id]));
      if (outcome.kind === 'error' && !outcome.result.ok) this.error.set(outcome.result.error);
    });
  }

  private async refresh(id: string) {
    const result = await this.reports.get(id);
    if (result.ok) this.replace(result.value);
  }

  private replace(job: ReportJob) {
    this.jobs.update((page) => ({
      ...page,
      items: page.items.map((item) => (item.id === job.id ? job : item)),
    }));
  }

  private announce(job: ReportJob) {
    this.announcement.set(
      this.transloco.translate('reports.announce', {
        type: this.transloco.translate(`reports.types.${job.reportType}`),
        status: this.transloco.translate(`statuses.${job.status}`),
      }),
    );
  }

  private stopPolling() {
    for (const controller of this.pollers.values()) controller.abort();
    this.pollers.clear();
  }
}
