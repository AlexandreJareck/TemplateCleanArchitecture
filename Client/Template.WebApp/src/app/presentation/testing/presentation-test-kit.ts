import { signal, type EnvironmentProviders, type Provider } from '@angular/core';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { success } from '@application/common/result';
import type { Page } from '@application/master-data/master-data';
import type { CurrentUser } from '@application/session/session';
import { provideTransloco, type Translation, type TranslocoLoader } from '@jsverse/transloco';
import { of } from 'rxjs';
import ptBr from '../../../../public/i18n/pt-BR.json';
import { ORGANIZATION_SETTINGS_PORT, USER_DIRECTORY_PORT } from '../state/ports';
import { SessionFacade } from '../state/session.facade';

/**
 * Test-only helpers for presentation component specs: translations (key echo, or the real pt-BR catalog when the
 * visible copy is under test), route data, a fake session with chosen permissions and controllable promises.
 */
class KeyTranslationLoader implements TranslocoLoader {
  getTranslation() {
    return of<Translation>({});
  }
}

class PortugueseTranslationLoader implements TranslocoLoader {
  getTranslation() {
    return of<Translation>(ptBr as Translation);
  }
}

export function provideTestTransloco(
  catalog: 'keys' | 'pt-BR' = 'keys',
): (Provider | EnvironmentProviders)[] {
  return [
    provideTransloco({
      config: {
        availableLangs: ['pt-BR'],
        defaultLang: 'pt-BR',
        missingHandler: { logMissingKey: false },
      },
      loader: catalog === 'pt-BR' ? PortugueseTranslationLoader : KeyTranslationLoader,
    }),
  ];
}

export function provideTestRoute(
  params: Record<string, string> = {},
  data: Record<string, unknown> = {},
  query: Record<string, string> = {},
): (Provider | EnvironmentProviders)[] {
  return [
    provideRouter([]),
    {
      provide: ActivatedRoute,
      useValue: {
        snapshot: {
          paramMap: convertToParamMap(params),
          queryParamMap: convertToParamMap(query),
          data,
        },
      },
    },
  ];
}

/** A signed-in session holding exactly `permissions`, plus organization/user-directory fakes. */
export function provideTestSession(
  permissions: readonly string[],
  user: Partial<CurrentUser> = {},
): Provider[] {
  const current: CurrentUser = {
    id: 'd0000000-0000-0000-0000-000000000001',
    userName: 'demo.user',
    displayName: 'Demo User',
    permissions: new Set(permissions),
    ...user,
  };
  return [
    {
      provide: SessionFacade,
      useValue: {
        user: signal(current),
        can: (permission: string) => current.permissions.has(permission),
      },
    },
    {
      provide: ORGANIZATION_SETTINGS_PORT,
      useValue: {
        get: async () =>
          success({
            displayName: 'Demo',
            baseCurrency: 'BRL',
            timeZone: 'America/Sao_Paulo',
            supportedCurrencies: ['BRL', 'USD', 'EUR'],
          }),
      },
    },
    { provide: USER_DIRECTORY_PORT, useValue: { list: async () => success([]) } },
  ];
}

export interface Deferred<T> {
  readonly promise: Promise<T>;
  resolve(value: T): void;
}

export function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => (resolve = done));
  return { promise, resolve };
}

export function page<T>(items: readonly T[]): Page<T> {
  return { items, page: 1, pageSize: 100, totalItems: items.length, totalPages: 1 };
}

/** Lets pending microtasks (resolved fakes) and change detection settle. */
export async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
  for (let round = 0; round < 3; round++) {
    await new Promise((done) => setTimeout(done, 0));
    fixture.detectChanges();
    await fixture.whenStable();
  }
}

export function text(element: Element | null | undefined) {
  return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
}
