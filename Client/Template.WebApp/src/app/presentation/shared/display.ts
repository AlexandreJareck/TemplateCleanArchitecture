import { ChangeDetectionStrategy, Component, computed, inject, input, Pipe } from '@angular/core';
import type { PipeTransform } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { formatFileSize, formatInstant, formatLocalDate } from '../../domain/date-display';
import { ActorDirectory } from '../state/actor-directory';
import { OrganizationContext } from '../state/organization.context';

/**
 * Shared localized display primitives (F-06): business dates as the same calendar day, UTC instants in the
 * organization time zone, and actors by name. The pipes are impure on purpose: they follow language and organization
 * time-zone changes; formatters are cached, so a call is a lookup plus `format`.
 */
@Pipe({ name: 'localDate', pure: false })
export class LocalDatePipe implements PipeTransform {
  private readonly transloco = inject(TranslocoService);

  transform(value: string | undefined | null): string {
    return formatLocalDate(value, this.transloco.getActiveLang());
  }
}

@Pipe({ name: 'instant', pure: false })
export class InstantPipe implements PipeTransform {
  private readonly transloco = inject(TranslocoService);
  private readonly organization = inject(OrganizationContext);

  transform(
    value: string | undefined | null,
    precision: 'minutes' | 'seconds' = 'minutes',
  ): string {
    return formatInstant(
      value,
      this.transloco.getActiveLang(),
      this.organization.timeZone(),
      precision === 'seconds',
    );
  }
}

@Pipe({ name: 'fileSize', pure: false })
export class FileSizePipe implements PipeTransform {
  private readonly transloco = inject(TranslocoService);

  transform(value: number | undefined | null): string {
    return formatFileSize(value, this.transloco.getActiveLang());
  }
}

/** Actor name; the user ID is never the visible text and is available only as a hover/assistive hint. */
@Component({
  selector: 'app-actor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `@let current = label();
    <span
      class="actor"
      [attr.data-actor-kind]="current.kind"
      [attr.title]="
        current.kind === 'system' ? null : ('actors.idHint' | transloco: { id: current.id })
      "
    >
      @switch (current.kind) {
        @case ('system') {
          {{ 'actors.system' | transloco }}
        }
        @case ('self') {
          {{ 'actors.self' | transloco: { name: current.name } }}
        }
        @case ('named') {
          {{ current.name }}
        }
        @default {
          {{ 'actors.otherUser' | transloco }}
        }
      }
    </span>`,
})
export class ActorName {
  private readonly directory = inject(ActorDirectory);
  readonly actorId = input<string | undefined | null>(undefined);
  protected readonly label = computed(() => this.directory.label(this.actorId()));
}
