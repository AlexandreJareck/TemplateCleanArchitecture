import { NgTemplateOutlet } from '@angular/common';
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
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus } from '@ng-icons/lucide';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { HlmSkeleton } from '@presentation/shared-ui/skeleton';
import { MASTER_DATA_PORT } from '../state/ports';
import { SessionFacade } from '../state/session.facade';
import { ActionConfirmation } from '../shared/action-confirmation';
import { FormDrawer } from '../shared/form-drawer';
import { HlmNativeSelectImports } from '@presentation/shared-ui/native-select';
import { HlmTableImports } from '@presentation/shared-ui/table';

@Component({
  selector: 'app-master-data-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ...HlmTableImports,
    ...HlmNativeSelectImports,
    FormField,
    TranslocoPipe,
    HlmButton,
    HlmInput,
    HlmSkeleton,
    ActionConfirmation,
    FormDrawer,
    NgIcon,
    NgTemplateOutlet,
  ],
  providers: [provideIcons({ lucidePlus })],
  templateUrl: './master-data.page.html',
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
  readonly drawerOpen = signal(false);
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
  create() {
    this.cancel();
    this.drawerOpen.set(true);
  }
  edit(item: MasterDataItem) {
    this.error.set(undefined);
    this.selected.set(item);
    this.drawerOpen.set(true);
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
    this.drawerOpen.set(false);
    this.error.set(undefined);
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
