import { inject, Injectable } from '@angular/core';
import { failure, success, type Result } from '@application/common/result';
import type {
  EnrichmentProposal,
  MasterDataInput,
  MasterDataItem,
  MasterDataKind,
  MasterDataPort,
  Page,
  PageRequest,
  SupplierDetail,
  SupplierAddress,
  SupplierContact,
  SupplierInput,
  SupplierSummary,
} from '@application/master-data/master-data';
import { firstValueFrom, fromEvent, takeUntil, type Observable } from 'rxjs';
import { CategoriesService } from '../generated/categories/categories.service';
import { CatalogItemsService } from '../generated/catalog-items/catalog-items.service';
import { CostCentersService } from '../generated/cost-centers/cost-centers.service';
import { DepartmentsService } from '../generated/departments/departments.service';
import { EnrichmentService } from '../generated/enrichment/enrichment.service';
import type { CatalogItemView } from '../generated/models/catalogItemView';
import type { CategoryView } from '../generated/models/categoryView';
import type { CostCenterView } from '../generated/models/costCenterView';
import type { DepartmentView } from '../generated/models/departmentView';
import type { SupplierSummaryView } from '../generated/models/supplierSummaryView';
import type { SupplierView } from '../generated/models/supplierView';
import type { UnitOfMeasureView } from '../generated/models/unitOfMeasureView';
import { SuppliersService } from '../generated/suppliers/suppliers.service';
import { UnitsOfMeasureService } from '../generated/units-of-measure/units-of-measure.service';
import { mapProblem } from '../../http/problem-mapper';

type TransportItem =
  DepartmentView | CostCenterView | CategoryView | UnitOfMeasureView | CatalogItemView;

@Injectable({ providedIn: 'root' })
export class MasterDataApiAdapter implements MasterDataPort {
  private readonly departments = inject(DepartmentsService);
  private readonly costCenters = inject(CostCentersService);
  private readonly categories = inject(CategoriesService);
  private readonly units = inject(UnitsOfMeasureService);
  private readonly catalogItems = inject(CatalogItemsService);
  private readonly suppliers = inject(SuppliersService);
  private readonly enrichment = inject(EnrichmentService);

  async list(
    kind: MasterDataKind,
    request: PageRequest,
    signal?: AbortSignal,
  ): Promise<Result<Page<MasterDataItem>>> {
    const params = {
      page: request.page,
      pageSize: request.pageSize,
      q: request.q,
      sort: request.sort,
    };
    const source =
      kind === 'departments'
        ? this.departments.getApiV2Departments(params)
        : kind === 'cost-centers'
          ? this.costCenters.getApiV2CostCenters(params)
          : kind === 'categories'
            ? this.categories.getApiV2Categories(params)
            : kind === 'units'
              ? this.units.getApiV2UnitsOfMeasure(params)
              : this.catalogItems.getApiV2CatalogItems(params);
    return this.execute(source, signal, (value) =>
      mapPage(value as PageEnvelope<TransportItem>, mapItem),
    );
  }

  async create(kind: MasterDataKind, input: MasterDataInput, signal?: AbortSignal) {
    const options = { headers: idempotencyHeaders() };
    const source =
      kind === 'departments'
        ? this.departments.postApiV2Departments(
            { code: input.code, name: input.name, headUserId: input.ownerUserId },
            options,
          )
        : kind === 'cost-centers'
          ? this.costCenters.postApiV2CostCenters(
              {
                code: input.code,
                name: input.name,
                departmentId: input.relatedId,
                managerUserId: input.ownerUserId,
              },
              options,
            )
          : kind === 'categories'
            ? this.categories.postApiV2Categories({ code: input.code, name: input.name }, options)
            : kind === 'units'
              ? this.units.postApiV2UnitsOfMeasure(
                  {
                    code: input.code,
                    name: input.name,
                    allowedDecimals: input.allowedDecimals ?? 0,
                  },
                  options,
                )
              : this.catalogItems.postApiV2CatalogItems(
                  {
                    code: input.code,
                    description: input.name,
                    kind: input.kind ?? 'Good',
                    categoryId: input.relatedId,
                    unitOfMeasureId: input.secondaryRelatedId,
                  },
                  options,
                );
    return this.execute(source, signal, (value) => mapItem(value as TransportItem));
  }

  async update(
    kind: MasterDataKind,
    item: MasterDataItem,
    input: MasterDataInput,
    signal?: AbortSignal,
  ) {
    const options = { headers: versionHeaders(item.version) };
    const source =
      kind === 'departments'
        ? this.departments.putApiV2DepartmentsId(
            item.id,
            { name: input.name, headUserId: input.ownerUserId },
            options,
          )
        : kind === 'cost-centers'
          ? this.costCenters.putApiV2CostCentersId(
              item.id,
              { name: input.name, managerUserId: input.ownerUserId },
              options,
            )
          : kind === 'categories'
            ? this.categories.putApiV2CategoriesId(item.id, { name: input.name }, options)
            : kind === 'units'
              ? this.units.putApiV2UnitsOfMeasureId(item.id, { name: input.name }, options)
              : this.catalogItems.putApiV2CatalogItemsId(
                  item.id,
                  {
                    code: item.code,
                    description: input.name,
                    kind: input.kind ?? 'Good',
                    categoryId: input.relatedId,
                    unitOfMeasureId: input.secondaryRelatedId,
                  },
                  options,
                );
    return this.execute(source, signal, (value) => mapItem(value as TransportItem));
  }

  async changeActive(
    kind: MasterDataKind,
    item: MasterDataItem,
    active: boolean,
    reason: string,
    signal?: AbortSignal,
  ) {
    const options = { headers: { ...versionHeaders(item.version), ...idempotencyHeaders() } };
    const body = { reason };
    const source =
      kind === 'departments'
        ? active
          ? this.departments.postApiV2DepartmentsIdReactivate(item.id, body, options)
          : this.departments.postApiV2DepartmentsIdDeactivate(item.id, body, options)
        : kind === 'cost-centers'
          ? active
            ? this.costCenters.postApiV2CostCentersIdReactivate(item.id, body, options)
            : this.costCenters.postApiV2CostCentersIdDeactivate(item.id, body, options)
          : kind === 'categories'
            ? active
              ? this.categories.postApiV2CategoriesIdReactivate(item.id, body, options)
              : this.categories.postApiV2CategoriesIdDeactivate(item.id, body, options)
            : kind === 'units'
              ? active
                ? this.units.postApiV2UnitsOfMeasureIdReactivate(item.id, body, options)
                : this.units.postApiV2UnitsOfMeasureIdDeactivate(item.id, body, options)
              : active
                ? this.catalogItems.postApiV2CatalogItemsIdReactivate(item.id, body, options)
                : this.catalogItems.postApiV2CatalogItemsIdDeactivate(item.id, body, options);
    return this.execute(source, signal, (value) => mapItem(value as TransportItem));
  }

  async listSuppliers(request: PageRequest, signal?: AbortSignal) {
    return this.execute(
      this.suppliers.getApiV2Suppliers({
        page: request.page,
        pageSize: request.pageSize,
        q: request.q,
        sort: request.sort,
      }),
      signal,
      (value) => mapPage(value, mapSupplierSummary),
    );
  }

  async getSupplier(id: string, signal?: AbortSignal) {
    return this.execute(this.suppliers.getApiV2SuppliersId(id), signal, mapSupplierDetail);
  }

  async saveSupplier(
    id: string | undefined,
    version: string | undefined,
    input: SupplierInput,
    signal?: AbortSignal,
  ) {
    const body = {
      legalName: input.legalName,
      tradeName: input.tradeName,
      country: input.country,
      taxId: input.taxId,
    };
    const source = id
      ? this.suppliers.putApiV2SuppliersId(id, body, { headers: versionHeaders(version ?? '') })
      : this.suppliers.postApiV2Suppliers(body, { headers: idempotencyHeaders() });
    return this.execute(source, signal, mapSupplierDetail);
  }

  async changeSupplierStatus(
    item: SupplierDetail,
    action: 'activate' | 'suspend' | 'inactivate' | 'reactivate',
    reason: string,
    signal?: AbortSignal,
  ) {
    const options = { headers: { ...versionHeaders(item.version), ...idempotencyHeaders() } };
    const body = { reason };
    const source =
      action === 'activate'
        ? this.suppliers.postApiV2SuppliersIdActivate(item.id, body, options)
        : action === 'suspend'
          ? this.suppliers.postApiV2SuppliersIdSuspend(item.id, body, options)
          : action === 'inactivate'
            ? this.suppliers.postApiV2SuppliersIdInactivate(item.id, body, options)
            : this.suppliers.postApiV2SuppliersIdReactivate(item.id, body, options);
    return this.execute(source, signal, mapSupplierDetail);
  }

  async lookupCompany(taxId: string, signal?: AbortSignal) {
    return this.execute(
      this.enrichment.getApiV2EnrichmentCompanyRegistrationsTaxId(taxId),
      signal,
      (value): EnrichmentProposal => ({
        provider: value.provider ?? '',
        retrievedAt: value.retrievedAt ?? '',
        legalName: value.legalName ?? '',
        tradeName: value.tradeName ?? '',
        notice: value.notice ?? '',
      }),
    );
  }

  async acceptEnrichment(item: SupplierDetail, proposal: EnrichmentProposal, signal?: AbortSignal) {
    return this.execute(
      this.suppliers.postApiV2SuppliersIdEnrichmentAcceptances(item.id, proposal, {
        headers: { ...versionHeaders(item.version), ...idempotencyHeaders() },
      }),
      signal,
      mapSupplierDetail,
    );
  }

  async saveSupplierContacts(
    item: SupplierDetail,
    contacts: readonly SupplierContact[],
    signal?: AbortSignal,
  ) {
    return this.execute(
      this.suppliers.putApiV2SuppliersIdContacts(
        item.id,
        { contacts: [...contacts] },
        { headers: versionHeaders(item.version) },
      ),
      signal,
      mapSupplierDetail,
    );
  }

  async saveSupplierAddresses(
    item: SupplierDetail,
    addresses: readonly SupplierAddress[],
    signal?: AbortSignal,
  ) {
    return this.execute(
      this.suppliers.putApiV2SuppliersIdAddresses(
        item.id,
        { addresses: [...addresses] },
        { headers: versionHeaders(item.version) },
      ),
      signal,
      mapSupplierDetail,
    );
  }

  async saveSupplierCategories(
    item: SupplierDetail,
    categoryIds: readonly string[],
    signal?: AbortSignal,
  ) {
    return this.execute(
      this.suppliers.putApiV2SuppliersIdCategories(
        item.id,
        { categoryIds: [...categoryIds] },
        { headers: versionHeaders(item.version) },
      ),
      signal,
      mapSupplierDetail,
    );
  }

  private async execute<T, U>(
    source: Observable<T>,
    signal: AbortSignal | undefined,
    map: (value: T) => U,
  ): Promise<Result<U>> {
    try {
      const bounded = signal ? source.pipe(takeUntil(fromEvent(signal, 'abort'))) : source;
      return success(map(await firstValueFrom(bounded)));
    } catch (error) {
      return failure<U>(mapProblem(error, signal));
    }
  }
}

interface PageEnvelope<T> {
  items?: T[] | null;
  page?: number;
  pageSize?: number;
  totalItems?: number;
  totalPages?: number;
}
function mapPage<T, U>(value: PageEnvelope<T>, map: (item: T) => U): Page<U> {
  return {
    items: (value.items ?? []).map(map),
    page: value.page ?? 1,
    pageSize: value.pageSize ?? 20,
    totalItems: value.totalItems ?? 0,
    totalPages: value.totalPages ?? 0,
  };
}
function mapItem(value: TransportItem): MasterDataItem {
  const catalog = value as CatalogItemView;
  const unit = value as UnitOfMeasureView;
  const costCenter = value as CostCenterView;
  const department = value as DepartmentView;
  return {
    id: value.id ?? '',
    code: value.code ?? '',
    name: catalog.description ?? ('name' in value ? value.name : '') ?? '',
    active: value.isActive ?? false,
    version: value.version ?? '',
    detail:
      catalog.kind ?? (unit.allowedDecimals === undefined ? undefined : `${unit.allowedDecimals}`),
    relatedId: catalog.categoryId ?? costCenter.departmentId,
    secondaryRelatedId: catalog.unitOfMeasureId,
    ownerUserId: department.headUserId ?? costCenter.managerUserId ?? undefined,
  };
}
function mapSupplierSummary(value: SupplierSummaryView): SupplierSummary {
  return {
    id: value.id ?? '',
    legalName: value.legalName ?? '',
    tradeName: value.tradeName ?? '',
    taxId: value.formattedTaxId ?? value.taxId ?? '',
    status: value.status ?? '',
    eligible: value.isEligibleForNewCommitments ?? false,
    version: value.version ?? '',
  };
}
function mapSupplierDetail(value: SupplierView): SupplierDetail {
  return {
    ...mapSupplierSummary(value),
    country: value.country ?? '',
    missingForActivation: value.missingForActivation ?? '',
    categoryIds: value.categoryIds ?? [],
    contacts: (value.contacts ?? []).map((contact) => ({
      id: contact.id ?? undefined,
      name: contact.name ?? '',
      email: contact.email ?? '',
      phone: contact.phone ?? '',
      role: contact.role ?? '',
    })),
    addresses: (value.addresses ?? []).map((address) => ({
      id: address.id ?? undefined,
      type: normalizeAddressType(address.type),
      postalCode: address.postalCode ?? '',
      street: address.street ?? '',
      number: address.number ?? '',
      complement: address.complement ?? '',
      district: address.district ?? '',
      city: address.city ?? '',
      state: address.state ?? '',
      country: address.country ?? '',
    })),
  };
}
function normalizeAddressType(value: string | null | undefined): SupplierAddress['type'] {
  return value === 'Billing' || value === 'Shipping' || value === 'Other' ? value : 'Headquarters';
}
function versionHeaders(version: string) {
  return { 'If-Match': `"${version}"` };
}
function idempotencyHeaders() {
  return { 'Idempotency-Key': crypto.randomUUID() };
}
