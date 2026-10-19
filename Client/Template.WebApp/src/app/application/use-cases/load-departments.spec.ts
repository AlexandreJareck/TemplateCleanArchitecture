import { describe, expect, it } from 'vitest';
import type { DepartmentReader } from '../ports/department-reader';
import { LoadDepartments } from './load-departments';

describe('LoadDepartments', () => {
  it('delegates through the application port', async () => {
    const reader: DepartmentReader = {
      readAll: async () => ({
        ok: true,
        value: [{ id: '1', code: 'FIN', name: 'Finance', active: true }],
      }),
    };

    const result = await new LoadDepartments(reader).execute();

    expect(result.ok).toBe(true);
  });
});
