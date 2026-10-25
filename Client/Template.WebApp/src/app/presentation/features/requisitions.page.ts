import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import type { Page } from '@application/master-data/master-data';
import {
  RequisitionUseCases,
  type RequisitionScope,
  type RequisitionSummary,
} from '@application/procurement/procurement';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { PermissionDirective } from '../security/permission.directive';
import { MoneyPipe, ProblemFeedback, StatusBadge } from '../shared/procurement-ui';
import { REQUISITION_PORT } from '../state/ports';

const STATUSES = ['', 'Draft', 'Submitted', 'Cancelled', 'Expired', 'Rejected'] as const;

/** My requisitions (`scope=mine`) and team requisitions (`scope=visible`, REQ-011) with server paging and filters. */
@Component({
  selector: 'app-requisitions-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    TranslocoPipe,
    HlmButton,
    HlmInput,
    PermissionDirective,
    MoneyPipe,
    ProblemFeedback,
    StatusBadge,
  ],
  templateUrl: './requisitions.page.html',
  styleUrl: './data-pages.css',
})
export class RequisitionsPage {
  private readonly useCases = new RequisitionUseCases(inject(REQUISITION_PORT));
  private readonly scope: RequisitionScope =
    inject(ActivatedRoute).snapshot.data['scope'] === 'visible' ? 'visible' : 'mine';
  readonly titleKey = this.scope === 'mine' ? 'nav.myRequisitions' : 'nav.teamRequisitions';
  readonly statuses = STATUSES;
  readonly page = signal<Page<RequisitionSummary>>({
    items: [],
    page: 1,
    pageSize: 20,
    totalItems: 0,
    totalPages: 0,
  });
  readonly loading = signal(true);
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly query = signal('');
  readonly status = signal('');

  constructor() {
    void this.load();
  }

  async load() {
    this.loading.set(true);
    const result = await this.useCases.list({
      scope: this.scope,
      page: this.page().page,
      pageSize: 20,
      q: this.query().trim(),
      status: this.status(),
      sort: '-createdAt',
    });
    this.loading.set(false);
    if (result.ok) {
      this.page.set(result.value);
      this.error.set(undefined);
    } else this.error.set(result.error);
  }

  search() {
    this.page.update((value) => ({ ...value, page: 1 }));
    void this.load();
  }

  move(delta: number) {
    this.page.update((value) => ({ ...value, page: value.page + delta }));
    void this.load();
  }
}
