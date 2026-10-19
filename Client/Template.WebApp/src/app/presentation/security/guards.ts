import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';
import { Router } from '@angular/router';
import { SessionFacade } from '../state/session.facade';

export const authenticatedGuard: CanActivateFn = async () => {
  const session = inject(SessionFacade);
  const router = inject(Router);
  if (session.snapshot().status === 'checking') await session.restore();
  if (session.snapshot().status === 'authenticated') return true;
  if (session.error()?.kind === 'unavailable') return router.parseUrl('/unavailable');
  return router.parseUrl('/login?reason=expired');
};

export const permissionGuard: CanActivateFn = (route) => {
  const permission = route.data['permission'];
  return typeof permission === 'string' && inject(SessionFacade).can(permission)
    ? true
    : inject(Router).parseUrl('/forbidden');
};
