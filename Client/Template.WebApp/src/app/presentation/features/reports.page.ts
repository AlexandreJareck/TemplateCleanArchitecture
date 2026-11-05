import { DOCUMENT } from '@angular/common';
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
import { EntityPicker, type EntityOption } from '../shared/entity-picker';
import { OrganizationContext } from '../state/organization.context';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { ProblemFeedback, StatusBadge } from '../shared/procurement-ui';
import { SessionFacade } from '../state/session.facade';
import {
  BUDGET_PORT,
  MASTER_DATA_PORT,
  REPORT_PORT,
  REQUISITION_PORT,
  SOURCING_PORT,
} from '../state/ports';
import { HlmNativeSelectImports } from '@presentation/shared-ui/native-select';
import { HlmTableImports } from '@presentation/shared-ui/table';
import { InstantPipe } from '../shared/display';

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
  imports: [
    InstantPipe,
    ...HlmTableImports,
    ...HlmNativeSelectImports,
    TranslocoPipe,
    HlmButton,
    HlmInput,
    ProblemFeedback,
    StatusBadge,
    EntityPicker,
  ],
  templateUrl: './reports.page.html',
  styleUrl: './data-pages.css',
})
export class ReportsPage {
  private readonly reports = inject(REPORT_PORT);
  private readonly budget = inject(BUDGET_PORT);
  private readonly requisitions = inject(REQUISITION_PORT);
  private readonly sourcing = inject(SOURCING_PORT);
  private readonly masterData = inject(MASTER_DATA_PORT);
  protected readonly organization = inject(OrganizationContext);
  private readonly document = inject(DOCUMENT);
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
  readonly submitted = signal(false);
  readonly typeInfo = computed(() => this.catalog().find((item) => item.type === this.type()));
  readonly problems = computed(() =>
    ReportUseCases.validate(this.type(), this.typeInfo(), this.values()),
  );
  readonly invalid = computed(() => Object.keys(this.problems()).length > 0);
  /** API parameter name -> visible label, so server field problems name the field people see. */
  readonly fieldLabels: Readonly<Record<string, string>> = {
    requisitionId: 'reports.requisitionId',
    rfqId: 'reports.rfqId',
    fiscalPeriodId: 'reports.fiscalPeriod',
    costCenterId: 'reports.costCenterId',
    currency: 'reports.currency',
    from: 'reports.from',
    to: 'reports.to',
    supplierId: 'reports.supplierId',
    reportType: 'reports.type',
  };

  readonly searchRequisitions = async (q: string): Promise<readonly EntityOption[]> => {
    const result = await this.requisitions.list({
      scope: 'visible',
      page: 1,
      pageSize: 20,
      q: q || undefined,
      sort: '-createdAt',
    });
    return result.ok
      ? result.value.items.map((item) => ({
          id: item.id,
          label: `${item.number} — ${item.costCenter.code} · ${this.transloco.translate(`statuses.${item.status}`)}`,
        }))
      : [];
  };
  readonly resolveRequisition = async (id: string): Promise<EntityOption | undefined> => {
    const result = await this.requisitions.get(id);
    return result.ok
      ? { id, label: `${result.value.number} — ${result.value.costCenter.code}` }
      : undefined;
  };
  readonly searchRfqs = async (q: string): Promise<readonly EntityOption[]> => {
    const result = await this.sourcing.list({ page: 1, pageSize: 20, q: q || undefined });
    return result.ok
      ? result.value.items.map((item) => ({
          id: item.id,
          label: `${item.number} — ${item.requisitionNumber} · ${this.transloco.translate(`statuses.${item.status}`)}`,
        }))
      : [];
  };
  readonly resolveRfq = async (id: string): Promise<EntityOption | undefined> => {
    const result = await this.sourcing.get(id);
    return result.ok ? { id, label: result.value.number } : undefined;
  };
  readonly searchCostCenters = async (q: string): Promise<readonly EntityOption[]> => {
    const result = await this.masterData.list('cost-centers', {
      page: 1,
      pageSize: 20,
      q: q || undefined,
      sort: 'code',
    });
    return result.ok
      ? result.value.items.map((item) => ({ id: item.id, label: `${item.code} — ${item.name}` }))
      : [];
  };
  readonly searchSuppliers = async (q: string): Promise<readonly EntityOption[]> => {
    const result = await this.masterData.listSuppliers({
      page: 1,
      pageSize: 20,
      q: q || undefined,
      sort: 'legalName',
    });
    return result.ok
      ? result.value.items.map((item) => ({
          id: item.id,
          label: `${item.legalName} (${item.taxId})`,
        }))
      : [];
  };

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

  changeType(type: ReportType) {
    this.type.set(type);
    this.submitted.set(false);
  }

  isRequired(name: keyof ReportParameters) {
    return ReportUseCases.requiredParameters(this.type(), this.typeInfo()).includes(name);
  }

  /** Localized inline message once a submission was attempted. */
  fieldError(name: keyof ReportParameters): string | undefined {
    if (!this.submitted()) return undefined;
    const problem = this.problems()[name];
    return problem ? this.transloco.translate(`reports.problems.${problem}`) : undefined;
  }

  async submit() {
    this.submitted.set(true);
    if (this.invalid()) {
      this.error.set(undefined);
      const first = Object.keys(this.problems())[0];
      if (first) this.document.getElementById(first)?.focus();
      return;
    }
    this.busy.set(true);
    const result = await this.reports.request({
      reportType: this.type(),
      parameters: ReportUseCases.parametersFor(this.type(), this.values()),
    });
    this.busy.set(false);
    if (result.ok) this.submitted.set(false);
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
