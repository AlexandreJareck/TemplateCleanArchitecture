import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { PermissionDirective } from '../security/permission.directive';
import { SessionFacade } from '../state/session.facade';

@Component({
  selector: 'app-dashboard-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslocoPipe, PermissionDirective],
  templateUrl: './dashboard.page.html',
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
