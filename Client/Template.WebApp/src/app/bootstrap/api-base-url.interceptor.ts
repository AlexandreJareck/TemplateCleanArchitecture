import { inject } from '@angular/core';
import type { HttpInterceptorFn } from '@angular/common/http';
import { RUNTIME_CONFIG } from './tokens';

export const apiBaseUrlInterceptor: HttpInterceptorFn = (request, next) => {
  const { apiBaseUrl } = inject(RUNTIME_CONFIG);
  if (!apiBaseUrl || !request.url.startsWith('/api/')) return next(request);
  return next(request.clone({ url: `${apiBaseUrl.replace(/\/$/, '')}${request.url}` }));
};
