import { inject, Injectable, signal } from '@angular/core';
import type { DirectoryUser } from '@application/organization/organization-settings';
import { USER_DIRECTORY_PORT } from './ports';
import { SessionFacade } from './session.facade';

export type ActorLabel =
  | { readonly kind: 'system' }
  | { readonly kind: 'self'; readonly name: string; readonly id: string }
  | { readonly kind: 'named'; readonly name: string; readonly id: string }
  | { readonly kind: 'unknown'; readonly id: string };

/**
 * Human-readable actor names for timelines and the audit trail. The current user is always named; other users are
 * named only when the session may read the user directory (`access.users.manage`); otherwise the UI shows a neutral
 * label and keeps the ID as optional technical detail. Nothing here widens what the API returns.
 */
@Injectable({ providedIn: 'root' })
export class ActorDirectory {
  private readonly port = inject(USER_DIRECTORY_PORT);
  private readonly session = inject(SessionFacade);
  private readonly users = signal<ReadonlyMap<string, DirectoryUser>>(new Map());
  private requested = false;

  label(actorId: string | undefined | null): ActorLabel {
    if (!actorId) return { kind: 'system' };
    const user = this.session.user();
    if (user && user.id.toLowerCase() === actorId.toLowerCase())
      return { kind: 'self', name: user.displayName || user.userName, id: actorId };
    this.ensureLoaded();
    const known = this.users().get(actorId.toLowerCase());
    return known
      ? { kind: 'named', name: known.displayName, id: actorId }
      : { kind: 'unknown', id: actorId };
  }

  private ensureLoaded() {
    if (this.requested || !this.session.can('access.users.manage')) return;
    this.requested = true;
    void this.port.list({ page: 1, pageSize: 100 }).then((result) => {
      if (result.ok)
        this.users.set(new Map(result.value.map((item) => [item.id.toLowerCase(), item])));
    });
  }
}
