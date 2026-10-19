import { HttpErrorResponse } from '@angular/common/http';
import type { DepartmentReader } from '@application/ports/department-reader';
import type { Department } from '@application/organization/department';
import { failure, success, type Result } from '@application/common/result';
import type { DepartmentViewPagedResult } from '../generated/models/departmentViewPagedResult';
import type { GetApiV2DepartmentsParams } from '../generated/models/getApiV2DepartmentsParams';
import { firstValueFrom, fromEvent, type Observable, takeUntil } from 'rxjs';

export interface DepartmentsTransport {
  getApiV2Departments(params?: GetApiV2DepartmentsParams): Observable<DepartmentViewPagedResult>;
}

interface ProblemBody {
  readonly code?: unknown;
  readonly detail?: unknown;
}

export class DepartmentApiReader implements DepartmentReader {
  constructor(private readonly transport: DepartmentsTransport) {}

  async readAll(signal?: AbortSignal): Promise<Result<readonly Department[]>> {
    if (signal?.aborted) return failure({ kind: 'cancelled', code: 'request.cancelled' });

    try {
      let response$ = this.transport.getApiV2Departments({ page: 1, pageSize: 100 });
      if (signal) response$ = response$.pipe(takeUntil(fromEvent(signal, 'abort')));
      const response = await firstValueFrom(response$);
      return success((response.items ?? []).map(mapDepartment));
    } catch (error: unknown) {
      if (signal?.aborted || isEmptySequenceError(error))
        return failure({ kind: 'cancelled', code: 'request.cancelled' });
      return failure(mapHttpError(error));
    }
  }
}

function mapDepartment(value: NonNullable<DepartmentViewPagedResult['items']>[number]): Department {
  return {
    id: value.id ?? '',
    code: value.code ?? '',
    name: value.name ?? '',
    active: value.isActive ?? false,
  };
}

function mapHttpError(error: unknown) {
  if (!(error instanceof HttpErrorResponse))
    return { kind: 'unexpected' as const, code: 'unexpected' };
  const problem = isProblemBody(error.error) ? error.error : undefined;
  const code = typeof problem?.code === 'string' ? problem.code : `http.${error.status}`;
  const detail = typeof problem?.detail === 'string' ? problem.detail : undefined;
  if (error.status === 401) return { kind: 'not-authenticated' as const, code };
  if (error.status === 403) return { kind: 'forbidden' as const, code };
  if (error.status === 409 || error.status === 412)
    return { kind: 'conflict' as const, code, detail };
  if (error.status === 400 || error.status === 422)
    return { kind: 'validation' as const, code, detail };
  if (error.status === 0 || error.status >= 500)
    return { kind: 'unavailable' as const, code, detail };
  return { kind: 'unexpected' as const, code, detail };
}

function isProblemBody(value: unknown): value is ProblemBody {
  return typeof value === 'object' && value !== null;
}

function isEmptySequenceError(value: unknown): boolean {
  return value instanceof Error && value.name === 'EmptyError';
}
