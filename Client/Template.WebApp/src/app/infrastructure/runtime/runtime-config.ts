export interface RuntimeConfig {
  readonly apiBaseUrl: string;
  readonly buildLabel: string;
  readonly features: Readonly<Record<string, boolean>>;
}

export const DEFAULT_RUNTIME_CONFIG: RuntimeConfig = {
  apiBaseUrl: '',
  buildLabel: 'development',
  features: {},
};

export async function loadRuntimeConfig(fetcher: typeof fetch = fetch): Promise<RuntimeConfig> {
  const response = await fetcher('/config/runtime-config.json', {
    cache: 'no-store',
    credentials: 'same-origin',
  });
  if (!response.ok) throw new Error(`Runtime configuration failed with HTTP ${response.status}.`);
  const candidate: unknown = await response.json();
  if (!isRuntimeConfig(candidate))
    throw new Error('Runtime configuration has an invalid public shape.');
  return candidate;
}

function isRuntimeConfig(value: unknown): value is RuntimeConfig {
  if (typeof value !== 'object' || value === null) return false;
  const record = value as Record<string, unknown>;
  return (
    typeof record['apiBaseUrl'] === 'string' &&
    typeof record['buildLabel'] === 'string' &&
    typeof record['features'] === 'object' &&
    record['features'] !== null
  );
}
