import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ApplicationError } from '@application/common/application-error';
import { success } from '@application/common/result';
import {
  provideTestSession,
  provideTestTransloco,
  settle,
  text,
} from '../testing/presentation-test-kit';
import { USER_DIRECTORY_PORT } from '../state/ports';
import { ActorName, FileSizePipe, InstantPipe, LocalDatePipe } from './display';
import { ProblemFeedback, StatusBadge } from './procurement-ui';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ActorName, InstantPipe, LocalDatePipe, FileSizePipe, StatusBadge, ProblemFeedback],
  template: `
    <p id="date">{{ '2026-12-17' | localDate }}</p>
    <p id="instant">{{ '2026-11-02T16:55:03.417612Z' | instant }}</p>
    <p id="size">{{ 2048 | fileSize }}</p>
    <p id="self"><app-actor [actorId]="selfId" /></p>
    <p id="other"><app-actor [actorId]="otherId" /></p>
    <p id="system"><app-actor [actorId]="null" /></p>
    <p id="supplier"><app-status-badge prefix="supplierStatuses" status="Suspended" /></p>
    <p id="workflow"><app-status-badge status="PartiallyReceived" /></p>
    <p id="unknown"><app-status-badge status="BrandNew" /></p>
    <app-problem-feedback [error]="problem()" />
  `,
})
class DisplayHost {
  readonly selfId = 'd0000000-0000-0000-0000-000000000001';
  readonly otherId = 'd0000000-0000-0000-0000-000000000002';
  readonly problem = input<ApplicationError | undefined>({
    kind: 'business-rule',
    code: 'budget.insufficient',
    detail: 'The allocation has 10.00 available.',
    ruleId: 'BUD-005',
    traceId: '00-trace-01',
  });
}

async function render(permissions: string[] = []) {
  TestBed.configureTestingModule({
    imports: [DisplayHost],
    providers: [...provideTestTransloco('pt-BR'), ...provideTestSession(permissions)],
  });
  const fixture = TestBed.createComponent(DisplayHost);
  await settle(fixture);
  const root = fixture.nativeElement as HTMLElement;
  return { fixture, root, read: (id: string) => text(root.querySelector(`#${id}`)) };
}

describe('localized display primitives (F-05, F-06)', () => {
  it('formats dates, instants and sizes for pt-BR in the organization zone', async () => {
    const { read } = await render();
    expect(read('date')).toBe('17/12/2026');
    expect(read('instant')).toBe('02/11/2026, 13:55');
    expect(read('size')).toBe('2 KB');
  });

  it('names actors without showing their technical ID', async () => {
    const { root, read } = await render();
    expect(read('self')).toBe('Demo User (você)');
    expect(read('other')).toBe('Outro usuário');
    expect(read('system')).toBe('Sistema');
    expect(root.querySelector('#other .actor')!.getAttribute('title')).toContain(
      'd0000000-0000-0000-0000-000000000002',
    );
  });

  it('names other users from the directory when the session may read it', async () => {
    TestBed.overrideProvider(USER_DIRECTORY_PORT, {
      useValue: {
        list: async () =>
          success([
            {
              id: 'd0000000-0000-0000-0000-000000000002',
              userName: 'demo.requester',
              displayName: 'Demo Requester',
              active: true,
            },
          ]),
      },
    });
    const { read } = await render(['access.users.manage']);
    expect(read('other')).toBe('Demo Requester');
  });

  it('localizes statuses per entity and falls back to the raw value for unknown ones', async () => {
    const { read } = await render();
    expect(read('supplier')).toBe('Suspenso');
    expect(read('workflow')).toBe('Recebido parcialmente');
    expect(read('unknown')).toBe('BrandNew');
  });

  it('shows a localized business message and keeps code, detail and trace in technical details', async () => {
    const { root } = await render();
    const feedback = root.querySelector('[data-testid="problem-feedback"]')!;
    const details = feedback.querySelector('details')!;
    const main = text(feedback).replace(text(details), '');
    expect(main).toContain('Regra de negócio não atendida');
    expect(main).toContain('Saldo orçamentário insuficiente.');
    expect(main).not.toContain('budget.insufficient');
    expect(main).not.toContain('00-trace-01');
    expect(details.open).toBe(false);
    expect(text(details)).toContain('budget.insufficient');
    expect(text(details)).toContain('BUD-005');
    expect(text(details)).toContain('00-trace-01');
  });
});
