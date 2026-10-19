import { computed, inject, Injectable, signal } from '@angular/core';
import { hasPermission, SessionUseCases, type SessionSnapshot } from '@application/session/session';
import type { ApplicationError } from '@application/common/application-error';
import { SESSION_PORT } from './ports';

@Injectable({ providedIn: 'root' })
export class SessionFacade {
  private readonly useCases = new SessionUseCases(inject(SESSION_PORT));
  readonly snapshot = signal<SessionSnapshot>({ status: 'checking' });
  readonly busy = signal(false);
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly user = computed(() => {
    const snapshot = this.snapshot();
    return snapshot.status === 'authenticated' ? snapshot.user : undefined;
  });

  async restore() {
    this.busy.set(true);
    const result = await this.useCases.restore();
    this.busy.set(false);
    this.snapshot.set(
      result.ok
        ? { status: 'authenticated', user: result.value }
        : {
            status: 'anonymous',
            reason: result.error.kind === 'not-authenticated' ? 'expired' : undefined,
          },
    );
    this.error.set(
      result.ok || result.error.kind === 'not-authenticated' ? undefined : result.error,
    );
  }

  async login(userName: string, password: string) {
    this.busy.set(true);
    this.error.set(undefined);
    const result = await this.useCases.login(userName, password);
    this.busy.set(false);
    if (result.ok) this.snapshot.set({ status: 'authenticated', user: result.value });
    else this.error.set(result.error);
    return result.ok;
  }

  async logout(all = false) {
    this.busy.set(true);
    await this.useCases.logout(all);
    this.busy.set(false);
    this.snapshot.set({ status: 'anonymous' });
  }

  can(permission: string) {
    return hasPermission(this.user(), permission);
  }
}
