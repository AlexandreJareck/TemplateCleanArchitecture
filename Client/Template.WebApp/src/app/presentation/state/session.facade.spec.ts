import { TestBed } from '@angular/core/testing';
import type { SessionPort } from '@application/session/session';
import { SESSION_PORT } from './ports';
import { SessionFacade } from './session.facade';

describe('SessionFacade', () => {
  it('exposes effective permissions after restore', async () => {
    const port: SessionPort = {
      restore: async () => ({
        ok: true,
        value: {
          id: '1',
          userName: 'buyer',
          displayName: 'Buyer',
          permissions: new Set(['suppliers.read']),
        },
      }),
      login: async () => ({
        ok: false,
        error: { kind: 'not-authenticated', code: 'auth.invalid' },
      }),
      logout: async () => ({ ok: true, value: undefined }),
    };
    TestBed.configureTestingModule({ providers: [{ provide: SESSION_PORT, useValue: port }] });
    const facade = TestBed.inject(SessionFacade);
    await facade.restore();
    expect(facade.can('suppliers.read')).toBe(true);
  });
});
