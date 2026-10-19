import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { mapProblem } from './problem-mapper';

describe('mapProblem', () => {
  it('maps validation fields and safe support details', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { code: 'validation.failed', traceId: 'trace-7', errors: { name: ['Required'] } },
    });
    expect(mapProblem(error)).toMatchObject({
      kind: 'validation',
      code: 'validation.failed',
      traceId: 'trace-7',
      fieldErrors: { name: ['Required'] },
    });
  });

  it.each([
    [409, 'conflict'],
    [412, 'version-mismatch'],
    [428, 'version-mismatch'],
    [503, 'unavailable'],
  ] as const)('maps HTTP %s to %s', (status, kind) => {
    expect(mapProblem(new HttpErrorResponse({ status, error: { code: 'problem' } })).kind).toBe(
      kind,
    );
  });

  it('maps Retry-After without exposing response internals', () => {
    const error = new HttpErrorResponse({
      status: 429,
      headers: new HttpHeaders({ 'Retry-After': '12' }),
      error: { code: 'rate.limit' },
    });
    expect(mapProblem(error)).toMatchObject({ kind: 'rate-limited', retryAfterSeconds: 12 });
  });

  it('maps cancellation independently from transport status', () => {
    const controller = new AbortController();
    controller.abort();
    expect(mapProblem(new Error('hidden transport detail'), controller.signal)).toEqual({
      kind: 'cancelled',
      code: 'request.cancelled',
    });
  });
});
