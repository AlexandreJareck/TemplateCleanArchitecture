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

/** Requires `data.permission`, or any of `data.anyPermission` (resource-scoped reads such as REQ-011). */
export const permissionGuard: CanActivateFn = (route) => {
  const session = inject(SessionFacade);
  const permission = route.data['permission'];
  const anyPermission = route.data['anyPermission'];
  const allowed =
    (typeof permission === 'string' && session.can(permission)) ||
    (Array.isArray(anyPermission) &&
      anyPermission.some((value) => typeof value === 'string' && session.can(value)));
  return allowed ? true : inject(Router).parseUrl('/forbidden');
};
