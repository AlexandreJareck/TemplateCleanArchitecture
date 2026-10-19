import { inject, Injectable } from '@angular/core';
import type { CurrentUser, SessionPort } from '@application/session/session';
import { failure, success } from '@application/common/result';
import { AuthSessionsService } from '../api/generated/auth-sessions/auth-sessions.service';
import { CurrentAccountService } from '../api/generated/current-account/current-account.service';
import type { CurrentUserDto } from '../api/generated/models/currentUserDto';
import { mapProblem } from '../http/problem-mapper';
import { AccessTokenStore } from './access-token-store';
import {
  firstValueFrom,
  fromEvent,
  shareReplay,
  takeUntil,
  type Observable,
  finalize,
  tap,
} from 'rxjs';

@Injectable({ providedIn: 'root' })
export class SessionAdapter implements SessionPort {
  private refreshInFlight?: Observable<unknown>;
  private readonly sessions = inject(AuthSessionsService);
  private readonly account = inject(CurrentAccountService);
  private readonly tokens = inject(AccessTokenStore);

  async login(userName: string, password: string, signal?: AbortSignal) {
    try {
      const token = await firstValueFrom(
        this.cancel(this.sessions.postApiV2AuthSessions({ userName, password }), signal),
      );
      this.tokens.set(token.accessToken ?? undefined);
      return success(await this.current(signal));
    } catch (error) {
      this.tokens.clear();
      return failure<CurrentUser>(mapProblem(error, signal));
    }
  }

  async restore(signal?: AbortSignal) {
    try {
      await firstValueFrom(this.cancel(this.refresh(), signal));
      return success(await this.current(signal));
    } catch (error) {
      this.tokens.clear();
      return failure<CurrentUser>(mapProblem(error, signal));
    }
  }

  async logout(all: boolean, signal?: AbortSignal) {
    try {
      const request = all
        ? this.sessions.deleteApiV2AuthSessions()
        : this.sessions.deleteApiV2AuthSessionsCurrent();
      await firstValueFrom(this.cancel(request, signal), { defaultValue: undefined });
      this.tokens.clear();
      return success(undefined);
    } catch (error) {
      this.tokens.clear();
      return failure<void>(mapProblem(error, signal));
    }
  }

  refresh(): Observable<unknown> {
    if (!this.refreshInFlight) {
      this.refreshInFlight = this.sessions.postApiV2AuthSessionsRefresh().pipe(
        tap((response) => this.tokens.set(response.accessToken ?? undefined)),
        finalize(() => {
          this.refreshInFlight = undefined;
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    }
    return this.refreshInFlight;
  }

  private async current(signal?: AbortSignal): Promise<CurrentUser> {
    const dto = await firstValueFrom(this.cancel(this.account.getApiV2AccountMe(), signal));
    return mapUser(dto);
  }

  private cancel<T>(source: Observable<T>, signal?: AbortSignal) {
    return signal ? source.pipe(takeUntil(fromEvent(signal, 'abort'))) : source;
  }
}

function mapUser(dto: CurrentUserDto): CurrentUser {
  return {
    id: dto.userId ?? '',
    userName: dto.userName ?? '',
    displayName: dto.displayName ?? dto.userName ?? '',
    permissions: new Set(dto.permissions ?? []),
  };
}
