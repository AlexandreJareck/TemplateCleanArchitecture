import type { Result } from '../common/result';

/** Organization-wide display context (ORG-004): business time zone and the controlled currency set. */
export interface OrganizationSettings {
  readonly displayName: string;
  readonly baseCurrency: string;
  readonly timeZone: string;
  readonly supportedCurrencies: readonly string[];
}

export interface OrganizationSettingsPort {
  get(signal?: AbortSignal): Promise<Result<OrganizationSettings>>;
}

/** A user as the user directory exposes it (display information only). */
export interface DirectoryUser {
  readonly id: string;
  readonly userName: string;
  readonly displayName: string;
  readonly active: boolean;
}

/** User directory lookup (ORG-006); the API restricts it to `access.users.manage`. */
export interface UserDirectoryPort {
  list(
    request: { page: number; pageSize: number; q?: string },
    signal?: AbortSignal,
  ): Promise<Result<readonly DirectoryUser[]>>;
}
