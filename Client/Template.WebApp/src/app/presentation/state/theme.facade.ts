import { DOCUMENT } from '@angular/common';
import { computed, effect, inject, Injectable, signal } from '@angular/core';

export type ThemePreference = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'procurement-hub.theme';
const ORDER: readonly ThemePreference[] = ['light', 'dark', 'system'];

/**
 * Light/dark appearance. Only the non-sensitive preference is kept in localStorage; the resolved
 * theme is applied as the `dark` class that the spartan preset's `dark:` variant expects.
 */
@Injectable({ providedIn: 'root' })
export class ThemeFacade {
  private readonly document = inject(DOCUMENT);
  private readonly media = this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)');
  private readonly systemDark = signal(this.media?.matches ?? false);
  readonly preference = signal<ThemePreference>(this.read());
  readonly resolved = computed(() => {
    const preference = this.preference();
    return preference === 'system' ? (this.systemDark() ? 'dark' : 'light') : preference;
  });

  constructor() {
    this.media?.addEventListener?.('change', (event) => this.systemDark.set(event.matches));
    effect(() => {
      const dark = this.resolved() === 'dark';
      this.document.documentElement.classList.toggle('dark', dark);
      this.document.documentElement.dataset['theme'] = dark ? 'dark' : 'light';
    });
  }

  cycle() {
    const next = ORDER[(ORDER.indexOf(this.preference()) + 1) % ORDER.length];
    this.preference.set(next);
    try {
      this.document.defaultView?.localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // Storage can be unavailable (private mode); the choice then lasts for this tab only.
    }
  }

  private read(): ThemePreference {
    try {
      const stored = this.document.defaultView?.localStorage.getItem(STORAGE_KEY);
      return ORDER.includes(stored as ThemePreference) ? (stored as ThemePreference) : 'system';
    } catch {
      return 'system';
    }
  }
}
