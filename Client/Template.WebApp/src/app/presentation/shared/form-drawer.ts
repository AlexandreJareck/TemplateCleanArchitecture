import { A11yModule } from '@angular/cdk/a11y';
import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX } from '@ng-icons/lucide';

let nextId = 0;

/**
 * Right-side modal drawer for create/edit forms. Traps focus while open, closes on Escape or the
 * backdrop and returns focus to the element that opened it.
 */
@Component({
  selector: 'app-form-drawer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [A11yModule, TranslocoPipe, NgIcon],
  providers: [provideIcons({ lucideX })],
  host: { '(document:keydown.escape)': 'onEscape($event)' },
  templateUrl: './form-drawer.html',
  styles: [
    `
      .backdrop {
        position: fixed;
        inset: 0;
        z-index: 40;
        border: 0;
        background: var(--overlay);
        animation: fade-in 150ms ease-out;
      }
      .panel {
        position: fixed;
        inset: 0 0 0 auto;
        z-index: 41;
        width: min(100vw, var(--drawer-width, 32rem));
        display: flex;
        flex-direction: column;
        background: var(--card);
        color: var(--card-foreground);
        border-inline-start: 1px solid var(--border);
        box-shadow: var(--shadow-lg);
        animation: slide-in 200ms cubic-bezier(0.32, 0.72, 0, 1);
      }
      .panel.wide {
        --drawer-width: 48rem;
      }
      header {
        display: flex;
        align-items: flex-start;
        gap: var(--space-3);
        padding: var(--space-5) var(--space-6);
        border-bottom: 1px solid var(--border);
      }
      header div {
        flex: 1;
        display: grid;
        gap: 0.25rem;
      }
      h2 {
        margin: 0;
        font-size: 1.25rem;
        font-weight: 700;
      }
      header p {
        margin: 0;
        font-size: 0.875rem;
        color: var(--muted-foreground);
      }
      .close {
        display: inline-grid;
        place-items: center;
        width: 2.25rem;
        height: 2.25rem;
        border: 1px solid var(--border);
        border-radius: var(--radius-md);
        background: var(--card);
        color: var(--foreground);
        font-size: 1.125rem;
        cursor: pointer;
      }
      .close:hover {
        background: var(--accent);
      }
      .body {
        flex: 1;
        overflow-y: auto;
        padding: var(--space-6);
      }
      @keyframes slide-in {
        from {
          transform: translateX(100%);
        }
      }
      @keyframes fade-in {
        from {
          opacity: 0;
        }
      }
    `,
  ],
})
export class FormDrawer {
  private readonly document = inject(DOCUMENT);
  readonly open = input(false);
  readonly title = input.required<string>();
  readonly description = input<string>();
  readonly wide = input(false);
  readonly closed = output<void>();
  protected readonly titleId = `form-drawer-title-${nextId++}`;
  private opener: HTMLElement | null = null;

  constructor() {
    effect(() => {
      if (this.open()) {
        this.opener = this.document.activeElement as HTMLElement | null;
      } else if (this.opener) {
        const opener = this.opener;
        this.opener = null;
        queueMicrotask(() => opener.isConnected && opener.focus());
      }
    });
  }

  close() {
    this.closed.emit();
  }

  /** Document-level so Escape still works after focus fell to the body (e.g. a disabled button). */
  protected onEscape(event: Event) {
    if (!this.open()) return;
    const dialog = (event.target as Element | null)?.closest?.('[role="dialog"]');
    if (dialog && !dialog.classList.contains('panel')) return; // a dialog stacked on top owns it
    this.close();
  }
}
