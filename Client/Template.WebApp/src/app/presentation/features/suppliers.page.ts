import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';
import type { ApplicationError } from '@application/common/application-error';
import type {
  EnrichmentProposal,
  Page,
  SupplierDetail,
  SupplierAddress,
  SupplierContact,
  SupplierInput,
  SupplierSummary,
} from '@application/master-data/master-data';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmButton } from '@presentation/shared-ui/button';
import { HlmInput } from '@presentation/shared-ui/input';
import { MASTER_DATA_PORT } from '../state/ports';
import { SessionFacade } from '../state/session.facade';
import { ActionConfirmation } from '../shared/action-confirmation';

@Component({
  selector: 'app-suppliers-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormField, TranslocoPipe, HlmButton, HlmInput, ActionConfirmation],
  templateUrl: './suppliers.page.html',
  styleUrl: './data-pages.css',
})
export class SuppliersPage {
  private readonly port = inject(MASTER_DATA_PORT);
  private readonly session = inject(SessionFacade);
  readonly canManage = this.session.can('suppliers.manage');
  readonly canStatus = this.session.can('suppliers.status.change');
  readonly page = signal<Page<SupplierSummary>>({
    items: [],
    page: 1,
    pageSize: 20,
    totalItems: 0,
    totalPages: 0,
  });
  readonly selected = signal<SupplierDetail | undefined>(undefined);
  readonly proposal = signal<EnrichmentProposal | undefined>(undefined);
  readonly error = signal<ApplicationError | undefined>(undefined);
  readonly loading = signal(true);
  readonly query = signal('');
  readonly pendingStatus = signal<'activate' | 'suspend' | 'inactivate' | 'reactivate' | undefined>(
    undefined,
  );
  readonly model = signal({ legalName: '', tradeName: '', country: 'BR', taxId: '' });
  readonly contactModel = signal({ name: '', email: '', phone: '', role: '' });
  readonly addressModel = signal({ postalCode: '', street: '', number: '', city: '', state: '' });
  readonly categoriesModel = signal({ ids: '' });
  readonly supplierForm = form(this.model, (path) => {
    required(path.legalName);
    required(path.country);
    required(path.taxId);
  });
  readonly contactForm = form(this.contactModel, (path) => required(path.name));
  readonly addressForm = form(this.addressModel, (path) => {
    required(path.postalCode);
    required(path.street);
    required(path.city);
    required(path.state);
  });
  readonly categoriesForm = form(this.categoriesModel);
  constructor() {
    void this.load();
  }
  async load() {
    this.loading.set(true);
    const result = await this.port.listSuppliers({
      page: this.page().page,
      pageSize: 20,
      q: this.query(),
      sort: 'legalName',
    });
    this.loading.set(false);
    if (result.ok) {
      this.page.set(result.value);
      this.error.set(undefined);
    } else this.error.set(result.error);
  }
  move(delta: number) {
    this.page.update((value) => ({ ...value, page: value.page + delta }));
    void this.load();
  }
  async open(id: string) {
    const result = await this.port.getSupplier(id);
    if (result.ok) {
      this.selected.set(result.value);
      this.model.set({
        legalName: result.value.legalName,
        tradeName: result.value.tradeName,
        country: result.value.country,
        taxId: result.value.taxId.replace(/\D/g, ''),
      });
      const contact = result.value.contacts[0];
      this.contactModel.set({
        name: contact?.name ?? '',
        email: contact?.email ?? '',
        phone: contact?.phone ?? '',
        role: contact?.role ?? '',
      });
      const address = result.value.addresses[0];
      this.addressModel.set({
        postalCode: address?.postalCode ?? '',
        street: address?.street ?? '',
        number: address?.number ?? '',
        city: address?.city ?? '',
        state: address?.state ?? '',
      });
      this.categoriesModel.set({ ids: result.value.categoryIds.join(', ') });
    } else this.error.set(result.error);
  }
  async save(event: Event) {
    event.preventDefault();
    if (this.supplierForm().invalid()) return;
    const item = this.selected();
    const result = await this.port.saveSupplier(
      item?.id,
      item?.version,
      this.model() as SupplierInput,
    );
    if (result.ok) {
      this.selected.set(result.value);
      await this.load();
    } else this.error.set(result.error);
  }
  async confirmStatus(reason: string) {
    const item = this.selected();
    const action = this.pendingStatus();
    if (!item || !action) return;
    this.pendingStatus.set(undefined);
    const result = await this.port.changeSupplierStatus(item, action, reason);
    if (result.ok) {
      this.selected.set(result.value);
      await this.load();
    } else this.error.set(result.error);
  }
  async lookup() {
    const result = await this.port.lookupCompany(this.model().taxId);
    if (result.ok) this.proposal.set(result.value);
    else this.error.set(result.error);
  }
  async accept(proposal: EnrichmentProposal) {
    const item = this.selected();
    if (!item) return;
    const result = await this.port.acceptEnrichment(item, proposal);
    if (result.ok) {
      this.selected.set(result.value);
      this.proposal.set(undefined);
    } else this.error.set(result.error);
  }
  async saveContact(event: Event, item: SupplierDetail) {
    event.preventDefault();
    if (this.contactForm().invalid()) return;
    const prior = item.contacts[0];
    const contact: SupplierContact = { id: prior?.id, ...this.contactModel() };
    this.applyDetail(await this.port.saveSupplierContacts(item, [contact]));
  }
  async saveAddress(event: Event, item: SupplierDetail) {
    event.preventDefault();
    if (this.addressForm().invalid()) return;
    const prior = item.addresses[0];
    const address: SupplierAddress = {
      id: prior?.id,
      type: prior?.type ?? 'Headquarters',
      complement: prior?.complement ?? '',
      district: prior?.district ?? '',
      country: prior?.country ?? item.country,
      ...this.addressModel(),
    };
    this.applyDetail(await this.port.saveSupplierAddresses(item, [address]));
  }
  async saveCategories(event: Event, item: SupplierDetail) {
    event.preventDefault();
    const ids = this.categoriesModel()
      .ids.split(',')
      .map((value) => value.trim())
      .filter(Boolean);
    this.applyDetail(await this.port.saveSupplierCategories(item, ids));
  }
  private applyDetail(result: Awaited<ReturnType<typeof this.port.getSupplier>>) {
    if (result.ok) this.selected.set(result.value);
    else this.error.set(result.error);
  }
  fieldErrors(error: ApplicationError) {
    return Object.entries(error.fieldErrors ?? {});
  }
}
