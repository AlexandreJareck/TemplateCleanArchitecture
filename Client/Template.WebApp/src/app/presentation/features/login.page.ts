import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { SessionFacade } from '../state/session.facade';

@Component({
  selector: 'app-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormField, TranslocoPipe, HlmButton, HlmInput],
  template: `
    <main class="auth-page">
      <form class="auth-card" novalidate (submit)="submit($event)">
        <p class="eyebrow">Procurement Hub</p>
        <h1>{{ 'auth.signIn' | transloco }}</h1>
        @if (expired) {
          <p class="session-message" role="status">{{ 'status.expired.body' | transloco }}</p>
        }
        <label for="userName">{{ 'auth.userName' | transloco }}</label>
        <input id="userName" hlmInput autocomplete="username" [formField]="loginForm.userName" />
        <label for="password">{{ 'auth.password' | transloco }}</label>
        <input
          id="password"
          hlmInput
          type="password"
          autocomplete="current-password"
          [formField]="loginForm.password"
        />
        @if (session.error(); as error) {
          <div class="feedback" role="alert">
            <strong>{{ 'auth.failed' | transloco }}</strong
            ><span>{{ error.code }}</span>
            @if (error.traceId) {
              <small>Trace: {{ error.traceId }}</small>
            }
          </div>
        }
        <button hlmBtn type="submit" [disabled]="loginForm().invalid() || session.busy()">
          {{ (session.busy() ? 'common.loading' : 'auth.enter') | transloco }}
        </button>
      </form>
    </main>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .auth-page {
        min-height: 100vh;
        display: grid;
        place-items: center;
        padding: var(--space-4);
        background: var(--muted);
      }
      .auth-card {
        width: min(100%, 24rem);
        display: grid;
        gap: var(--space-3);
        background: var(--card);
        padding: var(--space-8);
        border: 1px solid var(--border);
        border-radius: var(--radius-lg);
        box-shadow: var(--shadow-lg);
      }
      h1 {
        margin: 0 0 var(--space-4);
      }
      .feedback {
        display: grid;
        gap: var(--space-2);
        color: var(--destructive);
        padding: var(--space-3);
        border: 1px solid var(--destructive);
        border-radius: var(--radius-md);
      }
      .eyebrow {
        color: var(--primary);
        font-weight: 700;
      }
    `,
  ],
})
export class LoginPage {
  protected readonly session = inject(SessionFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly expired = this.route.snapshot.queryParamMap.get('reason') === 'expired';
  readonly model = signal({ userName: '', password: '' });
  readonly loginForm = form(this.model, (path) => {
    required(path.userName);
    required(path.password);
  });
  async submit(event: Event) {
    event.preventDefault();
    if (this.loginForm().invalid()) return;
    const value = this.model();
    if (await this.session.login(value.userName, value.password))
      await this.router.navigateByUrl('/');
  }
}
