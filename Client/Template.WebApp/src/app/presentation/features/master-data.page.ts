import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';
import { ActivatedRoute } from '@angular/router';
import type { ApplicationError } from '@application/common/application-error';
import type {
  MasterDataInput,
  MasterDataItem,
  MasterDataKind,
  Page,
} from '@application/master-data/master-data';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { HlmSkeleton } from '@presentation/shared-ui/skeleton';
import { MASTER_DATA_PORT } from '../state/ports';
import { SessionFacade } from '../state/session.facade';
import { ActionConfirmation } from '../shared/action-confirmation';

@Component({
  selector: 'app-master-data-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormField, TranslocoPipe, HlmButton, HlmInput, HlmSkeleton, ActionConfirmation],
  template: `
    <header class="page-title">
      <p>{{ 'shell.breadcrumbHome' | transloco }} / {{ titleKey | transloco }}</p>
      <h1>{{ titleKey | transloco }}</h1>
    </header>
    <div class="toolbar">
      <label for="search" class="sr-only">{{ 'common.search' | transloco }}</label
      ><input
        id="search"
        hlmInput
        [value]="query()"
        (input)="search($event)"
        [placeholder]="'common.search' | transloco"
      /><button hlmBtn type="button" variant="outline" (click)="load()">
        {{ 'common.refresh' | transloco }}
      </button>
    </div>
    @if (error(); as problem) {
      <div class="feedback" role="alert">
        <strong>{{ problem.code }}</strong
        ><span>{{ problem.detail }}</span>
        @if (problem.traceId) {
          <small>Trace: {{ problem.traceId }}</small>
        }
        @if (problem.kind === 'version-mismatch') {
          <button hlmBtn type="button" variant="outline" (click)="load()">
            {{ 'common.reload' | transloco }}
          </button>
        }
        @for (entry of fieldErrors(problem); track entry[0]) {
          <span>{{ entry[0] }}: {{ entry[1].join(', ') }}</span>
        }
      </div>
    }
    @if (loading()) {
      <div aria-busy="true" role="status"><div hlmSkeleton class="loading"></div></div>
    } @else if (page().items.length === 0) {
      <div class="empty">
        <h2>{{ 'common.empty' | transloco }}</h2>
        <p>{{ 'common.emptyHelp' | transloco }}</p>
      </div>
    } @else {
      <div class="table-scroll" role="region" tabindex="0" [attr.aria-label]="titleKey | transloco">
        <table>
          <thead>
            <tr>
              <th>{{ 'fields.code' | transloco }}</th>
              <th>{{ 'fields.name' | transloco }}</th>
              <th>{{ 'fields.status' | transloco }}</th>
              <th>{{ 'common.actions' | transloco }}</th>
            </tr>
          </thead>
          <tbody>
            @for (item of page().items; track item.id) {
              <tr>
                <td data-label="Código">{{ item.code }}</td>
                <td data-label="Nome">{{ item.name }}</td>
                <td data-label="Status">
                  {{ (item.active ? 'common.active' : 'common.inactive') | transloco }}
                </td>
                <td>
                  <button
                    hlmBtn
                    size="sm"
                    variant="outline"
                    type="button"
                    (click)="edit(item)"
                    [disabled]="!canManage"
                  >
                    {{ 'common.edit' | transloco }}</button
                  ><button
                    hlmBtn
                    size="sm"
                    variant="ghost"
                    type="button"
                    (click)="pendingToggle.set(item)"
                    [disabled]="!canManage"
                  >
                    {{ (item.active ? 'common.deactivate' : 'common.reactivate') | transloco }}
                  </button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
    <nav class="pager" aria-label="Paginação">
      <button
        hlmBtn
        variant="outline"
        type="button"
        [disabled]="page().page <= 1"
        (click)="move(-1)"
      >
        ←</button
      ><span>{{ page().page }} / {{ page().totalPages || 1 }}</span
      ><button
        hlmBtn
        variant="outline"
        type="button"
        [disabled]="page().page >= page().totalPages"
        (click)="move(1)"
      >
        →
      </button>
    </nav>
    @if (canManage) {
      <section class="editor">
        <h2>{{ selected() ? ('common.edit' | transloco) : ('common.create' | transloco) }}</h2>
        <form novalidate (submit)="save($event)">
          <label for="code">{{ 'fields.code' | transloco }}</label
          ><input id="code" hlmInput [formField]="itemForm.code" [readOnly]="!!selected()" /><label
            for="name"
            >{{ 'fields.name' | transloco }}</label
          ><input id="name" hlmInput [formField]="itemForm.name" />
          @if (kind === 'cost-centers' || kind === 'catalog-items') {
            <label for="related">{{ 'fields.relatedId' | transloco }}</label
            ><input id="related" hlmInput [formField]="itemForm.relatedId" />
          }
          @if (kind === 'departments' || kind === 'cost-centers') {
            <label for="owner">{{ 'fields.ownerUserId' | transloco }}</label
            ><input id="owner" hlmInput [formField]="itemForm.ownerUserId" />
          }
          @if (kind === 'catalog-items') {
            <label for="kind">{{ 'fields.kind' | transloco }}</label>
            <select id="kind" [formField]="itemForm.kind">
              <option value="Good">{{ 'fields.good' | transloco }}</option>
              <option value="Service">{{ 'fields.service' | transloco }}</option>
            </select>
            <label for="secondary">{{ 'fields.secondaryRelatedId' | transloco }}</label
            ><input id="secondary" hlmInput [formField]="itemForm.secondaryRelatedId" />
          }
          @if (kind === 'units') {
            <label for="decimals">{{ 'fields.decimals' | transloco }}</label
            ><input id="decimals" hlmInput type="number" [formField]="itemForm.allowedDecimals" />
          }
          <div class="actions">
            <button hlmBtn type="submit" [disabled]="itemForm().invalid() || saving()">
              {{ 'common.save' | transloco }}
            </button>
            @if (selected()) {
              <button hlmBtn variant="outline" type="button" (click)="cancel()">
                {{ 'common.cancel' | transloco }}
              </button>
            }
          </div>
        </form>
      </section>
    }
    <app-action-confirmation
      [request]="
        pendingToggle()
          ? {
              title: pendingToggle()!.active
                ? ('common.confirmDeactivate' | transloco)
                : ('common.confirmReactivate' | transloco),
              actionLabel: pendingToggle()!.active
                ? ('common.deactivate' | transloco)
                : ('common.reactivate' | transloco),
            }
          : undefined
      "
      (confirmed)="confirmToggle($event)"
      (cancelled)="pendingToggle.set(undefined)"
    />
  `,
  styleUrl: './data-pages.css',
})
export class MasterDataPage {
  private readonly port = inject(MASTER_DATA_PORT);
  private readonly session = inject(SessionFacade);
  private readonly route = inject(ActivatedRoute);
  readonly kind = this.route.snapshot.data['kind'] as MasterDataKind;
  readonly titleKey = this.route.snapshot.data['titleKey'] as string;
  readonly permission =
    this.kind === 'departments' || this.kind === 'cost-centers'
      ? 'organization.manage'
      : 'catalog.manage';
  readonly canManage = this.session.can(this.permission);
  readonly page = signal<Page<MasterDataItem>>({
    items: [],
    page: 1,
    pageSize: 20,
    totalItems: 0,
    totalPages: 0,
  });
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly query = signal('');
  readonly selected = signal<MasterDataItem | undefined>(undefined);
  readonly pendingToggle = signal<MasterDataItem | undefined>(undefined);
  readonly model = signal({
    code: '',
    name: '',
    relatedId: '',
    secondaryRelatedId: '',
    allowedDecimals: 0,
    ownerUserId: '',
    kind: 'Good' as 'Good' | 'Service',
  });
  readonly itemForm = form(this.model, (path) => {
    required(path.code);
    required(path.name);
  });
  constructor() {
    void this.load();
  }
  async load() {
    this.loading.set(true);
    const result = await this.port.list(this.kind, {
      page: this.page().page,
      pageSize: 20,
      q: this.query(),
      sort: 'code',
    });
    this.loading.set(false);
    if (result.ok) {
      this.page.set(result.value);
      this.error.set(undefined);
    } else this.error.set(result.error);
  }
  search(event: Event) {
    this.query.set((event.target as HTMLInputElement).value);
    this.page.update((value) => ({ ...value, page: 1 }));
  }
  move(delta: number) {
    this.page.update((value) => ({ ...value, page: value.page + delta }));
    void this.load();
  }
  edit(item: MasterDataItem) {
    this.selected.set(item);
    this.model.set({
      code: item.code,
      name: item.name,
      relatedId: item.relatedId ?? '',
      secondaryRelatedId: item.secondaryRelatedId ?? '',
      allowedDecimals: Number(item.detail ?? 0),
      ownerUserId: item.ownerUserId ?? '',
      kind: item.detail === 'Service' ? 'Service' : 'Good',
    });
  }
  cancel() {
    this.selected.set(undefined);
    this.model.set({
      code: '',
      name: '',
      relatedId: '',
      secondaryRelatedId: '',
      allowedDecimals: 0,
      ownerUserId: '',
      kind: 'Good' as 'Good' | 'Service',
    });
  }
  async save(event: Event) {
    event.preventDefault();
    if (this.itemForm().invalid()) return;
    this.saving.set(true);
    const input: MasterDataInput = this.model();
    const selected = this.selected();
    const result = selected
      ? await this.port.update(this.kind, selected, input)
      : await this.port.create(this.kind, input);
    this.saving.set(false);
    if (result.ok) {
      this.cancel();
      await this.load();
    } else this.error.set(result.error);
  }
  async confirmToggle(reason: string) {
    const item = this.pendingToggle();
    if (!item) return;
    this.pendingToggle.set(undefined);
    const result = await this.port.changeActive(this.kind, item, !item.active, reason);
    if (result.ok) await this.load();
    else this.error.set(result.error);
  }
  fieldErrors(error: ApplicationError) {
    return Object.entries(error.fieldErrors ?? {});
  }
}
