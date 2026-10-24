import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { PermissionDirective } from '../security/permission.directive';
import { SessionFacade } from '../state/session.facade';
import { filter } from 'rxjs';

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    TranslocoPipe,
    HlmButton,
    PermissionDirective,
  ],
  template: `
    <a class="skip-link" href="#main-content">{{ 'shell.skip' | transloco }}</a>
    <header>
      <button class="menu" type="button" (click)="open.update((v) => !v)" aria-label="Menu">
        ☰</button
      ><a routerLink="/" class="brand">Procurement Hub</a
      ><span class="user">{{ session.user()?.displayName }}</span
      ><button hlmBtn variant="ghost" type="button" (click)="logout(true)">
        {{ 'auth.logoutAll' | transloco }}</button
      ><button hlmBtn variant="outline" type="button" (click)="logout()">
        {{ 'auth.logout' | transloco }}
      </button>
    </header>
    <div class="layout">
      <nav [class.open]="open()" aria-label="Principal">
        <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">{{
          'nav.dashboard' | transloco
        }}</a>
        <a
          *appPermission="'organization.read'"
          routerLink="/organization/departments"
          routerLinkActive="active"
          >{{ 'nav.departments' | transloco }}</a
        >
        <a
          *appPermission="'organization.read'"
          routerLink="/organization/cost-centers"
          routerLinkActive="active"
          >{{ 'nav.costCenters' | transloco }}</a
        >
        <a *appPermission="'suppliers.read'" routerLink="/suppliers" routerLinkActive="active">{{
          'nav.suppliers' | transloco
        }}</a>
        <a
          *appPermission="'catalog.read'"
          routerLink="/catalog/categories"
          routerLinkActive="active"
          >{{ 'nav.categories' | transloco }}</a
        >
        <a *appPermission="'catalog.read'" routerLink="/catalog/units" routerLinkActive="active">{{
          'nav.units' | transloco
        }}</a>
        <a *appPermission="'catalog.read'" routerLink="/catalog/items" routerLinkActive="active">{{
          'nav.items' | transloco
        }}</a>
        <a
          *appPermission="'requisitions.create'"
          routerLink="/requisitions/mine"
          routerLinkActive="active"
          >{{ 'nav.myRequisitions' | transloco }}</a
        >
        @if (session.can('requisitions.read-team') || session.can('requisitions.read-all')) {
          <a routerLink="/requisitions/team" routerLinkActive="active">{{
            'nav.teamRequisitions' | transloco
          }}</a>
        }
        @if (session.can('sourcing.rfqs.manage') || session.can('sourcing.quotes.read')) {
          <a routerLink="/sourcing" routerLinkActive="active">{{ 'nav.sourcing' | transloco }}</a>
        }
        <a
          *appPermission="'approvals.decide'"
          routerLink="/approvals/inbox"
          routerLinkActive="active"
          >{{ 'nav.approvals' | transloco }}</a
        >
        <a
          *appPermission="'purchasing.orders.read'"
          routerLink="/purchasing/orders"
          routerLinkActive="active"
          >{{ 'nav.purchaseOrders' | transloco }}</a
        >
        <a
          *appPermission="'receiving.receipts.create'"
          routerLink="/receiving/queue"
          routerLinkActive="active"
          >{{ 'nav.receiving' | transloco }}</a
        >
        <a *appPermission="'reports.request'" routerLink="/reports" routerLinkActive="active">{{
          'nav.reports' | transloco
        }}</a>
        <a *appPermission="'budget.read'" routerLink="/budget/periods" routerLinkActive="active">{{
          'nav.budgetPeriods' | transloco
        }}</a>
        <a
          *appPermission="'budget.read'"
          routerLink="/budget/allocations"
          routerLinkActive="active"
          >{{ 'nav.budgetAllocations' | transloco }}</a
        >
      </nav>
      <main id="main-content" tabindex="-1"><router-outlet /></main>
    </div>
  `,
  styleUrl: './app-shell.css',
})
export class AppShell {
  protected readonly session = inject(SessionFacade);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  readonly open = signal(false);
  constructor() {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        this.open.set(false);
        setTimeout(() => this.document.getElementById('main-content')?.focus());
      });
  }
  async logout(all = false) {
    await this.session.logout(all);
    await this.router.navigateByUrl('/login');
  }
}
