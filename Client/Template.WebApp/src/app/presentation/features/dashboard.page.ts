import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { PermissionDirective } from '../security/permission.directive';
import { SessionFacade } from '../state/session.facade';

@Component({
  selector: 'app-dashboard-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslocoPipe, PermissionDirective],
  template: `<header class="page-title">
      <p>{{ 'shell.breadcrumbHome' | transloco }}</p>
      <h1>{{ 'dashboard.title' | transloco }}</h1>
      <p>{{ 'dashboard.welcome' | transloco: { name: session.user()?.displayName } }}</p>
    </header>
    <div class="cards">
      <a *appPermission="'organization.read'" routerLink="/organization/departments">{{
        'nav.departments' | transloco
      }}</a
      ><a *appPermission="'suppliers.read'" routerLink="/suppliers">{{
        'nav.suppliers' | transloco
      }}</a
      ><a *appPermission="'catalog.read'" routerLink="/catalog/items">{{
        'nav.items' | transloco
      }}</a
      ><a *appPermission="'requisitions.create'" routerLink="/requisitions/new">{{
        'requisition.new' | transloco
      }}</a
      ><a *appPermission="'requisitions.create'" routerLink="/requisitions/mine">{{
        'nav.myRequisitions' | transloco
      }}</a
      ><a *appPermission="'budget.read'" routerLink="/budget/allocations">{{
        'nav.budgetAllocations' | transloco
      }}</a>
    </div>`,
  styles: [
    `
      .page-title {
        margin-bottom: var(--space-8);
      }
      h1 {
        font-size: 2rem;
        margin: 0.25rem 0;
      }
      .cards {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
        gap: var(--space-4);
      }
      .cards a {
        padding: var(--space-6);
        border: 1px solid var(--border);
        border-radius: var(--radius-lg);
        background: var(--card);
        color: var(--foreground);
        font-weight: 700;
        text-decoration: none;
        box-shadow: var(--shadow-sm);
      }
    `,
  ],
})
export class DashboardPage {
  protected readonly session = inject(SessionFacade);
}
