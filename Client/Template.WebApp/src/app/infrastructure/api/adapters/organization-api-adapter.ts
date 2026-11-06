import { inject, Injectable } from '@angular/core';
import { failure, success, type Result } from '@application/common/result';
import type {
  DirectoryUser,
  OrganizationSettings,
  OrganizationSettingsPort,
  UserDirectoryPort,
} from '@application/organization/organization-settings';
import { firstValueFrom, fromEvent, takeUntil, type Observable } from 'rxjs';
import type { OrganizationSettingsView } from '../generated/models/organizationSettingsView';
import type { UserSummary } from '../generated/models/userSummary';
import { OrganizationSettingsService } from '../generated/organization-settings/organization-settings.service';
import { UsersService } from '../generated/users/users.service';
import { mapProblem } from '../../http/problem-mapper';

/** Organization settings (`organization.read`) and the user directory (`access.users.manage`) for display context. */
@Injectable({ providedIn: 'root' })
export class OrganizationApiAdapter implements OrganizationSettingsPort, UserDirectoryPort {
  private readonly settings = inject(OrganizationSettingsService);
  private readonly users = inject(UsersService);

  get(signal?: AbortSignal) {
    return execute(this.settings.getApiV2OrganizationSettings(), signal, mapSettings);
  }

  list(request: { page: number; pageSize: number; q?: string }, signal?: AbortSignal) {
    return execute(
      this.users.getApiV2Users({ page: request.page, pageSize: request.pageSize, q: request.q }),
      signal,
      (value) => (value.items ?? []).map(mapUser),
    );
  }
}

export function mapSettings(value: OrganizationSettingsView): OrganizationSettings {
  return {
    displayName: value.displayName ?? '',
    baseCurrency: value.baseCurrency ?? 'BRL',
    timeZone: value.timeZone ?? '',
    supportedCurrencies: value.supportedCurrencies ?? [],
  };
}

function mapUser(value: UserSummary): DirectoryUser {
  return {
    id: value.userId ?? '',
    userName: value.userName ?? '',
    displayName: value.displayName || value.userName || '',
    active: value.isActive ?? false,
  };
}

async function execute<T, U>(
  source: Observable<T>,
  signal: AbortSignal | undefined,
  map: (value: T) => U,
): Promise<Result<U>> {
  try {
    const bounded = signal ? source.pipe(takeUntil(fromEvent(signal, 'abort'))) : source;
    return success(map(await firstValueFrom(bounded)));
  } catch (error) {
    return failure<U>(mapProblem(error, signal));
  }
}
