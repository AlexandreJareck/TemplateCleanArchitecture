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
  template: `
    <header class="page-title">
      <p>{{ 'shell.breadcrumbHome' | transloco }} / {{ 'nav.suppliers' | transloco }}</p>
      <h1>{{ 'nav.suppliers' | transloco }}</h1>
    </header>
    <div class="toolbar">
      <input
        hlmInput
        [value]="query()"
        (input)="query.set($any($event.target).value)"
        [placeholder]="'common.search' | transloco"
        aria-label="Pesquisar fornecedores"
      /><button hlmBtn variant="outline" type="button" (click)="load()">
        {{ 'common.search' | transloco }}
      </button>
    </div>
    @if (error(); as problem) {
      <div class="feedback" role="alert">
        <strong>{{ problem.code }}</strong
        ><span>{{ problem.detail }}</span>
        @if (problem.traceId) {
          <small>Trace: {{ problem.traceId }}</small>
        }
        @for (entry of fieldErrors(problem); track entry[0]) {
          <span>{{ entry[0] }}: {{ entry[1].join(', ') }}</span>
        }
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
        ←
      </button>
      <span>{{ page().page }} / {{ page().totalPages || 1 }}</span>
      <button
        hlmBtn
        variant="outline"
        type="button"
        [disabled]="page().page >= page().totalPages"
        (click)="move(1)"
      >
        →
      </button>
    </nav>
    @if (loading()) {
      <p role="status">{{ 'common.loading' | transloco }}</p>
    } @else if (page().items.length === 0) {
      <div class="empty">
        <h2>{{ 'common.empty' | transloco }}</h2>
      </div>
    } @else {
      <div class="table-scroll" role="region" tabindex="0" aria-label="Fornecedores">
        <table>
          <thead>
            <tr>
              <th>{{ 'fields.name' | transloco }}</th>
              <th>CNPJ</th>
              <th>{{ 'fields.status' | transloco }}</th>
              <th>{{ 'common.actions' | transloco }}</th>
            </tr>
          </thead>
          <tbody>
            @for (item of page().items; track item.id) {
              <tr>
                <td>{{ item.legalName }}</td>
                <td>{{ item.taxId }}</td>
                <td>{{ item.status }}</td>
                <td>
                  <button hlmBtn size="sm" variant="outline" type="button" (click)="open(item.id)">
                    {{ 'common.details' | transloco }}
                  </button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
    @if (canManage) {
      <section class="editor">
        <h2>{{ selected() ? ('common.edit' | transloco) : ('common.create' | transloco) }}</h2>
        <form (submit)="save($event)" novalidate>
          <label for="legalName">{{ 'supplier.legalName' | transloco }}</label
          ><input id="legalName" hlmInput [formField]="supplierForm.legalName" /><label
            for="tradeName"
            >{{ 'supplier.tradeName' | transloco }}</label
          ><input id="tradeName" hlmInput [formField]="supplierForm.tradeName" /><label
            for="country"
            >{{ 'supplier.country' | transloco }}</label
          ><input id="country" hlmInput [formField]="supplierForm.country" /><label for="taxId"
            >CNPJ</label
          ><input id="taxId" hlmInput [formField]="supplierForm.taxId" />
          <div class="actions">
            <button hlmBtn type="submit">{{ 'common.save' | transloco }}</button
            ><button hlmBtn variant="outline" type="button" (click)="lookup()">
              {{ 'supplier.enrich' | transloco }}
            </button>
          </div>
        </form>
        @if (proposal(); as p) {
          <div class="proposal">
            <h3>{{ 'supplier.proposal' | transloco }}</h3>
            <p>{{ p.legalName }} — {{ p.notice }}</p>
            <button hlmBtn type="button" [disabled]="!selected()" (click)="accept(p)">
              {{ 'supplier.accept' | transloco }}
            </button>
          </div>
        }
        @if (selected(); as detail) {
          <div class="actions status-actions">
            <strong>{{ detail.status }}</strong
            ><span>{{ detail.missingForActivation }}</span>
            @if (canStatus) {
              <button
                hlmBtn
                variant="outline"
                type="button"
                (click)="pendingStatus.set('activate')"
              >
                Ativar</button
              ><button
                hlmBtn
                variant="outline"
                type="button"
                (click)="pendingStatus.set('suspend')"
              >
                Suspender</button
              ><button
                hlmBtn
                variant="outline"
                type="button"
                (click)="pendingStatus.set('inactivate')"
              >
                Inativar</button
              ><button
                hlmBtn
                variant="outline"
                type="button"
                (click)="pendingStatus.set('reactivate')"
              >
                Reativar
              </button>
            }
          </div>
          <div class="related-grid">
            <form (submit)="saveContact($event, detail)" novalidate>
              <h3>{{ 'supplier.contacts' | transloco }}</h3>
              <label for="contactName">{{ 'fields.name' | transloco }}</label>
              <input id="contactName" hlmInput [formField]="contactForm.name" />
              <label for="contactEmail">E-mail</label>
              <input id="contactEmail" hlmInput type="email" [formField]="contactForm.email" />
              <label for="contactPhone">{{ 'supplier.phone' | transloco }}</label>
              <input id="contactPhone" hlmInput [formField]="contactForm.phone" />
              <label for="contactRole">{{ 'supplier.role' | transloco }}</label>
              <input id="contactRole" hlmInput [formField]="contactForm.role" />
              <button hlmBtn type="submit">{{ 'common.save' | transloco }}</button>
            </form>
            <form (submit)="saveAddress($event, detail)" novalidate>
              <h3>{{ 'supplier.addresses' | transloco }}</h3>
              <label for="postalCode">CEP</label>
              <input id="postalCode" hlmInput [formField]="addressForm.postalCode" />
              <label for="street">{{ 'supplier.street' | transloco }}</label>
              <input id="street" hlmInput [formField]="addressForm.street" />
              <label for="number">{{ 'supplier.number' | transloco }}</label>
              <input id="number" hlmInput [formField]="addressForm.number" />
              <label for="city">{{ 'supplier.city' | transloco }}</label>
              <input id="city" hlmInput [formField]="addressForm.city" />
              <label for="state">UF</label>
              <input id="state" hlmInput [formField]="addressForm.state" />
              <button hlmBtn type="submit">{{ 'common.save' | transloco }}</button>
            </form>
          </div>
          <form class="categories-form" (submit)="saveCategories($event, detail)">
            <label for="categoryIds">{{ 'supplier.categoryIds' | transloco }}</label>
            <input id="categoryIds" hlmInput [formField]="categoriesForm.ids" />
            <button hlmBtn type="submit">{{ 'common.save' | transloco }}</button>
          </form>
        }
      </section>
    }
    <app-action-confirmation
      [request]="
        pendingStatus()
          ? {
              title: ('common.confirmStatus' | transloco),
              actionLabel: ('common.confirm' | transloco),
            }
          : undefined
      "
      (confirmed)="confirmStatus($event)"
      (cancelled)="pendingStatus.set(undefined)"
    />
  `,
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
