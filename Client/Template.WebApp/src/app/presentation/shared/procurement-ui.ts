import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  Pipe,
  type PipeTransform,
} from '@angular/core';
import type { ApplicationError } from '@application/common/application-error';
import type { Money } from '@application/procurement/procurement';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { formatMoney } from '../../domain/money-display';

/** Formats server money (decimal string) with `Intl` for display only; values are never recomputed from it. */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  private readonly transloco = inject(TranslocoService);

  transform(value: Money | undefined | null): string {
    if (!value?.currency) return '—';
    return formatMoney(
      { amount: Number(value.amount), currency: value.currency },
      this.transloco.getActiveLang(),
    );
  }
}

/**
 * Consistent Problem Details feedback in the active language: a localized summary, the localized message of a known
 * stable code, and field problems named by their localized label. The stable code, rule ID, server detail and trace
 * ID stay available under an optional "technical details" disclosure for support, never as the main message. A stale
 * version (412/428) or a state conflict (409) offers an explicit reload instead of silently overwriting.
 */
@Component({
  selector: 'app-problem-feedback',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, HlmButton],
  templateUrl: './procurement-ui.problem-feedback.html',
  styles: [
    `
      .feedback {
        display: grid;
        gap: var(--space-2);
        padding: var(--space-4);
        border: 1px solid var(--destructive);
        border-radius: var(--radius-md);
        color: var(--destructive);
        margin-bottom: var(--space-4);
        overflow-wrap: anywhere;
      }
      .problem-fields {
        margin: 0;
        padding-inline-start: 1.25rem;
        list-style: disc;
      }
      .problem-field {
        font-weight: 600;
      }
      .problem-technical {
        color: var(--muted-foreground);
        font-size: 0.8125rem;
      }
      .problem-technical summary {
        cursor: pointer;
        width: fit-content;
      }
      .problem-technical dl {
        display: grid;
        grid-template-columns: max-content minmax(0, 1fr);
        gap: 0.25rem var(--space-3);
        margin: var(--space-2) 0 0;
      }
      .problem-technical dd {
        margin: 0;
      }
    `,
  ],
})
export class ProblemFeedback {
  private readonly transloco = inject(TranslocoService);
  readonly error = input<ApplicationError | undefined>();
  /** Optional API field name -> translation key of the visible label, for fields the page names differently. */
  readonly fieldLabels = input<Readonly<Record<string, string>>>({});
  readonly reload = output<void>();

  messageKey(problem: ApplicationError) {
    return `problems.${problem.kind}`;
  }

  /** Localized message for a known stable code; the English server detail stays under technical details. */
  summary(problem: ApplicationError): string | undefined {
    return this.optional(`problems.codes.${problem.code}`);
  }

  fields(problem: ApplicationError): [string, string][] {
    return Object.entries(problem.fieldErrors ?? {}).map(([field, messages]) => [
      this.fieldLabel(field),
      messages.length > 0
        ? [...new Set(messages.map((message) => this.fieldMessage(message)))].join(' ')
        : this.transloco.translate('validation.invalid'),
    ]);
  }

  private fieldLabel(field: string): string {
    const explicit = this.fieldLabels()[field];
    if (explicit) return this.transloco.translate(explicit);
    const normalized = field.replace(/^\$\./, '').replace(/\[\d+\]/g, '');
    const key = normalized.charAt(0).toLowerCase() + normalized.slice(1);
    return (
      this.optional(`problemFields.${key}`) ??
      this.transloco.translate<string>('problemFields.unknown')
    );
  }

  /** Server validation text is English and technical; show a localized category instead. */
  private fieldMessage(message: string) {
    if (/required|must not be empty|is empty/i.test(message))
      return this.transloco.translate('validation.required');
    if (/length|characters|too long|too short/i.test(message))
      return this.transloco.translate('validation.length');
    if (/format|pattern|valid/i.test(message)) return this.transloco.translate('validation.format');
    return this.transloco.translate('validation.invalid');
  }

  private optional(key: string): string | undefined {
    const value = this.transloco.translate(key);
    return value && value !== key ? value : undefined;
  }
}

/** Localized status badge for every workflow status; `prefix` selects an entity-specific translation group. */
type StatusTone = 'success' | 'info' | 'warning' | 'danger' | 'neutral';

const STATUS_TONES: Readonly<Record<string, StatusTone>> = {
  Active: 'success',
  Approved: 'success',
  Awarded: 'success',
  Committed: 'success',
  Complete: 'success',
  Current: 'success',
  Ordered: 'success',
  Ready: 'success',
  InSourcing: 'info',
  Open: 'info',
  Planned: 'info',
  Published: 'info',
  Reserved: 'info',
  Running: 'info',
  Submitted: 'info',
  AwardPending: 'warning',
  Incomplete: 'warning',
  Pending: 'warning',
  PendingApproval: 'warning',
  Issued: 'info',
  PartiallyReceived: 'info',
  Posted: 'success',
  Completed: 'success',
  Available: 'success',
  Suspended: 'warning',
  Failed: 'danger',
  Rejected: 'danger',
  Invalidated: 'danger',
};

@Component({
  selector: 'app-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  templateUrl: './procurement-ui.status-badge.html',
  styles: [
    `
      .status-badge {
        display: inline-flex;
        align-items: center;
        gap: 0.375rem;
        padding: 0.125rem 0.625rem;
        border-radius: 999px;
        font-size: 0.8125rem;
        font-weight: 600;
        white-space: nowrap;
        background: var(--neutral-soft);
        color: var(--muted-foreground);
      }
      .status-badge::before {
        content: '';
        width: 0.4375rem;
        height: 0.4375rem;
        border-radius: 50%;
        background: currentColor;
      }
      [data-tone='success'] {
        background: var(--success-soft);
        color: var(--success);
      }
      [data-tone='info'] {
        background: var(--info-soft);
        color: var(--info);
      }
      [data-tone='warning'] {
        background: var(--warning-soft);
        color: var(--warning);
      }
      [data-tone='danger'] {
        background: var(--danger-soft);
        color: var(--destructive);
      }
    `,
  ],
})
export class StatusBadge {
  readonly status = input.required<string>();
  /** Translation group; entity-specific groups keep grammatical gender right (e.g. supplier `Ativo`). */
  readonly prefix = input('statuses');
  protected readonly tone = computed(() => STATUS_TONES[this.status()] ?? 'neutral');
  /** Unknown statuses fall back to the raw value rather than showing a translation key. */
  protected readonly key = computed(() => `${this.prefix()}.${this.status()}`);
}
