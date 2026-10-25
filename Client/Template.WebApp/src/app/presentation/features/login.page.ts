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
  templateUrl: './login.page.html',
  styles: [
    `
      :host {
        display: block;
      }
      .auth-page {
        min-height: 100vh;
        display: grid;
        grid-template-columns: minmax(0, 1fr);
        background: var(--background);
      }
      .auth-brand {
        display: none;
      }
      .auth-panel {
        display: grid;
        place-items: center;
        padding: var(--space-6) var(--space-4);
      }
      .auth-card {
        width: min(100%, 24rem);
        display: grid;
        gap: var(--space-5);
      }
      .auth-header {
        display: grid;
        gap: var(--space-2);
      }
      .auth-header h1 {
        margin: var(--space-3) 0 0;
        font-size: 1.75rem;
        line-height: 1.2;
        font-weight: 700;
        letter-spacing: -0.01em;
      }
      .auth-header p {
        margin: 0;
        color: var(--muted-foreground);
      }
      .brand-logo {
        display: inline-grid;
        place-items: center;
        width: 2.5rem;
        height: 2.5rem;
        border-radius: var(--radius-md);
        background: var(--primary-foreground);
        color: var(--primary);
        font-weight: 800;
        font-size: 0.875rem;
      }
      .brand-logo.compact {
        background: var(--primary);
        color: var(--primary-foreground);
      }
      .field {
        display: grid;
        gap: var(--space-2);
      }
      .field label {
        font-size: 0.875rem;
        font-weight: 600;
      }
      .field input {
        height: 2.75rem;
        background: var(--card);
      }
      .submit {
        width: 100%;
        height: 2.75rem;
        font-size: 0.95rem;
      }
      .notice {
        margin: 0;
        padding: var(--space-3);
        border-radius: var(--radius-md);
        background: var(--accent);
        color: var(--accent-foreground);
        font-size: 0.875rem;
      }
      .feedback {
        display: grid;
        gap: 0.25rem;
        padding: var(--space-3);
        border: 1px solid var(--destructive);
        border-radius: var(--radius-md);
        color: var(--destructive);
        font-size: 0.875rem;
      }
      @media (min-width: 960px) {
        .auth-page {
          grid-template-columns: minmax(0, 1.1fr) minmax(0, 1fr);
        }
        .auth-brand {
          display: flex;
          flex-direction: column;
          justify-content: space-between;
          padding: var(--space-8);
          color: var(--primary-foreground);
          background:
            radial-gradient(circle at 20% 15%, oklch(0.62 0.17 258 / 0.55), transparent 55%),
            linear-gradient(160deg, var(--primary), oklch(0.3 0.12 262));
        }
        .brand-mark {
          display: flex;
          align-items: center;
          gap: var(--space-3);
          font-weight: 700;
          font-size: 1.125rem;
        }
        .brand-copy h2 {
          margin: 0 0 var(--space-6);
          max-width: 26rem;
          font-size: 2.25rem;
          line-height: 1.15;
          font-weight: 700;
        }
        .brand-copy ul {
          display: grid;
          gap: var(--space-3);
          margin: 0;
          padding: 0;
          list-style: none;
        }
        .brand-copy li {
          display: flex;
          align-items: center;
          gap: var(--space-3);
          opacity: 0.92;
        }
        .brand-copy li::before {
          content: '';
          width: 0.5rem;
          height: 0.5rem;
          border-radius: 50%;
          background: currentColor;
        }
        .brand-footer {
          margin: 0;
          font-size: 0.8rem;
          opacity: 0.75;
        }
        .auth-header .compact {
          display: none;
        }
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
