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
  /** Optional context shown above the reason (for example the submission summary). */
  readonly description?: string;
  /** Defaults to true; transitions without a reason only confirm. */
  readonly requireReason?: boolean;
}

@Component({
  selector: 'app-action-confirmation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, HlmButton, HlmInput],
  templateUrl: './action-confirmation.html',
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
  private readonly confirmButton = viewChild<ElementRef<HTMLButtonElement>>('confirmButton');

  constructor() {
    effect(() => {
      this.request();
      this.reason.set('');
      queueMicrotask(() => (this.reasonInput() ?? this.confirmButton())?.nativeElement.focus());
    });
  }

  confirm() {
    const reason = this.reason().trim();
    if (reason || this.request()?.requireReason === false) this.confirmed.emit(reason);
  }

  cancel() {
    this.cancelled.emit();
  }
}
