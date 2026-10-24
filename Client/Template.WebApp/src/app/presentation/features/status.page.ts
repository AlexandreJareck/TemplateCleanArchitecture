import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

@Component({
  selector: 'app-status-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslocoPipe],
  templateUrl: './status.page.html',
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
