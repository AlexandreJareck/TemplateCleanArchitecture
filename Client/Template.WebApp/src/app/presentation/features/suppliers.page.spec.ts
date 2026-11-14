import { TestBed } from '@angular/core/testing';
import { success } from '@application/common/result';
import type {
  MasterDataPort,
  SupplierDetail,
  SupplierSummary,
} from '@application/master-data/master-data';
import { MASTER_DATA_PORT } from '../state/ports';
import {
  page,
  provideTestRoute,
  provideTestSession,
  provideTestTransloco,
  settle,
  text,
} from '../testing/presentation-test-kit';
import { SuppliersPage } from './suppliers.page';

const summary: SupplierSummary = {
  id: 's1',
  legalName: 'Alfa Tecnologia Ltda',
  tradeName: 'Alfa',
  taxId: '12.345.678/0001-95',
  status: 'Active',
  eligible: true,
  version: '3',
};

const detail: SupplierDetail = {
  ...summary,
  country: 'BR',
  missingForActivation: '',
  categoryIds: ['c1', 'c2'],
  contacts: [{ id: 'k1', name: 'Ana Souza', email: 'ana@alfa.test', phone: '', role: 'Vendas' }],
  addresses: [],
};

async function render(permissions: readonly string[]) {
  const port = {
    listSuppliers: vi.fn(async () => success(page([summary]))),
    getSupplier: vi.fn(async () => success(detail)),
    saveSupplier: vi.fn(),
    changeSupplierStatus: vi.fn(),
  } as unknown as MasterDataPort;
  TestBed.configureTestingModule({
    imports: [SuppliersPage],
    providers: [
      ...provideTestTransloco('pt-BR'),
      ...provideTestRoute(),
      ...provideTestSession(permissions),
      { provide: MASTER_DATA_PORT, useValue: port },
    ],
  });
  const fixture = TestBed.createComponent(SuppliersPage);
  await settle(fixture);
  const root = fixture.nativeElement as HTMLElement;
  const openDetails = async () => {
    root.querySelector<HTMLButtonElement>('tbody button')!.click();
    await settle(fixture);
  };
  return { fixture, root, port, openDetails };
}

describe('SuppliersPage', () => {
  it('shows the supplier status in Portuguese (F-05)', async () => {
    const { root } = await render(['suppliers.read']);
    expect(text(root.querySelector('tbody .status-badge'))).toBe('Ativo');
    expect(text(root.querySelector('tbody'))).not.toContain('Active');
  });

  it('opens a read-only detail for suppliers.read without mutating controls (F-01)', async () => {
    const { root, port, openDetails } = await render(['suppliers.read']);
    await openDetails();
    expect(port.getSupplier).toHaveBeenCalledWith('s1');
    const readonly = root.querySelector('[data-testid="supplier-readonly"]');
    expect(text(readonly)).toContain('Alfa Tecnologia Ltda');
    expect(text(readonly)).toContain('Ana Souza');
    expect(text(readonly)).toContain('2 categoria(s) vinculada(s)');
    expect(root.querySelector('#legalName')).toBeNull();
    expect(root.querySelector('#contactName')).toBeNull();
    expect(text(root.querySelector('.status-actions'))).toContain('Ativo');
    expect(text(root.querySelector('.status-actions'))).not.toContain('Suspender');
    expect(text(root.querySelector('.toolbar'))).not.toContain('Cadastrar');
  });

  it('renders long contact values in the wrapping read-only summary (F-01)', async () => {
    const email = 'relacionamento.comercial.fornecedores@alfa-tecnologia-servicos.test';
    const { root, port, openDetails } = await render(['suppliers.read']);
    vi.mocked(port.getSupplier).mockResolvedValue(
      success({
        ...detail,
        contacts: [{ id: 'k1', name: 'Ana Souza', email, phone: '+55 11 99999-0000', role: '' }],
      }),
    );
    await openDetails();
    const readonly = root.querySelector('[data-testid="supplier-readonly"]')!;
    expect(readonly.tagName).toBe('DL');
    expect(readonly.classList).toContain('readonly-summary');
    expect(readonly.querySelectorAll(':scope > div > dt + dd').length).toBe(
      readonly.querySelectorAll(':scope > div').length,
    );
    expect(text(readonly)).toContain(email);
    expect(text(readonly)).toContain('+55 11 99999-0000');
  });

  it('joins street and number without a space before the comma', async () => {
    const address = {
      type: 'Headquarters' as const,
      postalCode: '01310-100',
      street: 'Avenida Paulista',
      number: '1000',
      complement: '',
      district: 'Bela Vista',
      city: 'São Paulo',
      state: 'SP',
      country: 'BR',
    };
    const { root, port, openDetails } = await render(['suppliers.read']);
    vi.mocked(port.getSupplier).mockResolvedValue(success({ ...detail, addresses: [address] }));
    await openDetails();
    const readonly = root.querySelector('[data-testid="supplier-readonly"]');
    expect(text(readonly)).toContain('Avenida Paulista, 1000 São Paulo/SP · 01310-100');
    expect(text(readonly)).not.toContain(' ,');

    TestBed.resetTestingModule();
    const withoutNumber = await render(['suppliers.read']);
    vi.mocked(withoutNumber.port.getSupplier).mockResolvedValue(
      success({ ...detail, addresses: [{ ...address, number: '' }] }),
    );
    await withoutNumber.openDetails();
    const summaryText = text(withoutNumber.root.querySelector('[data-testid="supplier-readonly"]'));
    expect(summaryText).toContain('Avenida Paulista São Paulo/SP');
    expect(summaryText).not.toContain(',');
  });

  it('keeps edit forms for suppliers.manage and status actions for suppliers.status.change', async () => {
    const manager = await render(['suppliers.read', 'suppliers.manage']);
    await manager.openDetails();
    expect(manager.root.querySelector('#legalName')).not.toBeNull();
    expect(manager.root.querySelector('[data-testid="supplier-readonly"]')).toBeNull();
    expect(text(manager.root.querySelector('.status-actions'))).not.toContain('Suspender');
    TestBed.resetTestingModule();

    const status = await render(['suppliers.read', 'suppliers.status.change']);
    await status.openDetails();
    expect(status.root.querySelector('#legalName')).toBeNull();
    expect(text(status.root.querySelector('.status-actions'))).toContain('Suspender');
  });
});
