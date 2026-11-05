import { TestBed } from '@angular/core/testing';
import type { AuditPort, AuditQuery, AuditRecord } from '@application/auditing/audit';
import { success } from '@application/common/result';
import { routes } from '../routes';
import { NAVIGATION } from '../shell/navigation';
import { AUDIT_PORT } from '../state/ports';
import {
  page,
  provideTestRoute,
  provideTestSession,
  provideTestTransloco,
  settle,
  text,
} from '../testing/presentation-test-kit';
import { AuditPage } from './audit.page';

const auditor = 'd0000000-0000-0000-0000-000000000009';
const record: AuditRecord = {
  id: 'a1',
  occurredAt: '2026-11-02T16:55:03Z',
  actorId: 'd0000000-0000-0000-0000-000000000002',
  action: 'requisition.submitted',
  resourceType: 'PurchaseRequisition',
  resourceId: 'r1',
  fromState: 'Draft',
  toState: 'Submitted',
  ruleIds: ['REQ-004'],
  details: [['number', 'REQ-2026-000001']],
};

async function render() {
  const list = vi.fn(async (query: AuditQuery) => {
    void query;
    return success(page([record]));
  });
  TestBed.configureTestingModule({
    imports: [AuditPage],
    providers: [
      ...provideTestTransloco('pt-BR'),
      ...provideTestRoute(),
      ...provideTestSession(['audit.read'], { id: auditor, displayName: 'Demo Auditor' }),
      { provide: AUDIT_PORT, useValue: { list } satisfies AuditPort },
    ],
  });
  const fixture = TestBed.createComponent(AuditPage);
  await settle(fixture);
  return { fixture, root: fixture.nativeElement as HTMLElement, list };
}

function input(root: HTMLElement, id: string, value: string) {
  const element = root.querySelector<HTMLInputElement>(`#${id}`)!;
  element.value = value;
  element.dispatchEvent(new Event('input'));
}

describe('Audit trail (F-03)', () => {
  it('is reachable only with audit.read through the route guard and the navigation', () => {
    const shell = routes.find((route) => route.path === '')!;
    const audit = shell.children!.find((route) => route.path === 'audit')!;
    expect(audit.data).toEqual({ permission: 'audit.read' });
    expect(audit.canActivate?.length).toBe(1);
    const item = NAVIGATION.flatMap((group) => group.items).find((nav) => nav.link === '/audit');
    expect(item?.anyOf).toEqual(['audit.read']);
  });

  it('lists records newest first with localized time, states and a human actor', async () => {
    const { root, list } = await render();
    expect(list).toHaveBeenCalledWith({ page: 1, pageSize: 20, sort: '-occurredAt' });
    const row = root.querySelector('[data-testid="audit-row"]')!;
    expect(text(row)).toContain('02/11/2026, 13:55:03');
    expect(text(row)).toContain('Rascunho');
    expect(text(row)).toContain('Submetida');
    expect(text(row)).toContain('Outro usuário');
    expect(text(row.querySelector('.actor'))).not.toContain('d0000000');
    expect(text(row.querySelector('details'))).toContain('REQ-2026-000001');
    expect(root.querySelector('button[aria-label*="Excluir"], button.delete')).toBeNull();
  });

  it('sends filters with local day bounds converted to UTC in the organization zone', async () => {
    const { fixture, root, list } = await render();
    input(root, 'auditAction', ' requisition.submitted ');
    input(root, 'auditFrom', '2026-11-01');
    input(root, 'auditTo', '2026-11-05');
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(fixture);
    expect(list).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 20,
      sort: '-occurredAt',
      action: 'requisition.submitted',
      from: '2026-11-01T03:00:00.000Z',
      to: '2026-11-06T03:00:00.000Z',
    });
  });

  it('blocks a malformed actor ID before calling the API', async () => {
    const { fixture, root, list } = await render();
    list.mockClear();
    input(root, 'auditActorId', 'demo.admin');
    fixture.detectChanges();
    root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(fixture);
    expect(list).not.toHaveBeenCalled();
    expect(text(root)).toContain('Informe o ID do usuário no formato GUID.');
  });
});
