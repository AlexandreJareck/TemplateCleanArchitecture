import {
  ChangeDetectionStrategy,
  Component,
  effect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import type { ElementRef } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';

export interface ActionConfirmationRequest {
  readonly title: string;
  readonly actionLabel: string;
}

@Component({
  selector: 'app-action-confirmation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, HlmButton, HlmInput],
  template: `
    @if (request(); as current) {
      <div class="dialog-backdrop" role="presentation" (keydown.escape)="cancel()">
        <section role="dialog" aria-modal="true" aria-labelledby="confirmation-title">
          <h2 id="confirmation-title">{{ current.title }}</h2>
          <label for="action-reason">{{ 'common.reason' | transloco }}</label>
          <input
            id="action-reason"
            #reasonInput
            hlmInput
            [value]="reason()"
            (input)="reason.set($any($event.target).value)"
          />
          <div class="actions">
            <button hlmBtn variant="outline" type="button" (click)="cancel()">
              {{ 'common.cancel' | transloco }}
            </button>
            <button hlmBtn type="button" [disabled]="!reason().trim()" (click)="confirm()">
              {{ current.actionLabel }}
            </button>
          </div>
        </section>
      </div>
    }
  `,
  styles: [
    `
      .dialog-backdrop {
        position: fixed;
        inset: 0;
        z-index: 50;
        display: grid;
        place-items: center;
        padding: var(--space-4);
        background: color-mix(in srgb, var(--foreground) 45%, transparent);
      }
      section {
        width: min(100%, 30rem);
        display: grid;
        gap: var(--space-3);
        padding: var(--space-6);
        border-radius: var(--radius-lg);
        background: var(--card);
        box-shadow: var(--shadow-lg);
      }
      h2 {
        margin: 0;
      }
      .actions {
        display: flex;
        justify-content: flex-end;
        gap: var(--space-3);
      }
    `,
  ],
})
export class ActionConfirmation {
  readonly request = input<ActionConfirmationRequest>();
  readonly confirmed = output<string>();
  readonly cancelled = output<void>();
  readonly reason = signal('');
  private readonly reasonInput = viewChild<ElementRef<HTMLInputElement>>('reasonInput');

  constructor() {
    effect(() => {
      this.request();
      this.reason.set('');
      queueMicrotask(() => this.reasonInput()?.nativeElement.focus());
    });
  }

  confirm() {
    const reason = this.reason().trim();
    if (reason) this.confirmed.emit(reason);
  }

  cancel() {
    this.cancelled.emit();
  }
}
