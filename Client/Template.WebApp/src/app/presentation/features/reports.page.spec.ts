import { TestBed } from '@angular/core/testing';
import { failure, success, type Result } from '@application/common/result';
import type { MasterDataPort } from '@application/master-data/master-data';
import type { BudgetPort, RequisitionPort } from '@application/procurement/procurement';
import type { ReportJob, ReportPort, ReportTypeInfo } from '@application/reports/reports';
import type { SourcingPort } from '@application/sourcing/sourcing';
import {
  BUDGET_PORT,
  MASTER_DATA_PORT,
  REPORT_PORT,
  REQUISITION_PORT,
  SOURCING_PORT,
} from '../state/ports';
import {
  page,
  provideTestRoute,
  provideTestSession,
  provideTestTransloco,
  settle,
  text,
} from '../testing/presentation-test-kit';
import { ReportsPage } from './reports.page';

const catalog: ReportTypeInfo[] = [
  {
    type: 'RequisitionPdf',
    format: 'Pdf',
    requiredPermissions: [],
    parameters: ['requisitionId'],
    available: true,
  },
  {
    type: 'BudgetConsumptionXlsx',
    format: 'Xlsx',
    requiredPermissions: [],
    parameters: ['fiscalPeriodId', 'currency?', 'costCenterId?'],
    available: true,
  },
];

async function render(
  requestResult: Result<ReportJob> = failure({ kind: 'validation', code: 'validation' }),
) {
  const request = vi.fn(async () => requestResult);
  const reports = {
    types: async () => success(catalog),
    list: async () => success(page([])),
    request,
  } as unknown as ReportPort;
  const requisitions = {
    list: async () =>
      success(
        page([
          {
            id: '6f1c0b5e-0000-4000-8000-000000000001',
            number: 'REQ-2026-000001',
            costCenter: { id: 'cc', code: 'TEC-INFRA', name: 'Infra' },
            status: 'Draft',
          },
        ]),
      ),
  } as unknown as RequisitionPort;
  TestBed.configureTestingModule({
    imports: [ReportsPage],
    providers: [
      ...provideTestTransloco('pt-BR'),
      ...provideTestRoute(),
      ...provideTestSession(['reports.request', 'requisitions.read-all', 'budget.read']),
      { provide: REPORT_PORT, useValue: reports },
      { provide: REQUISITION_PORT, useValue: requisitions },
      {
        provide: BUDGET_PORT,
        useValue: { listPeriods: async () => success(page([])) } as unknown as BudgetPort,
      },
      {
        provide: SOURCING_PORT,
        useValue: { list: async () => success(page([])) } as unknown as SourcingPort,
      },
      {
        provide: MASTER_DATA_PORT,
        useValue: {
          list: async () => success(page([])),
          listSuppliers: async () => success(page([])),
        } as unknown as MasterDataPort,
      },
    ],
  });
  const fixture = TestBed.createComponent(ReportsPage);
  await settle(fixture);
  const root = fixture.nativeElement as HTMLElement;
  const submit = async () => {
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(fixture);
  };
  return { fixture, root, request, submit };
}

describe('ReportsPage parameters (F-04, F-07)', () => {
  it('blocks an incomplete request locally with Portuguese field feedback and no API keys', async () => {
    const { root, request, submit } = await render();
    await submit();
    expect(request).not.toHaveBeenCalled();
    const picker = root.querySelector('app-entity-picker')!;
    expect(text(picker)).toContain('Obrigatório para este tipo de relatório.');
    expect(text(root)).toContain('Revise os campos destacados');
    expect(text(root)).not.toContain('requisitionId');
    expect(text(root)).not.toContain('This parameter');
    expect(document.activeElement?.id).toBe('requisitionId');
  });

  it('offers requisitions by business number and submits the stable ID', async () => {
    const { fixture, root, request, submit } = await render(
      success({
        id: 'j1',
        reportType: 'RequisitionPdf',
        format: 'Pdf',
        status: 'Ready',
        parameters: {},
        requestedAt: '2026-11-05T12:00:00Z',
        asOf: '2026-11-05T12:00:00Z',
        timeZone: 'America/Sao_Paulo',
        attempts: 1,
        maxAttempts: 3,
        progress: 100,
        allowedActions: [],
        version: '1',
      }),
    );
    const select = root.querySelector<HTMLSelectElement>('#requisitionId')!;
    const option = [...select.options].find((item) => item.text.includes('REQ-2026-000001'))!;
    expect(option.text).toContain('TEC-INFRA');
    select.value = option.value;
    select.dispatchEvent(new Event('change'));
    await settle(fixture);
    await submit();
    expect(request).toHaveBeenCalledWith({
      reportType: 'RequisitionPdf',
      parameters: { requisitionId: '6f1c0b5e-0000-4000-8000-000000000001' },
    });
  });

  it('uses a controlled currency list for budget consumption', async () => {
    const { fixture, root } = await render();
    const type = root.querySelector<HTMLSelectElement>('#reportType')!;
    type.value = 'BudgetConsumptionXlsx';
    type.dispatchEvent(new Event('change'));
    await settle(fixture);
    const currency = root.querySelector<HTMLSelectElement>('#currency')!;
    expect(currency.tagName).toBe('SELECT');
    expect([...currency.options].map((option) => option.value)).toEqual(['', 'BRL', 'USD', 'EUR']);
    expect(root.querySelector('#costCenterId')?.tagName).toBe('SELECT');
  });

  it('maps a server validation problem to localized field copy with the trace only in details', async () => {
    const { fixture, root, submit } = await render(
      failure({
        kind: 'validation',
        code: 'validation',
        detail: 'One or more validation errors occurred.',
        traceId: '00-abc-01',
        fieldErrors: { requisitionId: ['This parameter is required for RequisitionPdf.'] },
      }),
    );
    const select = root.querySelector<HTMLSelectElement>('#requisitionId')!;
    select.value = select.options[1]!.value;
    select.dispatchEvent(new Event('change'));
    await settle(fixture);
    await submit();
    const feedback = root.querySelector('[data-testid="problem-feedback"]')!;
    const main = text(feedback).replace(text(feedback.querySelector('details')), '');
    expect(main).toContain('Revise os campos informados');
    expect(main).toContain('Requisição: Campo obrigatório');
    expect(main).not.toContain('requisitionId');
    expect(main).not.toContain('One or more');
    expect(main).not.toContain('00-abc-01');
    expect(text(feedback.querySelector('details'))).toContain('00-abc-01');
  });
});
