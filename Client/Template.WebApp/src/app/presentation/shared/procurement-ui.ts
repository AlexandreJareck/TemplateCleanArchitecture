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
 * Consistent Problem Details feedback: stable code, safe detail, rule ID, trace, field messages and, for a stale
 * version (412/428) or a state conflict (409), an explicit reload action instead of silently overwriting.
 */
@Component({
  selector: 'app-problem-feedback',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, HlmButton],
  templateUrl: './procurement-ui.problem-feedback.html',
})
export class ProblemFeedback {
  readonly error = input<ApplicationError | undefined>();
  readonly reload = output<void>();

  messageKey(problem: ApplicationError) {
    return `problems.${problem.kind}`;
  }

  fields(problem: ApplicationError): [string, string][] {
    return Object.entries(problem.fieldErrors ?? {}).map(([field, messages]) => [
      field,
      messages.join(', '),
    ]);
  }
}

/** Localized status badge for requisitions, periods, allocations and reservations. */
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
  Failed: 'danger',
  Rejected: 'danger',
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
  protected readonly tone = computed(() => STATUS_TONES[this.status()] ?? 'neutral');
}
