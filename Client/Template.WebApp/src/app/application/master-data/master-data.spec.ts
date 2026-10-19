import { describe, expect, it } from 'vitest';
import type { MasterDataPort } from './master-data';
import { MasterDataUseCases } from './master-data';

describe('MasterDataUseCases', () => {
  it('keeps paging intent in Application', async () => {
    let page = 0;
    const port = {
      list: async (
        _kind: Parameters<MasterDataPort['list']>[0],
        request: Parameters<MasterDataPort['list']>[1],
      ) => {
        page = request.page;
        return {
          ok: true as const,
          value: { items: [], page, pageSize: 20, totalItems: 0, totalPages: 0 },
        };
      },
    } as unknown as MasterDataPort;
    await new MasterDataUseCases(port).list('departments', { page: 2, pageSize: 20 });
    expect(page).toBe(2);
  });
});
