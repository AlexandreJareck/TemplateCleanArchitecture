import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

@Component({
  selector: 'app-status-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslocoPipe],
  template: `<main class="status">
    <h1>{{ key + '.title' | transloco }}</h1>
    <p>{{ key + '.body' | transloco }}</p>
    <a routerLink="/">{{ 'common.backHome' | transloco }}</a>
  </main>`,
  styles: [
    `
      .status {
        max-width: 40rem;
        margin: 10vh auto;
        padding: var(--space-8);
        text-align: center;
      }
    `,
  ],
})
export class StatusPage {
  protected readonly key = inject(ActivatedRoute).snapshot.data['key'] as string;
}
