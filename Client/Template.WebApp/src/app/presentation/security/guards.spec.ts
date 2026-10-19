import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  provideRouter,
  Router,
  type ActivatedRouteSnapshot,
  type RouterStateSnapshot,
} from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import type { SessionSnapshot } from '@application/session/session';
import { describe, expect, it, vi } from 'vitest';
import { SessionFacade } from '../state/session.facade';
import { authenticatedGuard, permissionGuard } from './guards';

function setup(snapshot: SessionSnapshot, error?: ApplicationError, allowed = false) {
  const facade = {
    snapshot: signal(snapshot),
    error: signal(error),
    restore: vi.fn(),
    can: vi.fn(() => allowed),
  };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: SessionFacade, useValue: facade }],
  });
  return facade;
}

describe('route guards', () => {
  it('routes an unavailable bootstrap to the dedicated experience', async () => {
    setup({ status: 'anonymous' }, { kind: 'unavailable', code: 'http.0' });
    const result = await TestBed.runInInjectionContext(() =>
      authenticatedGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
    expect(result?.toString()).toBe('/unavailable');
  });

  it('routes an expired anonymous session to login with context', async () => {
    setup({ status: 'anonymous', reason: 'expired' });
    const result = await TestBed.runInInjectionContext(() =>
      authenticatedGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
    expect(result?.toString()).toBe('/login?reason=expired');
  });

  it('keeps permission navigation client-side while denying absent capabilities', () => {
    setup({
      status: 'authenticated',
      user: { id: '1', userName: 'u', displayName: 'U', permissions: new Set() },
    });
    const route = { data: { permission: 'catalog.read' } } as unknown as ActivatedRouteSnapshot;
    const result = TestBed.runInInjectionContext(() =>
      permissionGuard(route, {} as RouterStateSnapshot),
    );
    expect(result?.toString()).toBe('/forbidden');
    expect(TestBed.inject(Router)).toBeTruthy();
  });
});
