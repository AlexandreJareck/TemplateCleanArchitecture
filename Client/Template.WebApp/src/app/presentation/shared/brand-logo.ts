import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Fictitious Procurement Hub mark: a hexagon (hub) carrying an approval check. */
@Component({
  selector: 'app-brand-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './brand-logo.html',
  styles: [
    `
      :host {
        display: inline-flex;
        align-items: center;
        gap: 0.625rem;
        color: inherit;
      }
      svg {
        flex: none;
        filter: drop-shadow(0 4px 10px var(--brand-glow));
      }
      .wordmark {
        display: grid;
        line-height: 1.05;
      }
      .name {
        font-weight: 800;
        letter-spacing: -0.01em;
      }
      .tagline {
        font-size: 0.6875rem;
        font-weight: 600;
        letter-spacing: 0.08em;
        text-transform: uppercase;
        opacity: 0.7;
      }
    `,
  ],
})
export class BrandLogo {
  readonly size = input(36);
  readonly showWordmark = input(true);
  readonly showTagline = input(false);
}
