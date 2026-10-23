import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import type { SupplierSummary } from '@application/master-data/master-data';
import { RequisitionUseCases } from '@application/procurement/procurement';
import {
  SourcingUseCases,
  type ComparisonEntry,
  type QuoteInput,
  type QuoteSource,
  type Rfq,
  type RfqAction,
  type RfqHistoryEntry,
} from '@application/sourcing/sourcing';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { ActionConfirmation, type ActionConfirmationRequest } from '../shared/action-confirmation';
import { MoneyPipe, ProblemFeedback, StatusBadge } from '../shared/procurement-ui';
import { MASTER_DATA_PORT, SOURCING_PORT } from '../state/ports';

type Pending = 'cancel' | 'close-early' | 'withdraw-quote' | 'award' | undefined;

interface QuoteForm {
  supplierId: string;
  quoteId: string;
  currency: string;
  lines: { rfqLineNumber: number; quantityOffered: string; unitPrice: string }[];
  freight: string;
  discount: string;
  taxesIncluded: string;
  validUntil: string;
  deliveryDays: string;
  paymentTerms: string;
  notes: string;
  source: QuoteSource;
  receivedAt: string;
}

const SOURCES: readonly QuoteSource[] = ['Email', 'Phone', 'Document', 'Other'];

/**
 * RFQ workspace (SRC-001..022): requisition snapshot lines, invitations with supplier snapshots, publication with the
 * deadline and the Direct waiver reason, buyer-entered quote revisions, close, the server comparison (raw and
 * normalized totals, rate provenance, coverage, validity reasons, rank, warnings), manual rate, recommendation and
 * award confirmation with the waiver reasons the server asks for, history and conflict recovery. The browser formats
 * and previews only; ranks, conversions and totals come from the API.
 */
@Component({
  selector: 'app-rfq-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    TranslocoPipe,
    HlmButton,
    HlmInput,
    ActionConfirmation,
    MoneyPipe,
    ProblemFeedback,
    StatusBadge,
  ],
  template: `
    <header class="page-title">
      <p>
        {{ 'shell.breadcrumbHome' | transloco }} /
        <a routerLink="/sourcing">{{ 'nav.sourcing' | transloco }}</a> / {{ rfq()?.number }}
      </p>
      <h1>{{ rfq()?.number ?? ('common.loading' | transloco) }}</h1>
    </header>
    <app-problem-feedback [error]="error()" (reload)="load()" />
    @if (notice()) {
      <p class="notice" role="status">{{ notice()! | transloco }}</p>
    }
    @if (rfq(); as current) {
      <section class="panel" aria-labelledby="rfq-summary">
        <h2 id="rfq-summary">{{ 'sourcing.summary' | transloco }}</h2>
        <dl class="summary-grid">
          <div>
            <dt>{{ 'fields.status' | transloco }}</dt>
            <dd><app-status-badge [status]="current.status" /></dd>
          </div>
          <div>
            <dt>{{ 'sourcing.method' | transloco }}</dt>
            <dd>{{ 'sourcing.methods.' + current.method | transloco }}</dd>
          </div>
          <div>
            <dt>{{ 'sourcing.round' | transloco }}</dt>
            <dd>{{ current.roundNumber }}</dd>
          </div>
          <div>
            <dt>{{ 'requisition.number' | transloco }}</dt>
            <dd>
              <a [routerLink]="['/requisitions', current.requisition.id]">{{
                current.requisition.number
              }}</a>
              · rev. {{ current.requisition.revision }}
            </dd>
          </div>
          <div>
            <dt>{{ 'requisition.serverTotal' | transloco }}</dt>
            <dd>{{ current.requisition.estimatedTotal | money }}</dd>
          </div>
          <div>
            <dt>{{ 'sourcing.threshold' | transloco }}</dt>
            <dd data-testid="threshold">
              {{
                (current.aboveThreshold ? 'sourcing.aboveThreshold' : 'sourcing.belowThreshold')
                  | transloco
              }}
            </dd>
          </div>
          <div>
            <dt>{{ 'sourcing.requiredInvitations' | transloco }}</dt>
            <dd>{{ current.requiredInvitations }}</dd>
          </div>
          <div>
            <dt>{{ 'sourcing.deadline' | transloco }}</dt>
            <dd data-testid="deadline">{{ current.deadline ?? '—' }}</dd>
          </div>
          <div>
            <dt>{{ 'sourcing.policy' | transloco }}</dt>
            <dd>SourcingPolicy v{{ current.policyVersion }}</dd>
          </div>
        </dl>
        @if (current.closeReason) {
          <p class="notice">{{ 'sourcing.closedEarly' | transloco }}: {{ current.closeReason }}</p>
        }
        @if (current.cancellationReason) {
          <p class="warning">
            {{ 'statuses.Cancelled' | transloco }}: {{ current.cancellationReason }}
          </p>
        }
      </section>

      <section class="panel" aria-labelledby="rfq-lines">
        <h2 id="rfq-lines">{{ 'requisition.lines' | transloco }}</h2>
        <div
          class="table-scroll"
          role="region"
          tabindex="0"
          [attr.aria-label]="'requisition.linesTable' | transloco"
        >
          <table class="responsive-table">
            <thead>
              <tr>
                <th>#</th>
                <th>{{ 'requisition.description' | transloco }}</th>
                <th>{{ 'requisition.quantity' | transloco }}</th>
                <th>{{ 'requisition.unitPrice' | transloco }}</th>
              </tr>
            </thead>
            <tbody>
              @for (line of current.lines; track line.lineNumber) {
                <tr>
                  <td data-label="#">{{ line.lineNumber }}</td>
                  <td [attr.data-label]="'requisition.description' | transloco">
                    {{ line.description }}
                  </td>
                  <td [attr.data-label]="'requisition.quantity' | transloco">
                    {{ line.quantity }} {{ line.unitCode }}
                  </td>
                  <td [attr.data-label]="'requisition.unitPrice' | transloco">
                    {{ line.estimatedUnitPrice | money }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      <section class="panel" aria-labelledby="rfq-invitations">
        <h2 id="rfq-invitations">{{ 'sourcing.invitations' | transloco }}</h2>
        <ul class="plain-list">
          @for (invitation of current.invitations; track $index) {
            <li [class.muted]="!invitation.isActive">
              {{ invitation.supplierLegalName }} · {{ invitation.supplierTaxId }} ·
              {{ 'sourcing.statusAtInvitation' | transloco }}:
              {{ invitation.supplierStatusAtInvitation }}
              @if (!invitation.isActive) {
                · {{ 'sourcing.removed' | transloco }}
              } @else if (can('remove-invitation')) {
                <button
                  hlmBtn
                  size="sm"
                  variant="ghost"
                  type="button"
                  (click)="removeInvitation(invitation.supplierId)"
                >
                  {{ 'sourcing.remove' | transloco }}
                </button>
              }
            </li>
          }
        </ul>
        @if (can('invite')) {
          <div class="toolbar">
            <label for="inviteSupplier">{{ 'sourcing.supplier' | transloco }}</label>
            <select
              id="inviteSupplier"
              class="select"
              [value]="inviteSupplier()"
              (change)="inviteSupplier.set($any($event.target).value)"
            >
              <option value="">—</option>
              @for (supplier of eligibleSuppliers(); track supplier.id) {
                <option [value]="supplier.id">{{ supplier.legalName }}</option>
              }
            </select>
            <button hlmBtn type="button" [disabled]="!inviteSupplier()" (click)="invite()">
              {{ 'sourcing.invite' | transloco }}
            </button>
          </div>
        }
      </section>

      @if (can('publish')) {
        <section class="panel" aria-labelledby="rfq-publish">
          <h2 id="rfq-publish">{{ 'sourcing.publish' | transloco }}</h2>
          <div class="form-grid">
            <label for="deadline">{{ 'sourcing.deadline' | transloco }}</label>
            <input
              id="deadline"
              hlmInput
              type="datetime-local"
              [value]="deadline()"
              (input)="deadline.set($any($event.target).value)"
            />
            @if (current.requiredPublishWaiver) {
              <label for="waiverReason"
                >{{ 'sourcing.waiverReason' | transloco }} ({{
                  current.requiredPublishWaiver
                }})</label
              >
              <input
                id="waiverReason"
                hlmInput
                [value]="waiverReason()"
                (input)="waiverReason.set($any($event.target).value)"
              />
            }
          </div>
          <p class="hint">{{ 'sourcing.publishHelp' | transloco }}</p>
          <button hlmBtn type="button" [disabled]="!deadline()" (click)="publish()">
            {{ 'sourcing.publish' | transloco }}
          </button>
        </section>
      }

      @if (can('extend-deadline') || can('remind')) {
        <section class="panel" aria-labelledby="rfq-deadline">
          <h2 id="rfq-deadline">{{ 'sourcing.deadlineActions' | transloco }}</h2>
          @if (can('extend-deadline')) {
            <div class="form-grid">
              <label for="newDeadline">{{ 'sourcing.newDeadline' | transloco }}</label>
              <input
                id="newDeadline"
                hlmInput
                type="datetime-local"
                [value]="deadline()"
                (input)="deadline.set($any($event.target).value)"
              />
              <label for="extendReason">{{ 'common.reason' | transloco }}</label>
              <input
                id="extendReason"
                hlmInput
                [value]="reason()"
                (input)="reason.set($any($event.target).value)"
              />
            </div>
            <button
              hlmBtn
              variant="outline"
              type="button"
              [disabled]="!deadline() || !reason().trim()"
              (click)="extend()"
            >
              {{ 'sourcing.extend' | transloco }}
            </button>
          }
          @if (can('remind')) {
            <button
              hlmBtn
              variant="outline"
              type="button"
              [disabled]="pendingSuppliers().length === 0"
              (click)="remind()"
            >
              {{ 'sourcing.remind' | transloco }} ({{ pendingSuppliers().length }})
            </button>
          }
        </section>
      }

      @if (current.commercialDetailsVisible) {
        <section class="panel" aria-labelledby="rfq-quotes">
          <h2 id="rfq-quotes">{{ 'sourcing.quotes' | transloco }}</h2>
          @for (quote of current.quotes; track quote.id) {
            <article class="quote">
              <h3>
                {{ quote.supplierLegalName }} · <app-status-badge [status]="quote.latestStatus" />
              </h3>
              <ol>
                @for (revision of quote.revisions; track revision.id) {
                  <li>
                    rev. {{ revision.revisionNumber }} ·
                    <app-status-badge [status]="revision.status" /> · {{ revision.total | money }} ·
                    {{ 'sourcing.validUntil' | transloco }} {{ revision.validUntil }} ·
                    {{ revision.deliveryDays }} {{ 'sourcing.days' | transloco }} ·
                    {{ revision.paymentTerms }} ·
                    {{ 'sourcing.sources.' + revision.source | transloco }} ·
                    {{ 'sourcing.received' | transloco }} {{ revision.receivedAt }}
                    @if (revision.taxesIncluded.amount !== '0.00') {
                      · {{ 'sourcing.taxesIncluded' | transloco }}
                      {{ revision.taxesIncluded | money }}
                    }
                    @if (revision.withdrawalReason) {
                      · “{{ revision.withdrawalReason }}”
                    }
                  </li>
                }
              </ol>
              <div class="actions">
                @if (can('revise-quote')) {
                  <button
                    hlmBtn
                    size="sm"
                    variant="outline"
                    type="button"
                    (click)="editQuote(quote.id, quote.supplierId)"
                  >
                    {{ 'sourcing.revise' | transloco }}
                  </button>
                }
                @if (can('withdraw-quote') && quote.latestStatus === 'Current') {
                  <button
                    hlmBtn
                    size="sm"
                    variant="outline"
                    type="button"
                    (click)="askWithdraw(quote.id)"
                  >
                    {{ 'sourcing.withdrawQuote' | transloco }}
                  </button>
                }
              </div>
            </article>
          } @empty {
            <p class="empty">{{ 'sourcing.noQuotes' | transloco }}</p>
          }

          @if (can('enter-quote') && quoteForm(); as form) {
            <form
              class="editor"
              aria-labelledby="quote-form-title"
              (submit)="$event.preventDefault(); saveQuote()"
            >
              <h3 id="quote-form-title">
                {{ (form.quoteId ? 'sourcing.reviseQuote' : 'sourcing.enterQuote') | transloco }}
              </h3>
              <div class="form-grid">
                @if (!form.quoteId) {
                  <label for="quoteSupplier">{{ 'sourcing.supplier' | transloco }}</label>
                  <select
                    id="quoteSupplier"
                    class="select"
                    [value]="form.supplierId"
                    (change)="patch({ supplierId: $any($event.target).value })"
                  >
                    <option value="">—</option>
                    @for (invitation of pendingSuppliers(); track invitation.supplierId) {
                      <option [value]="invitation.supplierId">
                        {{ invitation.supplierLegalName }}
                      </option>
                    }
                  </select>
                }
                <label for="quoteCurrency">{{ 'sourcing.currency' | transloco }}</label>
                <input
                  id="quoteCurrency"
                  hlmInput
                  [value]="form.currency"
                  (input)="patch({ currency: $any($event.target).value })"
                />
                @for (line of form.lines; track line.rfqLineNumber; let index = $index) {
                  <label [for]="'qty-' + line.rfqLineNumber"
                    >{{ 'sourcing.quantityOffered' | transloco }} #{{ line.rfqLineNumber }}</label
                  >
                  <input
                    [id]="'qty-' + line.rfqLineNumber"
                    hlmInput
                    inputmode="decimal"
                    [value]="line.quantityOffered"
                    (input)="patchLine(index, { quantityOffered: $any($event.target).value })"
                  />
                  <label [for]="'price-' + line.rfqLineNumber"
                    >{{ 'requisition.unitPrice' | transloco }} #{{ line.rfqLineNumber }}</label
                  >
                  <input
                    [id]="'price-' + line.rfqLineNumber"
                    hlmInput
                    inputmode="decimal"
                    [value]="line.unitPrice"
                    (input)="patchLine(index, { unitPrice: $any($event.target).value })"
                  />
                }
                <label for="freight">{{ 'sourcing.freight' | transloco }}</label>
                <input
                  id="freight"
                  hlmInput
                  inputmode="decimal"
                  [value]="form.freight"
                  (input)="patch({ freight: $any($event.target).value })"
                />
                <label for="discount">{{ 'sourcing.discount' | transloco }}</label>
                <input
                  id="discount"
                  hlmInput
                  inputmode="decimal"
                  [value]="form.discount"
                  (input)="patch({ discount: $any($event.target).value })"
                />
                <label for="taxes">{{ 'sourcing.taxesIncluded' | transloco }}</label>
                <input
                  id="taxes"
                  hlmInput
                  inputmode="decimal"
                  [value]="form.taxesIncluded"
                  (input)="patch({ taxesIncluded: $any($event.target).value })"
                />
                <label for="validUntil">{{ 'sourcing.validUntil' | transloco }}</label>
                <input
                  id="validUntil"
                  hlmInput
                  type="date"
                  [value]="form.validUntil"
                  (input)="patch({ validUntil: $any($event.target).value })"
                />
                <label for="deliveryDays">{{ 'sourcing.deliveryDays' | transloco }}</label>
                <input
                  id="deliveryDays"
                  hlmInput
                  inputmode="numeric"
                  [value]="form.deliveryDays"
                  (input)="patch({ deliveryDays: $any($event.target).value })"
                />
                <label for="paymentTerms">{{ 'sourcing.paymentTerms' | transloco }}</label>
                <input
                  id="paymentTerms"
                  hlmInput
                  [value]="form.paymentTerms"
                  (input)="patch({ paymentTerms: $any($event.target).value })"
                />
                <label for="source">{{ 'sourcing.source' | transloco }}</label>
                <select
                  id="source"
                  class="select"
                  [value]="form.source"
                  (change)="patch({ source: $any($event.target).value })"
                >
                  @for (source of sources; track source) {
                    <option [value]="source">{{ 'sourcing.sources.' + source | transloco }}</option>
                  }
                </select>
                <label for="receivedAt">{{ 'sourcing.received' | transloco }}</label>
                <input
                  id="receivedAt"
                  hlmInput
                  type="datetime-local"
                  [value]="form.receivedAt"
                  (input)="patch({ receivedAt: $any($event.target).value })"
                />
                <label for="notes">{{ 'sourcing.notes' | transloco }}</label>
                <input
                  id="notes"
                  hlmInput
                  [value]="form.notes"
                  (input)="patch({ notes: $any($event.target).value })"
                />
              </div>
              <p class="hint" data-testid="quote-preview">
                {{ 'sourcing.preview' | transloco }} — {{ 'requisition.previewTotal' | transloco }}:
                {{ quotePreview() }} {{ form.currency }}
              </p>
              <div class="actions">
                <button hlmBtn type="submit" [disabled]="!form.quoteId && !form.supplierId">
                  {{ 'common.save' | transloco }}
                </button>
                <button hlmBtn variant="outline" type="button" (click)="resetQuote()">
                  {{ 'common.cancel' | transloco }}
                </button>
              </div>
            </form>
          }
        </section>
      }

      @if (current.comparison; as comparison) {
        <section class="panel" aria-labelledby="rfq-comparison">
          <h2 id="rfq-comparison">
            {{ 'sourcing.comparison' | transloco }} #{{ comparison.sequence }} ·
            <app-status-badge [status]="comparison.status" />
          </h2>
          <p>
            {{ 'sourcing.validQuotes' | transloco }}: {{ comparison.validQuoteCount }} /
            {{ comparison.requiredValidQuotes }} · {{ 'sourcing.baseCurrency' | transloco }}
            {{ comparison.baseCurrency }} · {{ comparison.closeLocalDate }}
          </p>
          @for (warning of comparison.warnings; track warning) {
            <p class="warning" data-testid="comparison-warning">{{ warningLabel(warning) }}</p>
          }
          <div
            class="table-scroll"
            role="region"
            tabindex="0"
            [attr.aria-label]="'sourcing.comparison' | transloco"
          >
            <table class="responsive-table" data-testid="comparison-table">
              <thead>
                <tr>
                  <th>{{ 'sourcing.rank' | transloco }}</th>
                  <th>{{ 'sourcing.supplier' | transloco }}</th>
                  <th>{{ 'sourcing.rawTotal' | transloco }}</th>
                  <th>{{ 'sourcing.rate' | transloco }}</th>
                  <th>{{ 'sourcing.normalizedTotal' | transloco }}</th>
                  <th>{{ 'sourcing.deliveryDays' | transloco }}</th>
                  <th>{{ 'sourcing.coverage' | transloco }}</th>
                  <th>{{ 'sourcing.validity' | transloco }}</th>
                  <th>{{ 'common.actions' | transloco }}</th>
                </tr>
              </thead>
              <tbody>
                @for (entry of entries(); track entry.quoteRevisionId) {
                  <tr
                    [class.recommended]="
                      entry.quoteRevisionId === comparison.recommendedQuoteRevisionId
                    "
                  >
                    <td [attr.data-label]="'sourcing.rank' | transloco">{{ entry.rank ?? '—' }}</td>
                    <td [attr.data-label]="'sourcing.supplier' | transloco">
                      {{ entry.supplierLegalName }} · rev. {{ entry.revisionNumber }}
                      @if (entry.quoteRevisionId === comparison.recommendedQuoteRevisionId) {
                        <strong> · {{ 'sourcing.recommended' | transloco }}</strong>
                      }
                    </td>
                    <td [attr.data-label]="'sourcing.rawTotal' | transloco">
                      {{ entry.rawTotal | money }}
                    </td>
                    <td [attr.data-label]="'sourcing.rate' | transloco">{{ entry.rate ?? '—' }}</td>
                    <td [attr.data-label]="'sourcing.normalizedTotal' | transloco">
                      {{ entry.normalizedTotal | money }}
                    </td>
                    <td [attr.data-label]="'sourcing.deliveryDays' | transloco">
                      {{ entry.deliveryDays }}
                    </td>
                    <td [attr.data-label]="'sourcing.coverage' | transloco">
                      {{ entry.coveredLines }}/{{ entry.totalLines }}
                    </td>
                    <td [attr.data-label]="'sourcing.validity' | transloco">
                      @if (entry.isValid) {
                        {{ 'sourcing.valid' | transloco }}
                      } @else {
                        @for (reason of entry.invalidReasons; track reason) {
                          <span class="tag">{{ 'sourcing.invalid.' + reason | transloco }}</span>
                        }
                      }
                    </td>
                    <td>
                      @if (can('award') && entry.rank) {
                        <button hlmBtn size="sm" type="button" (click)="askAward(entry)">
                          {{ 'sourcing.select' | transloco }}
                        </button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (can('record-rate')) {
            <form
              class="editor"
              aria-labelledby="manual-rate-title"
              (submit)="$event.preventDefault(); recordRate()"
            >
              <h3 id="manual-rate-title">{{ 'sourcing.manualRate' | transloco }}</h3>
              <p class="hint">{{ 'sourcing.manualRateHelp' | transloco }}</p>
              <div class="form-grid">
                <label for="rateCurrency">{{ 'sourcing.currency' | transloco }}</label>
                <select
                  id="rateCurrency"
                  class="select"
                  [value]="rateCurrency()"
                  (change)="rateCurrency.set($any($event.target).value)"
                >
                  @for (currency of comparison.missingRateCurrencies; track currency) {
                    <option [value]="currency">{{ currency }}</option>
                  }
                </select>
                <label for="rateValue">{{ 'sourcing.rate' | transloco }}</label>
                <input
                  id="rateValue"
                  hlmInput
                  inputmode="decimal"
                  [value]="rateValue()"
                  (input)="rateValue.set($any($event.target).value)"
                />
                <label for="rateDate">{{ 'sourcing.rateDate' | transloco }}</label>
                <input
                  id="rateDate"
                  hlmInput
                  type="date"
                  [value]="rateDate()"
                  (input)="rateDate.set($any($event.target).value)"
                />
                <label for="rateReason">{{ 'common.reason' | transloco }}</label>
                <input
                  id="rateReason"
                  hlmInput
                  [value]="reason()"
                  (input)="reason.set($any($event.target).value)"
                />
              </div>
              <button
                hlmBtn
                type="submit"
                [disabled]="!rateValue() || !rateDate() || !reason().trim()"
              >
                {{ 'sourcing.recordRate' | transloco }}
              </button>
            </form>
          }
        </section>
      }

      @if (current.exchangeRates.length > 0) {
        <section class="panel" aria-labelledby="rfq-rates">
          <h2 id="rfq-rates">{{ 'sourcing.rates' | transloco }}</h2>
          <ul class="plain-list">
            @for (rate of current.exchangeRates; track $index) {
              <li>
                1 {{ rate.fromCurrency }} = {{ rate.rate }} {{ rate.toCurrency }} ·
                {{ rate.source }} · {{ rate.quoteDate }} · {{ rate.purpose }}
              </li>
            }
          </ul>
        </section>
      }

      @if (current.awards.length > 0 || current.waivers.length > 0) {
        <section class="panel" aria-labelledby="rfq-awards">
          <h2 id="rfq-awards">{{ 'sourcing.award' | transloco }}</h2>
          @for (award of current.awards; track award.id) {
            <p data-testid="award">
              <app-status-badge [status]="award.status" /> {{ award.supplierLegalName }}
              @if (award.awardedTotal) {
                · {{ award.awardedTotal | money }} · {{ award.normalizedTotal | money }} ·
                {{ award.requisitionCurrencyTotal | money }}
              }
              @if (award.justification) {
                · “{{ award.justification }}”
              }
            </p>
          }
          @for (waiver of current.waivers; track $index) {
            <p class="notice">{{ waiver.type }} ({{ waiver.ruleId }}): {{ waiver.reason }}</p>
          }
        </section>
      }

      <section class="panel" aria-labelledby="rfq-history">
        <h2 id="rfq-history">{{ 'requisition.timeline' | transloco }}</h2>
        <ol class="timeline">
          @for (entry of history(); track entry.sequence) {
            <li>
              <strong>{{ 'sourcing.actions.' + entry.action | transloco }}</strong>
              @if (entry.from && entry.from !== entry.to) {
                · {{ 'statuses.' + entry.from | transloco }} →
                {{ 'statuses.' + entry.to | transloco }}
              }
              @if (entry.reason) {
                · “{{ entry.reason }}”
              }
              <br /><small
                >{{ entry.at }} ·
                {{ entry.actorId ?? ('requisition.systemActor' | transloco) }}</small
              >
            </li>
          }
        </ol>
      </section>

      <div class="actions panel">
        @if (can('close')) {
          <button hlmBtn type="button" (click)="close()">{{ 'sourcing.close' | transloco }}</button>
        }
        @if (can('close-early')) {
          <button hlmBtn variant="outline" type="button" (click)="pending.set('close-early')">
            {{ 'sourcing.closeEarly' | transloco }}
          </button>
        }
        @if (can('cancel')) {
          <button hlmBtn variant="outline" type="button" (click)="pending.set('cancel')">
            {{ 'sourcing.cancel' | transloco }}
          </button>
        }
      </div>

      @if (pending() === 'award' && awardEntry(); as entry) {
        <section
          class="editor"
          role="dialog"
          aria-modal="true"
          aria-labelledby="award-title"
          (keydown.escape)="pending.set(undefined)"
        >
          <h2 id="award-title">{{ 'sourcing.confirmAward' | transloco }}</h2>
          <p>
            {{ entry.supplierLegalName }} · {{ entry.rawTotal | money }} ·
            {{ entry.normalizedTotal | money }} · {{ 'sourcing.rank' | transloco }} {{ entry.rank }}
          </p>
          <p class="hint">{{ 'sourcing.awardHelp' | transloco }}</p>
          <div class="form-grid">
            <label for="justification">{{ 'sourcing.justification' | transloco }}</label>
            <input
              id="justification"
              hlmInput
              [value]="justification()"
              (input)="justification.set($any($event.target).value)"
            />
            @if (waiverNeeded().includes('insufficient')) {
              <label for="insufficientReason">{{
                'sourcing.insufficientReason' | transloco
              }}</label>
              <input
                id="insufficientReason"
                hlmInput
                [value]="insufficientReason()"
                (input)="insufficientReason.set($any($event.target).value)"
              />
            }
            @if (waiverNeeded().includes('non-leading')) {
              <label for="nonLeadingReason">{{ 'sourcing.nonLeadingReason' | transloco }}</label>
              <input
                id="nonLeadingReason"
                hlmInput
                [value]="nonLeadingReason()"
                (input)="nonLeadingReason.set($any($event.target).value)"
              />
            }
          </div>
          <div class="actions">
            <button hlmBtn variant="outline" type="button" (click)="pending.set(undefined)">
              {{ 'common.cancel' | transloco }}
            </button>
            <button hlmBtn type="button" (click)="award()">
              {{ 'sourcing.submitAward' | transloco }}
            </button>
          </div>
        </section>
      }
    }
    <app-action-confirmation
      [request]="confirmation()"
      (confirmed)="confirm($event)"
      (cancelled)="pending.set(undefined)"
    />
  `,
  styleUrl: './data-pages.css',
})
export class RfqDetailPage {
  private readonly port = inject(SOURCING_PORT);
  private readonly masterData = inject(MASTER_DATA_PORT);
  private readonly transloco = inject(TranslocoService);
  private readonly route = inject(ActivatedRoute);
  readonly sources = SOURCES;
  readonly rfq = signal<Rfq | undefined>(undefined);
  readonly history = signal<readonly RfqHistoryEntry[]>([]);
  readonly suppliers = signal<readonly SupplierSummary[]>([]);
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly notice = signal<string | undefined>(undefined);
  readonly pending = signal<Pending>(undefined);
  readonly inviteSupplier = signal('');
  readonly deadline = signal('');
  readonly waiverReason = signal('');
  readonly reason = signal('');
  readonly quoteForm = signal<QuoteForm | undefined>(undefined);
  readonly withdrawQuoteId = signal('');
  readonly awardEntry = signal<ComparisonEntry | undefined>(undefined);
  readonly justification = signal('');
  readonly insufficientReason = signal('');
  readonly nonLeadingReason = signal('');
  readonly waiverNeeded = signal<readonly ('insufficient' | 'non-leading')[]>([]);
  readonly rateCurrency = signal('');
  readonly rateValue = signal('');
  readonly rateDate = signal('');
  readonly entries = computed(() => SourcingUseCases.rankedEntries(this.rfq()?.comparison));
  readonly pendingSuppliers = computed(() => SourcingUseCases.supplierWithoutQuote(this.rfq()));
  readonly eligibleSuppliers = computed(() => {
    const invited = new Set(
      SourcingUseCases.activeInvitations(this.rfq()).map((invitation) => invitation.supplierId),
    );
    return this.suppliers().filter((supplier) => supplier.eligible && !invited.has(supplier.id));
  });
  readonly quotePreview = computed(
    () =>
      RequisitionUseCases.preview(
        (this.quoteForm()?.lines ?? []).map((line) => ({
          quantity: line.quantityOffered,
          unitPrice: line.unitPrice,
        })),
      ).total,
  );
  readonly confirmation = computed<ActionConfirmationRequest | undefined>(() => {
    switch (this.pending()) {
      case 'cancel':
        return {
          title: this.transloco.translate('sourcing.confirmCancel'),
          actionLabel: this.transloco.translate('sourcing.cancel'),
        };
      case 'close-early':
        return {
          title: this.transloco.translate('sourcing.confirmCloseEarly'),
          actionLabel: this.transloco.translate('sourcing.closeEarly'),
          description: this.transloco.translate('sourcing.closeEarlyHelp'),
        };
      case 'withdraw-quote':
        return {
          title: this.transloco.translate('sourcing.confirmWithdrawQuote'),
          actionLabel: this.transloco.translate('sourcing.withdrawQuote'),
        };
      default:
        return undefined;
    }
  });

  constructor() {
    this.route.paramMap.subscribe(() => void this.load());
  }

  can(action: RfqAction) {
    return SourcingUseCases.can(this.rfq(), action);
  }

  async load() {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    const [rfq, history] = await Promise.all([this.port.get(id), this.port.history(id)]);
    if (!rfq.ok) {
      this.error.set(rfq.error);
      return;
    }
    this.error.set(undefined);
    this.apply(rfq.value);
    this.history.set(history.ok ? history.value : []);
    if (rfq.value.allowedActions.includes('invite') && this.suppliers().length === 0) {
      const suppliers = await this.masterData.listSuppliers({
        page: 1,
        pageSize: 100,
        sort: 'legalName',
      });
      if (suppliers.ok) this.suppliers.set(suppliers.value.items);
    }
  }

  async invite() {
    await this.run((rfq) => this.port.invite(rfq, this.inviteSupplier()), 'sourcing.invited');
    this.inviteSupplier.set('');
  }

  removeInvitation(supplierId: string) {
    return this.run((rfq) => this.port.removeInvitation(rfq, supplierId), 'sourcing.removedNotice');
  }

  publish() {
    const deadline = SourcingUseCases.deadlineInstant(this.deadline());
    if (!deadline) return;
    return this.run(
      (rfq) => this.port.publish(rfq, deadline, this.waiverReason()),
      'sourcing.published',
    );
  }

  extend() {
    const deadline = SourcingUseCases.deadlineInstant(this.deadline());
    if (!deadline) return;
    return this.run(
      (rfq) => this.port.extendDeadline(rfq, deadline, this.reason()),
      'sourcing.extended',
    );
  }

  remind() {
    return this.run((rfq) => this.port.remind(rfq), 'sourcing.reminded');
  }

  close() {
    return this.run((rfq) => this.port.close(rfq), 'sourcing.closedNotice');
  }

  editQuote(quoteId: string, supplierId: string) {
    this.resetQuote(quoteId, supplierId);
  }

  askWithdraw(quoteId: string) {
    this.withdrawQuoteId.set(quoteId);
    this.pending.set('withdraw-quote');
  }

  patch(change: Partial<QuoteForm>) {
    this.quoteForm.update((form) => (form ? { ...form, ...change } : form));
  }

  patchLine(index: number, change: Partial<QuoteForm['lines'][number]>) {
    this.quoteForm.update((form) =>
      form
        ? {
            ...form,
            lines: form.lines.map((line, position) =>
              position === index ? { ...line, ...change } : line,
            ),
          }
        : form,
    );
  }

  resetQuote(quoteId = '', supplierId = '') {
    const rfq = this.rfq();
    this.quoteForm.set(
      rfq && SourcingUseCases.can(rfq, 'enter-quote')
        ? {
            supplierId,
            quoteId,
            currency: rfq.baseCurrency || 'BRL',
            lines: rfq.lines.map((line) => ({
              rfqLineNumber: line.lineNumber,
              quantityOffered: line.quantity,
              unitPrice: '',
            })),
            freight: '',
            discount: '',
            taxesIncluded: '',
            validUntil: rfq.deadline?.slice(0, 10) ?? '',
            deliveryDays: '',
            paymentTerms: '',
            notes: '',
            source: 'Email',
            receivedAt: '',
          }
        : undefined,
    );
  }

  async saveQuote() {
    const form = this.quoteForm();
    if (!form) return;
    const receivedAt =
      SourcingUseCases.deadlineInstant(form.receivedAt) ?? new Date().toISOString();
    const input: QuoteInput = {
      supplierId: form.supplierId || undefined,
      currency: form.currency.trim().toUpperCase(),
      lines: form.lines.filter((line) => line.unitPrice.trim()),
      freight: form.freight,
      discount: form.discount,
      taxesIncluded: form.taxesIncluded,
      validUntil: form.validUntil,
      deliveryDays: Number.parseInt(form.deliveryDays, 10) || 0,
      paymentTerms: form.paymentTerms,
      notes: form.notes,
      source: form.source,
      receivedAt,
    };
    await this.run(
      (rfq) =>
        form.quoteId
          ? this.port.reviseQuote(rfq, form.quoteId, input)
          : this.port.enterQuote(rfq, input),
      'sourcing.quoteSaved',
    );
  }

  askAward(entry: ComparisonEntry) {
    this.awardEntry.set(entry);
    this.waiverNeeded.set([]);
    this.pending.set('award');
  }

  async award() {
    const entry = this.awardEntry();
    if (!entry) return;
    const result = await this.run(
      (rfq) =>
        this.port.award(rfq, {
          quoteRevisionId: entry.quoteRevisionId,
          justification: this.justification(),
          insufficientQuotationsReason: this.waiverNeeded().includes('insufficient')
            ? this.insufficientReason()
            : undefined,
          nonLeadingReason: this.waiverNeeded().includes('non-leading')
            ? this.nonLeadingReason()
            : undefined,
        }),
      'sourcing.awarded',
    );
    if (result) {
      this.pending.set(undefined);
      return;
    }
    const waiver = SourcingUseCases.waiverForProblem(this.error()?.code);
    if (waiver && !this.waiverNeeded().includes(waiver))
      this.waiverNeeded.update((list) => [...list, waiver]);
  }

  recordRate() {
    const currency = this.rateCurrency() || this.rfq()?.comparison?.missingRateCurrencies[0] || '';
    return this.run(
      (rfq) =>
        this.port.recordRate(rfq, {
          currency,
          rate: this.rateValue().trim(),
          quoteDate: this.rateDate(),
          reason: this.reason(),
        }),
      'sourcing.rateRecorded',
    );
  }

  async confirm(reason: string) {
    const action = this.pending();
    this.pending.set(undefined);
    if (action === 'cancel')
      await this.run((rfq) => this.port.cancel(rfq, reason), 'sourcing.cancelledNotice');
    if (action === 'close-early')
      await this.run((rfq) => this.port.close(rfq, reason), 'sourcing.closedNotice');
    if (action === 'withdraw-quote')
      await this.run(
        (rfq) => this.port.withdrawQuote(rfq, this.withdrawQuoteId(), reason),
        'sourcing.quoteWithdrawn',
      );
  }

  warningLabel(warning: string) {
    const [code, subject] = warning.split(':');
    return `${this.transloco.translate('sourcing.warnings.' + code)}${subject ? ` (${subject})` : ''}`;
  }

  private apply(rfq: Rfq) {
    this.rfq.set(rfq);
    if (!this.quoteForm() || !SourcingUseCases.can(rfq, 'enter-quote')) this.resetQuote();
  }

  /** Runs a versioned change; on success the server representation (with its new version) replaces the local one. */
  private async run(
    action: (
      rfq: Rfq,
    ) => Promise<{ ok: true; value: Rfq } | { ok: false; error: ApplicationError }>,
    notice: string,
  ) {
    const current = this.rfq();
    if (!current) return false;
    const result = await action(current);
    if (!result.ok) {
      this.error.set(result.error);
      this.notice.set(undefined);
      return false;
    }
    this.error.set(undefined);
    this.notice.set(notice);
    this.quoteForm.set(undefined);
    this.reason.set('');
    this.apply(result.value);
    const history = await this.port.history(result.value.id);
    if (history.ok) this.history.set(history.value);
    return true;
  }
}
