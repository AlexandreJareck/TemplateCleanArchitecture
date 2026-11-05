import { computed, effect, inject, Injectable, signal } from '@angular/core';
import type { OrganizationSettings } from '@application/organization/organization-settings';
import { DEFAULT_TIME_ZONE, isValidTimeZone } from '../../domain/date-display';
import { ORGANIZATION_SETTINGS_PORT } from './ports';
import { SessionFacade } from './session.facade';

/** Currency set used until (or if) the organization settings cannot be read. */
export const FALLBACK_CURRENCIES: readonly string[] = ['BRL', 'USD', 'EUR'];

/**
 * Organization display context (ORG-004): the business time zone used to render instants and the controlled currency
 * set. Loaded once per authenticated session (`organization.read`, held by every seeded role); until it arrives the
 * product default zone `America/Sao_Paulo` is used so rendering never falls back to the browser zone.
 */
@Injectable({ providedIn: 'root' })
export class OrganizationContext {
  private readonly port = inject(ORGANIZATION_SETTINGS_PORT);
  private readonly session = inject(SessionFacade);
  readonly settings = signal<OrganizationSettings | undefined>(undefined);
  readonly timeZone = computed(() => {
    const zone = this.settings()?.timeZone;
    return isValidTimeZone(zone) ? zone : DEFAULT_TIME_ZONE;
  });
  readonly baseCurrency = computed(() => this.settings()?.baseCurrency || 'BRL');
  readonly currencies = computed(() => {
    const supported = this.settings()?.supportedCurrencies ?? [];
    return supported.length > 0 ? supported : FALLBACK_CURRENCIES;
  });
  private loadedFor?: string;

  constructor() {
    effect(() => {
      const user = this.session.user();
      if (!user) {
        this.loadedFor = undefined;
        return;
      }
      if (this.loadedFor === user.id) return;
      this.loadedFor = user.id;
      void this.load();
    });
  }

  async load() {
    const result = await this.port.get();
    if (result.ok) this.settings.set(result.value);
  }
}
