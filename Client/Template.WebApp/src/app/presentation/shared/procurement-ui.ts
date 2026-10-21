import {
  ChangeDetectionStrategy,
  Component,
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
  template: `
    @if (error(); as problem) {
      <div class="feedback" role="alert">
        <strong>{{ messageKey(problem) | transloco }}</strong>
        <span
          >{{ problem.code }}
          @if (problem.ruleId) {
            · {{ problem.ruleId }}
          }
        </span>
        @if (problem.detail) {
          <span>{{ problem.detail }}</span>
        }
        @for (entry of fields(problem); track entry[0]) {
          <span>{{ entry[0] }}: {{ entry[1] }}</span>
        }
        @if (problem.traceId) {
          <small>Trace: {{ problem.traceId }}</small>
        }
        @if (problem.kind === 'version-mismatch' || problem.kind === 'conflict') {
          <button hlmBtn variant="outline" size="sm" type="button" (click)="reload.emit()">
            {{ 'common.reload' | transloco }}
          </button>
        }
      </div>
    }
  `,
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
@Component({
  selector: 'app-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `<span class="status-badge" [attr.data-status]="status()">{{
    'statuses.' + status() | transloco
  }}</span>`,
  styles: [
    `
      .status-badge {
        display: inline-block;
        padding: 0.125rem 0.5rem;
        border-radius: 999px;
        border: 1px solid var(--border);
        font-size: 0.875rem;
        background: var(--muted);
      }
      [data-status='Submitted'],
      [data-status='Open'],
      [data-status='Reserved'],
      [data-status='Active'] {
        border-color: var(--primary);
      }
      [data-status='Cancelled'],
      [data-status='Rejected'],
      [data-status='Expired'],
      [data-status='Released'],
      [data-status='Closed'] {
        color: var(--muted-foreground);
      }
    `,
  ],
})
export class StatusBadge {
  readonly status = input.required<string>();
}
