import { HttpContextToken, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AccessTokenStore } from './access-token-store';
import { SessionAdapter } from './session-adapter';

const RETRIED = new HttpContextToken(() => false);

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const tokens = inject(AccessTokenStore);
  const session = inject(SessionAdapter);
  const authRequest =
    tokens.token && request.url.includes('/api/')
      ? request.clone({ setHeaders: { Authorization: `Bearer ${tokens.token}` } })
      : request;
  const sessionEndpoint = request.url.includes('/api/v2/auth/sessions');
  return next(authRequest).pipe(
    catchError((error: unknown) => {
      const status =
        typeof error === 'object' && error !== null && 'status' in error ? error.status : undefined;
      if (status !== 401 || sessionEndpoint || request.context.get(RETRIED))
        return throwError(() => error);
      return session.refresh().pipe(
        switchMap(() =>
          next(
            request.clone({
              context: request.context.set(RETRIED, true),
              setHeaders: { Authorization: `Bearer ${tokens.token ?? ''}` },
            }),
          ),
        ),
        catchError((refreshError: unknown) => {
          tokens.clear();
          return throwError(() => refreshError);
        }),
      );
    }),
  );
};
