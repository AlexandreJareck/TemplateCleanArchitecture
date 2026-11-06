import type { Result } from '../common/result';

export type MasterDataKind =
  'departments' | 'cost-centers' | 'categories' | 'units' | 'catalog-items';

export interface PageRequest {
  readonly page: number;
  readonly pageSize: number;
  readonly q?: string;
  readonly sort?: string;
}

export interface Page<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
  readonly totalPages: number;
}

export interface MasterDataItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly active: boolean;
  readonly version: string;
  readonly detail?: string;
  readonly relatedId?: string;
  readonly secondaryRelatedId?: string;
  readonly ownerUserId?: string;
}

export interface MasterDataInput {
  readonly code: string;
  readonly name: string;
  readonly relatedId?: string;
  readonly secondaryRelatedId?: string;
  readonly kind?: 'Good' | 'Service';
  readonly allowedDecimals?: number;
  readonly ownerUserId?: string;
}

export interface SupplierSummary {
  readonly id: string;
  readonly legalName: string;
  readonly tradeName: string;
  readonly taxId: string;
  readonly status: string;
  readonly eligible: boolean;
  readonly version: string;
}

export interface SupplierDetail extends SupplierSummary {
  readonly country: string;
  readonly missingForActivation: string;
  readonly categoryIds: readonly string[];
  readonly contacts: readonly SupplierContact[];
  readonly addresses: readonly SupplierAddress[];
}

export interface SupplierContact {
  readonly id?: string;
  readonly name: string;
  readonly email: string;
  readonly phone: string;
  readonly role: string;
}

export interface SupplierAddress {
  readonly id?: string;
  readonly type: 'Headquarters' | 'Billing' | 'Shipping' | 'Other';
  readonly postalCode: string;
  readonly street: string;
  readonly number: string;
  readonly complement: string;
  readonly district: string;
  readonly city: string;
  readonly state: string;
  readonly country: string;
}

export interface SupplierInput {
  readonly legalName: string;
  readonly tradeName: string;
  readonly country: string;
  readonly taxId: string;
}

export interface EnrichmentProposal {
  readonly provider: string;
  readonly retrievedAt: string;
  readonly legalName: string;
  readonly tradeName: string;
  readonly notice: string;
}

export interface MasterDataPort {
  list(
    kind: MasterDataKind,
    request: PageRequest,
    signal?: AbortSignal,
  ): Promise<Result<Page<MasterDataItem>>>;
  create(
    kind: MasterDataKind,
    input: MasterDataInput,
    signal?: AbortSignal,
  ): Promise<Result<MasterDataItem>>;
  update(
    kind: MasterDataKind,
    item: MasterDataItem,
    input: MasterDataInput,
    signal?: AbortSignal,
  ): Promise<Result<MasterDataItem>>;
  changeActive(
    kind: MasterDataKind,
    item: MasterDataItem,
    active: boolean,
    reason: string,
    signal?: AbortSignal,
  ): Promise<Result<MasterDataItem>>;
  listSuppliers(request: PageRequest, signal?: AbortSignal): Promise<Result<Page<SupplierSummary>>>;
  getSupplier(id: string, signal?: AbortSignal): Promise<Result<SupplierDetail>>;
  saveSupplier(
    id: string | undefined,
    version: string | undefined,
    input: SupplierInput,
    signal?: AbortSignal,
  ): Promise<Result<SupplierDetail>>;
  changeSupplierStatus(
    item: SupplierDetail,
    action: 'activate' | 'suspend' | 'inactivate' | 'reactivate',
    reason: string,
    signal?: AbortSignal,
  ): Promise<Result<SupplierDetail>>;
  lookupCompany(taxId: string, signal?: AbortSignal): Promise<Result<EnrichmentProposal>>;
  acceptEnrichment(
    item: SupplierDetail,
    proposal: EnrichmentProposal,
    signal?: AbortSignal,
  ): Promise<Result<SupplierDetail>>;
  saveSupplierContacts(
    item: SupplierDetail,
    contacts: readonly SupplierContact[],
    signal?: AbortSignal,
  ): Promise<Result<SupplierDetail>>;
  saveSupplierAddresses(
    item: SupplierDetail,
    addresses: readonly SupplierAddress[],
    signal?: AbortSignal,
  ): Promise<Result<SupplierDetail>>;
  saveSupplierCategories(
    item: SupplierDetail,
    categoryIds: readonly string[],
    signal?: AbortSignal,
  ): Promise<Result<SupplierDetail>>;
}

/**
 * Stable syntactic code rules mirrored from the domain (`MasterDataRules`: ORG-001, CAT-001) for early feedback.
 * The server normalizes a code (trim + upper case) before matching, so the client does the same; the API stays
 * authoritative and still rejects anything these rules miss.
 */
export const MASTER_DATA_CODE_RULES: Readonly<
  Record<MasterDataKind, { readonly pattern: RegExp; readonly min: number; readonly max: number }>
> = {
  departments: { pattern: /^[A-Z0-9-]{2,20}$/, min: 2, max: 20 },
  'cost-centers': { pattern: /^[A-Z0-9-]{2,20}$/, min: 2, max: 20 },
  categories: { pattern: /^[A-Z0-9-]{1,20}$/, min: 1, max: 20 },
  units: { pattern: /^[A-Z0-9-]{1,20}$/, min: 1, max: 20 },
  'catalog-items': { pattern: /^[A-Z0-9][A-Z0-9._-]{0,39}$/, min: 1, max: 40 },
};

export const MASTER_DATA_NAME_MAX_LENGTH = 200;

export type MasterDataCodeProblem = 'length' | 'format';

export class MasterDataUseCases {
  static normalizeCode(code: string): string {
    return code.trim().toUpperCase();
  }

  /** `undefined` when the code satisfies the domain syntax for the kind (empty is left to `required`). */
  static codeProblem(kind: MasterDataKind, code: string): MasterDataCodeProblem | undefined {
    const normalized = MasterDataUseCases.normalizeCode(code);
    if (normalized === '') return undefined;
    const rule = MASTER_DATA_CODE_RULES[kind];
    if (normalized.length < rule.min || normalized.length > rule.max) return 'length';
    return rule.pattern.test(normalized) ? undefined : 'format';
  }

  constructor(private readonly port: MasterDataPort) {}
  list(kind: MasterDataKind, request: PageRequest, signal?: AbortSignal) {
    return this.port.list(kind, request, signal);
  }
  create(kind: MasterDataKind, input: MasterDataInput, signal?: AbortSignal) {
    return this.port.create(kind, input, signal);
  }
  update(kind: MasterDataKind, item: MasterDataItem, input: MasterDataInput, signal?: AbortSignal) {
    return this.port.update(kind, item, input, signal);
  }
  changeActive(
    kind: MasterDataKind,
    item: MasterDataItem,
    active: boolean,
    reason: string,
    signal?: AbortSignal,
  ) {
    return this.port.changeActive(kind, item, active, reason, signal);
  }
}
