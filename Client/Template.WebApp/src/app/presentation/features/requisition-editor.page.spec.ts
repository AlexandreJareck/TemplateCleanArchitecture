import { TestBed } from '@angular/core/testing';
import { success } from '@application/common/result';
import type {
  MasterDataItem,
  MasterDataKind,
  MasterDataPort,
  Page,
} from '@application/master-data/master-data';
import type {
  Requisition,
  RequisitionDraftInput,
  RequisitionPort,
} from '@application/procurement/procurement';
import type { Result } from '@application/common/result';
import { MASTER_DATA_PORT, REQUISITION_PORT } from '../state/ports';
import {
  deferred,
  page,
  provideTestRoute,
  provideTestTransloco,
  settle,
} from '../testing/presentation-test-kit';
import { RequisitionEditorPage } from './requisition-editor.page';

const costCenter = (id: string, code: string, active = true): MasterDataItem => ({
  id,
  code,
  name: `${code} name`,
  active,
  version: '1',
});

const persisted: Requisition = {
  id: 'r1',
  number: 'REQ-2026-000001',
  costCenter: { id: 'cc-infra', code: 'TEC-INFRA', name: 'Infraestrutura e Cloud' },
  estimatedTotal: { amount: '0.00', currency: 'BRL' },
  status: 'Draft',
  revision: 1,
  createdAt: '2026-11-02T16:55:03Z',
  version: 'v7',
  requesterId: 'u1',
  currency: 'BRL',
  purpose: 'Servidores',
  lines: [],
  attachments: [],
  submissions: [],
  allowedActions: ['edit', 'discard'],
};

function setup(costCenters: readonly MasterDataItem[]) {
  const requisition = deferred<Result<Requisition>>();
  const lookups = deferred<Result<Page<MasterDataItem>>>();
  const update = vi.fn<
    (current: Requisition, input: RequisitionDraftInput) => Promise<Result<Requisition>>
  >(async () => success(persisted));
  const requisitionPort = {
    get: () => requisition.promise,
    update,
    availability: async () => success({}),
  } as unknown as RequisitionPort;
  const masterData = {
    list: (kind: MasterDataKind) =>
      kind === 'cost-centers' ? lookups.promise : Promise.resolve(success(page([]))),
  } as unknown as MasterDataPort;
  TestBed.configureTestingModule({
    imports: [RequisitionEditorPage],
    providers: [
      ...provideTestTransloco(),
      ...provideTestRoute({ id: 'r1' }),
      { provide: REQUISITION_PORT, useValue: requisitionPort },
      { provide: MASTER_DATA_PORT, useValue: masterData },
    ],
  });
  const fixture = TestBed.createComponent(RequisitionEditorPage);
  return {
    fixture,
    update,
    resolveRequisition: () => requisition.resolve(success(persisted)),
    resolveLookups: () => lookups.resolve(success(page(costCenters))),
    costCenterSelect: () =>
      (fixture.nativeElement as HTMLElement).querySelector<HTMLSelectElement>('select')!,
  };
}

describe('RequisitionEditorPage cost center (F-02)', () => {
  const options = [costCenter('cc-adm', 'ADM'), costCenter('cc-infra', 'TEC-INFRA')];

  it('selects the persisted cost center when the requisition loads before the options', async () => {
    const harness = setup(options);
    harness.resolveRequisition();
    await settle(harness.fixture);
    harness.resolveLookups();
    await settle(harness.fixture);
    expect(harness.costCenterSelect().value).toBe('cc-infra');
    expect(harness.costCenterSelect().selectedOptions[0]?.textContent).toContain('TEC-INFRA');
  });

  it('selects the persisted cost center when the options load before the requisition', async () => {
    const harness = setup(options);
    harness.resolveLookups();
    await settle(harness.fixture);
    harness.resolveRequisition();
    await settle(harness.fixture);
    expect(harness.costCenterSelect().value).toBe('cc-infra');
  });

  it('keeps an inactive persisted cost center selectable instead of showing an empty choice', async () => {
    const harness = setup([costCenter('cc-adm', 'ADM')]);
    harness.resolveLookups();
    harness.resolveRequisition();
    await settle(harness.fixture);
    expect(harness.costCenterSelect().value).toBe('cc-infra');
  });

  it('preserves the cost center ID on a save without changes', async () => {
    const harness = setup(options);
    harness.resolveRequisition();
    harness.resolveLookups();
    await settle(harness.fixture);
    const form = (harness.fixture.nativeElement as HTMLElement).querySelector('form')!;
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(harness.fixture);
    expect(harness.update).toHaveBeenCalledTimes(1);
    expect(harness.update.mock.calls[0]![1].costCenterId).toBe('cc-infra');
    expect(harness.update.mock.calls[0]![0].version).toBe('v7');
  });
});
