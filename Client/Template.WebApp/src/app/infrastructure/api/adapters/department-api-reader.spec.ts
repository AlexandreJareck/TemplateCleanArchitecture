import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { NEVER, of, throwError } from 'rxjs';
import { DepartmentApiReader, type DepartmentsTransport } from './department-api-reader';

describe('DepartmentApiReader', () => {
  it('maps generated transport DTOs to application models', async () => {
    const transport: DepartmentsTransport = {
      getApiV2Departments: () =>
        of({ items: [{ id: '1', code: 'FIN', name: 'Finance', isActive: true }] }),
    };

    const result = await new DepartmentApiReader(transport).readAll();

    expect(result).toEqual({
      ok: true,
      value: [{ id: '1', code: 'FIN', name: 'Finance', active: true }],
    });
  });

  it('maps RFC Problem Details without leaking the transport type', async () => {
    const transport: DepartmentsTransport = {
      getApiV2Departments: () =>
        throwError(
          () =>
            new HttpErrorResponse({
              status: 403,
              error: { code: 'auth.forbidden', detail: 'Denied' },
            }),
        ),
    };

    const result = await new DepartmentApiReader(transport).readAll();

    expect(result).toEqual({ ok: false, error: { kind: 'forbidden', code: 'auth.forbidden' } });
  });

  it('maps an AbortSignal to a cancelled application result', async () => {
    const transport: DepartmentsTransport = { getApiV2Departments: () => NEVER };
    const controller = new AbortController();
    const pending = new DepartmentApiReader(transport).readAll(controller.signal);
    controller.abort();

    expect(await pending).toEqual({
      ok: false,
      error: { kind: 'cancelled', code: 'request.cancelled' },
    });
  });
});
