import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AuditUseCases, type AuditQuery, type AuditRecord } from '@application/auditing/audit';
import type { ApplicationError } from '@application/common/application-error';
import type { Page } from '@application/master-data/master-data';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { HlmNativeSelectImports } from '@presentation/shared-ui/native-select';
import { HlmTableImports } from '@presentation/shared-ui/table';
import { nextLocalDate, startOfLocalDayUtc } from '../../domain/date-display';
import { ActorName, InstantPipe } from '../shared/display';
import { ProblemFeedback, StatusBadge } from '../shared/procurement-ui';
import { OrganizationContext } from '../state/organization.context';
import { AUDIT_PORT } from '../state/ports';

const PAGE_SIZE = 20;
const EMPTY: Page<AuditRecord> = {
  items: [],
  page: 1,
  pageSize: PAGE_SIZE,
  totalItems: 0,
  totalPages: 0,
};

interface AuditFilters {
  action: string;
  resourceType: string;
  resourceId: string;
  actorId: string;
  fromDate: string;
  toDate: string;
  sort: '-occurredAt' | 'occurredAt';
}

const NO_FILTERS: AuditFilters = {
  action: '',
  resourceType: '',
  resourceId: '',
  actorId: '',
  fromDate: '',
  toDate: '',
  sort: '-occurredAt',
};

/**
 * Global audit search (`/audit`, `audit.read`, AUD-004): read-only, paged, newest first, filtered by action, resource,
 * actor and a business-date range converted to UTC bounds in the organization time zone. Only the server's
 * allow-listed details are shown (AUD-003); there is no edit or delete affordance (AUD-002, SOD-8).
 */
@Component({
  selector: 'app-audit-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ...HlmTableImports,
    ...HlmNativeSelectImports,
    TranslocoPipe,
    HlmButton,
    HlmInput,
    ProblemFeedback,
    StatusBadge,
    ActorName,
    InstantPipe,
  ],
  templateUrl: './audit.page.html',
  styleUrls: ['./data-pages.css', './audit.page.css'],
})
export class AuditPage {
  private readonly port = inject(AUDIT_PORT);
  private readonly organization = inject(OrganizationContext);
  private readonly transloco = inject(TranslocoService);
  readonly records = signal<Page<AuditRecord>>(EMPTY);
  readonly loading = signal(true);
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly draft = signal<AuditFilters>({ ...NO_FILTERS });
  readonly applied = signal<AuditFilters>({ ...NO_FILTERS });
  readonly page = signal(1);
  readonly problems = computed(() => AuditUseCases.validate(this.draft()));
  readonly fieldLabels: Readonly<Record<string, string>> = {
    action: 'audit.action',
    actorId: 'audit.actorId',
    resourceType: 'audit.resourceType',
    resourceId: 'audit.resourceId',
    from: 'audit.from',
    to: 'audit.to',
  };

  constructor() {
    void this.load();
  }

  set(key: keyof AuditFilters, value: string) {
    this.draft.update((current) => ({ ...current, [key]: value }));
  }

  problem(key: 'actorId' | 'toDate') {
    const problem = this.problems()[key];
    return problem ? this.transloco.translate(`audit.problems.${problem}`) : undefined;
  }

  search(event: Event) {
    event.preventDefault();
    if (Object.keys(this.problems()).length > 0) return;
    this.applied.set({ ...this.draft() });
    this.page.set(1);
    void this.load();
  }

  clear() {
    this.draft.set({ ...NO_FILTERS });
    this.applied.set({ ...NO_FILTERS });
    this.page.set(1);
    void this.load();
  }

  move(delta: number) {
    this.page.update((value) => Math.max(1, value + delta));
    void this.load();
  }

  async load() {
    this.loading.set(true);
    const result = await this.port.list(this.query());
    this.loading.set(false);
    if (!result.ok) {
      this.error.set(result.error);
      return;
    }
    this.error.set(undefined);
    this.records.set(result.value);
  }

  /** Allow-listed detail values of a record, labelled by a known key when there is one. */
  detailLabel(key: string) {
    const translated = this.transloco.translate(`audit.detailKeys.${key}`);
    return translated === `audit.detailKeys.${key}` ? key : translated;
  }

  private query(): AuditQuery {
    const filters = this.applied();
    const zone = this.organization.timeZone();
    const toDay = filters.toDate ? nextLocalDate(filters.toDate) : undefined;
    return AuditUseCases.compact({
      page: this.page(),
      pageSize: PAGE_SIZE,
      sort: filters.sort,
      action: filters.action,
      resourceType: filters.resourceType,
      resourceId: filters.resourceId,
      actorId: filters.actorId,
      from: filters.fromDate ? startOfLocalDayUtc(filters.fromDate, zone) : undefined,
      to: toDay ? startOfLocalDayUtc(toDay, zone) : undefined,
    });
  }
}
