import { describe, expect, it } from 'vitest';
import { loadRuntimeConfig } from './runtime-config';

describe('loadRuntimeConfig', () => {
  it('accepts only public typed configuration', async () => {
    const fetcher = async () =>
      new Response(JSON.stringify({ apiBaseUrl: '', buildLabel: 'test', features: {} }));
    await expect(loadRuntimeConfig(fetcher as typeof fetch)).resolves.toMatchObject({
      buildLabel: 'test',
    });
  });
});
