import { describe, expect, it } from 'vitest';
import type { SessionPort } from './session';
import { hasPermission, SessionUseCases } from './session';

describe('SessionUseCases', () => {
  it('normalizes the user name and delegates login', async () => {
    let received = '';
    const port: SessionPort = {
      login: async (userName) => {
        received = userName;
        return {
          ok: false,
          error: { kind: 'not-authenticated', code: 'auth.invalid-credentials' },
        };
      },
      restore: async () => ({
        ok: false,
        error: { kind: 'not-authenticated', code: 'auth.session-expired' },
      }),
      logout: async () => ({ ok: true, value: undefined }),
    };
    await new SessionUseCases(port).login('  buyer  ', 'secret');
    expect(received).toBe('buyer');
  });

  it('checks effective permissions rather than role names', () => {
    expect(
      hasPermission(
        { id: '1', userName: 'a', displayName: 'A', permissions: new Set(['catalog.read']) },
        'catalog.read',
      ),
    ).toBe(true);
  });
});
