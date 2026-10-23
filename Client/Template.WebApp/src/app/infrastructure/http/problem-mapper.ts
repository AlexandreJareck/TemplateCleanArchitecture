import { HttpErrorResponse } from '@angular/common/http';
import type { ApplicationError } from '@application/common/application-error';

export function mapProblem(error: unknown, signal?: AbortSignal): ApplicationError {
  if (signal?.aborted || (error instanceof Error && error.name === 'EmptyError'))
    return { kind: 'cancelled', code: 'request.cancelled' };
  if (!(error instanceof HttpErrorResponse)) return { kind: 'unexpected', code: 'unexpected' };
  const body = isRecord(error.error) ? error.error : {};
  const code = stringValue(body['code']) ?? `http.${error.status}`;
  const common = {
    code,
    detail: stringValue(body['detail']),
    traceId: stringValue(body['traceId']),
    ruleId: stringValue(body['ruleId']),
    currentVersion: stringValue(body['version']),
    currentState: stringValue(body['currentState']),
  };
  if (error.status === 401) return { kind: 'not-authenticated', ...common };
  if (error.status === 403) return { kind: 'forbidden', ...common };
  if (error.status === 404) return { kind: 'not-found', ...common };
  if (error.status === 400)
    return { kind: 'validation', ...common, fieldErrors: fieldErrors(body['errors']) };
  if (error.status === 409) return { kind: 'conflict', ...common };
  if (error.status === 410) return { kind: 'gone', ...common };
  if (error.status === 412 || error.status === 428) return { kind: 'version-mismatch', ...common };
  if (error.status === 422)
    return { kind: 'business-rule', ...common, fieldErrors: fieldErrors(body['errors']) };
  if (error.status === 429)
    return { kind: 'rate-limited', ...common, retryAfterSeconds: retryAfter(error) };
  if (error.status === 0 || error.status >= 500) return { kind: 'unavailable', ...common };
  return { kind: 'unexpected', ...common };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
function stringValue(value: unknown) {
  return typeof value === 'string' ? value : undefined;
}
function fieldErrors(value: unknown): Readonly<Record<string, readonly string[]>> | undefined {
  if (!isRecord(value)) return undefined;
  return Object.fromEntries(
    Object.entries(value).map(([key, messages]) => [
      key,
      Array.isArray(messages)
        ? messages.filter((item): item is string => typeof item === 'string')
        : [],
    ]),
  );
}
function retryAfter(error: HttpErrorResponse): number | undefined {
  const raw = error.headers.get('Retry-After');
  if (!raw) return undefined;
  const seconds = Number(raw);
  return Number.isFinite(seconds) ? seconds : undefined;
}
