import type { Result } from '../common/result';

export interface CurrentUser {
  readonly id: string;
  readonly userName: string;
  readonly displayName: string;
  readonly permissions: ReadonlySet<string>;
}

export type SessionSnapshot =
  | { readonly status: 'checking' }
  | { readonly status: 'anonymous'; readonly reason?: 'expired' | 'revoked' }
  | { readonly status: 'authenticated'; readonly user: CurrentUser };

export interface SessionPort {
  login(userName: string, password: string, signal?: AbortSignal): Promise<Result<CurrentUser>>;
  restore(signal?: AbortSignal): Promise<Result<CurrentUser>>;
  logout(all: boolean, signal?: AbortSignal): Promise<Result<void>>;
}

export class SessionUseCases {
  constructor(private readonly port: SessionPort) {}

  login(userName: string, password: string, signal?: AbortSignal) {
    return this.port.login(userName.trim(), password, signal);
  }

  restore(signal?: AbortSignal) {
    return this.port.restore(signal);
  }

  logout(all = false, signal?: AbortSignal) {
    return this.port.logout(all, signal);
  }
}

export function hasPermission(user: CurrentUser | undefined, permission: string): boolean {
  return user?.permissions.has(permission) ?? false;
}
