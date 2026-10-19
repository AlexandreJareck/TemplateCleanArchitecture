import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, Subject, throwError } from 'rxjs';
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
