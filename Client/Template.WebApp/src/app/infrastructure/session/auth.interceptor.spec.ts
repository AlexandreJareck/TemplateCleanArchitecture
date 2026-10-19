import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AccessTokenStore } from './access-token-store';
import { authInterceptor } from './auth.interceptor';
import { SessionAdapter } from './session-adapter';
import { HttpClient } from '@angular/common/http';

describe('authInterceptor', () => {
  it('performs only one bounded refresh-and-retry for a protected request', async () => {
    const tokens = new AccessTokenStore();
    tokens.set('old-token');
    const refresh = vi.fn(() => {
      tokens.set('new-token');
      return of({});
    });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AccessTokenStore, useValue: tokens },
        { provide: SessionAdapter, useValue: { refresh } },
      ],
    });
    const http = TestBed.inject(HttpClient);
    const controller = TestBed.inject(HttpTestingController);
    const response = firstValueFrom(http.get('/api/v2/departments'));
    const initial = controller.expectOne('/api/v2/departments');
    expect(initial.request.headers.get('Authorization')).toBe('Bearer old-token');
    initial.flush({ code: 'session.expired' }, { status: 401, statusText: 'Unauthorized' });
    const retry = controller.expectOne('/api/v2/departments');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new-token');
    retry.flush({ code: 'still.unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    await expect(response).rejects.toMatchObject({ status: 401 });
    expect(refresh).toHaveBeenCalledTimes(1);
    controller.verify();
  });
});
