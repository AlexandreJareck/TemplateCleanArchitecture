import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { defer, firstValueFrom, Subject, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AuthSessionsService } from '../api/generated/auth-sessions/auth-sessions.service';
import { CurrentAccountService } from '../api/generated/current-account/current-account.service';
import { AccessTokenStore } from './access-token-store';
import { SessionAdapter } from './session-adapter';

describe('SessionAdapter', () => {
  it('shares one refresh across concurrent callers and keeps the token only in memory', async () => {
    const response = new Subject<{ accessToken: string }>();
    const refresh = vi.fn(() => response.asObservable());
    TestBed.configureTestingModule({
      providers: [
        SessionAdapter,
        AccessTokenStore,
        { provide: AuthSessionsService, useValue: { postApiV2AuthSessionsRefresh: refresh } },
        { provide: CurrentAccountService, useValue: {} },
      ],
    });
    const adapter = TestBed.inject(SessionAdapter);
    const first = firstValueFrom(adapter.refresh());
    const second = firstValueFrom(adapter.refresh());
    response.next({ accessToken: 'transient-token' });
    response.complete();
    await Promise.all([first, second]);
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('retries a refresh once when another refresh of the family is still in progress (SEC-004)', async () => {
    vi.useFakeTimers();
    try {
      const tokens = new AccessTokenStore();
      const response = new Subject<{ accessToken: string }>();
      // HttpClient observables are cold: each subscription (including the retry) sends a request.
      let requests = 0;
      const refresh = vi.fn(() =>
        defer(() =>
          ++requests === 1
            ? throwError(
                () =>
                  new HttpErrorResponse({
                    status: 409,
                    error: { code: 'auth.refresh-in-progress' },
                  }),
              )
            : response.asObservable(),
        ),
      );
      TestBed.configureTestingModule({
        providers: [
          SessionAdapter,
          { provide: AccessTokenStore, useValue: tokens },
          { provide: AuthSessionsService, useValue: { postApiV2AuthSessionsRefresh: refresh } },
          { provide: CurrentAccountService, useValue: {} },
        ],
      });
      const result = firstValueFrom(TestBed.inject(SessionAdapter).refresh());
      await vi.advanceTimersByTimeAsync(500);
      response.next({ accessToken: 'after-retry' });
      response.complete();
      await result;
      expect(requests).toBe(2);
      expect(tokens.token).toBe('after-retry');
    } finally {
      vi.useRealTimers();
    }
  });

  it('does not retry other refresh failures', async () => {
    const refresh = vi.fn(() =>
      throwError(
        () => new HttpErrorResponse({ status: 401, error: { code: 'auth.session-expired' } }),
      ),
    );
    TestBed.configureTestingModule({
      providers: [
        SessionAdapter,
        AccessTokenStore,
        { provide: AuthSessionsService, useValue: { postApiV2AuthSessionsRefresh: refresh } },
        { provide: CurrentAccountService, useValue: {} },
      ],
    });
    await expect(firstValueFrom(TestBed.inject(SessionAdapter).refresh())).rejects.toBeInstanceOf(
      HttpErrorResponse,
    );
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('clears a stale token when restoration is rejected', async () => {
    const tokens = new AccessTokenStore();
    tokens.set('stale');
    TestBed.configureTestingModule({
      providers: [
        SessionAdapter,
        { provide: AccessTokenStore, useValue: tokens },
        {
          provide: AuthSessionsService,
          useValue: {
            postApiV2AuthSessionsRefresh: () =>
              throwError(
                () => new HttpErrorResponse({ status: 401, error: { code: 'session.expired' } }),
              ),
          },
        },
        { provide: CurrentAccountService, useValue: {} },
      ],
    });
    const adapter = TestBed.inject(SessionAdapter);
    const result = await adapter.restore();
    expect(result).toMatchObject({ ok: false, error: { kind: 'not-authenticated' } });
    expect(tokens.token).toBeUndefined();
  });
});
