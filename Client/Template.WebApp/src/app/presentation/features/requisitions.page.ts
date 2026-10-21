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
  template: `
    <header class="page-title">
      <p>{{ 'shell.breadcrumbHome' | transloco }} / {{ titleKey | transloco }}</p>
      <h1>{{ titleKey | transloco }}</h1>
    </header>
    <div class="toolbar">
      <input
        hlmInput
        [value]="query()"
        (input)="query.set($any($event.target).value)"
        [placeholder]="'requisition.searchNumber' | transloco"
        [attr.aria-label]="'requisition.searchNumber' | transloco"
      />
      <label for="statusFilter" class="sr-only">{{ 'fields.status' | transloco }}</label>
      <select
        id="statusFilter"
        class="select"
        [value]="status()"
        (change)="status.set($any($event.target).value)"
      >
        @for (option of statuses; track option) {
          <option [value]="option">
            {{ (option ? 'statuses.' + option : 'requisition.allStatuses') | transloco }}
          </option>
        }
      </select>
      <button hlmBtn variant="outline" type="button" (click)="search()">
        {{ 'common.search' | transloco }}
      </button>
      <a *appPermission="'requisitions.create'" hlmBtn routerLink="/requisitions/new">{{
        'requisition.new' | transloco
      }}</a>
    </div>
    <app-problem-feedback [error]="error()" (reload)="load()" />
    @if (loading()) {
      <p role="status">{{ 'common.loading' | transloco }}</p>
    } @else if (page().items.length === 0) {
      <div class="empty">
        <h2>{{ 'common.empty' | transloco }}</h2>
        <p>{{ 'requisition.emptyHelp' | transloco }}</p>
      </div>
    } @else {
      <div class="table-scroll" role="region" tabindex="0" [attr.aria-label]="titleKey | transloco">
        <table class="responsive-table">
          <thead>
            <tr>
              <th>{{ 'requisition.number' | transloco }}</th>
              <th>{{ 'requisition.costCenter' | transloco }}</th>
              <th>{{ 'requisition.neededBy' | transloco }}</th>
              <th>{{ 'requisition.total' | transloco }}</th>
              <th>{{ 'fields.status' | transloco }}</th>
              <th>{{ 'common.actions' | transloco }}</th>
            </tr>
          </thead>
          <tbody>
            @for (item of page().items; track item.id) {
              <tr>
                <td [attr.data-label]="'requisition.number' | transloco">{{ item.number }}</td>
                <td [attr.data-label]="'requisition.costCenter' | transloco">
                  {{ item.costCenter.code }} — {{ item.costCenter.name }}
                </td>
                <td [attr.data-label]="'requisition.neededBy' | transloco">
                  {{ item.neededBy ?? '—' }}
                </td>
                <td [attr.data-label]="'requisition.total' | transloco">
                  {{ item.estimatedTotal | money }}
                </td>
                <td [attr.data-label]="'fields.status' | transloco">
                  <app-status-badge [status]="item.status" />
                </td>
                <td>
                  <a hlmBtn size="sm" variant="outline" [routerLink]="['/requisitions', item.id]">{{
                    'common.details' | transloco
                  }}</a>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <nav class="pager" [attr.aria-label]="'common.pagination' | transloco">
        <button
          hlmBtn
          variant="outline"
          type="button"
          [disabled]="page().page <= 1"
          (click)="move(-1)"
        >
          ←
        </button>
        <span>{{ page().page }} / {{ page().totalPages || 1 }}</span>
        <button
          hlmBtn
          variant="outline"
          type="button"
          [disabled]="page().page >= page().totalPages"
          (click)="move(1)"
        >
          →
        </button>
      </nav>
    }
  `,
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
