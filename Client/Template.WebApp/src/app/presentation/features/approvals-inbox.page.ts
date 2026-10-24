import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  ApprovalUseCases,
  type ApprovalSummary,
  type DueState,
} from '@application/approvals/approvals';
import type { ApplicationError } from '@application/common/application-error';
import type { Page } from '@application/master-data/master-data';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { MoneyPipe, ProblemFeedback } from '../shared/procurement-ui';
import { APPROVAL_PORT } from '../state/ports';

const EMPTY: Page<ApprovalSummary> = {
  items: [],
  page: 1,
  pageSize: 20,
  totalItems: 0,
  totalPages: 0,
};

/**
 * Approval inbox (Phase 31, `/approvals/inbox`): pending steps assigned to the caller or to one of the caller's role
 * groups, ordered by due time, with the escalation and deadline state the server computed. Opening a row leads to the
 * detail, where the server-computed `allowedActions` decide what can be done.
 */
@Component({
  selector: 'app-approvals-inbox-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, RouterLink, HlmButton, MoneyPipe, ProblemFeedback],
  template: `
    <header class="page-title">
      <p>{{ 'shell.breadcrumbHome' | transloco }} / {{ 'nav.approvals' | transloco }}</p>
      <h1>{{ 'approvals.inbox' | transloco }}</h1>
    </header>
    <app-problem-feedback [error]="error()" (reload)="load()" />

    <section class="panel" aria-labelledby="inbox-title">
      <h2 id="inbox-title">{{ 'approvals.pending' | transloco }}</h2>
      <div class="toolbar">
        <label for="escalatedOnly">
          <input
            id="escalatedOnly"
            type="checkbox"
            [checked]="escalatedOnly()"
            (change)="toggleEscalated($any($event.target).checked)"
          />
          {{ 'approvals.escalatedOnly' | transloco }}
        </label>
        <button hlmBtn variant="outline" size="sm" type="button" (click)="load()">
          {{ 'common.refresh' | transloco }}
        </button>
      </div>
      @if (items().items.length === 0) {
        <p class="empty">{{ 'approvals.empty' | transloco }}</p>
      } @else {
        <div
          class="table-scroll"
          role="region"
          tabindex="0"
          [attr.aria-label]="'approvals.inboxTable' | transloco"
        >
          <table class="responsive-table">
            <thead>
              <tr>
                <th>{{ 'approvals.requisition' | transloco }}</th>
                <th>{{ 'approvals.amount' | transloco }}</th>
                <th>{{ 'approvals.level' | transloco }}</th>
                <th>{{ 'approvals.assignee' | transloco }}</th>
                <th>{{ 'approvals.dueAt' | transloco }}</th>
                <th>{{ 'common.actions' | transloco }}</th>
              </tr>
            </thead>
            <tbody>
              @for (item of items().items; track item.id) {
                <tr [attr.data-approval]="item.id">
                  <td [attr.data-label]="'approvals.requisition' | transloco">
                    {{ item.requisitionNumber }}
                  </td>
                  <td [attr.data-label]="'approvals.amount' | transloco">
                    {{ item.amount | money }}
                  </td>
                  <td [attr.data-label]="'approvals.level' | transloco">
                    {{ item.currentLevel }}/{{ item.finalLevel }}
                  </td>
                  <td [attr.data-label]="'approvals.assignee' | transloco">
                    @if (item.assigneeRole) {
                      {{ 'approvals.roleGroup' | transloco: { role: item.assigneeRole } }}
                    } @else {
                      {{ 'approvals.assignedToMe' | transloco }}
                    }
                    @if (item.escalated) {
                      <span class="notice">{{ 'approvals.escalated' | transloco }}</span>
                    }
                  </td>
                  <td [attr.data-label]="'approvals.dueAt' | transloco">
                    {{ instant(item.dueAt) }}
                    @if (due(item.dueAt); as state) {
                      @if (state === 'overdue' || state === 'due-soon') {
                        <span class="warning">{{ 'approvals.due.' + state | transloco }}</span>
                      }
                    }
                  </td>
                  <td class="actions">
                    <a hlmBtn size="sm" [routerLink]="['/approvals', item.id]">{{
                      'approvals.open' | transloco
                    }}</a>
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
export class ApprovalsInboxPage {
  private readonly approvals = inject(APPROVAL_PORT);
  private readonly transloco = inject(TranslocoService);

  readonly items = signal<Page<ApprovalSummary>>(EMPTY);
  readonly escalatedOnly = signal(false);
  readonly error = signal<ApplicationError | undefined>(undefined);

  constructor() {
    void this.load();
  }

  async load() {
    const result = await this.approvals.inbox({
      page: 1,
      pageSize: 50,
      status: 'Pending',
      escalated: this.escalatedOnly() ? true : undefined,
    });
    if (!result.ok) return this.error.set(result.error);
    this.error.set(undefined);
    this.items.set(result.value);
  }

  toggleEscalated(value: boolean) {
    this.escalatedOnly.set(value);
    void this.load();
  }

  due(dueAt: string | undefined): DueState {
    return ApprovalUseCases.dueState(dueAt, Date.now());
  }

  instant(value: string | undefined) {
    if (!value) return '—';
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? value
      : new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
          dateStyle: 'short',
          timeStyle: 'short',
        }).format(date);
  }
}
