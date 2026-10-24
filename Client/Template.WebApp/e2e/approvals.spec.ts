import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type Route } from '@playwright/test';

/**
 * Phase 31 approval inbox and detail against a deterministic same-origin fixture of the v2 approval-instances
 * contract (allowedActions, If-Match/ETag, Idempotency-Key, Problem Details). Backend unit, functional, PostgreSQL and
 * Durable emulator suites prove the rules and the orchestration; these journeys prove the browser experience,
 * stale-action handling and accessibility.
 */
interface ApprovalState {
  instance: Record<string, unknown>;
  decisions: { body: Record<string, unknown>; ifMatch?: string; key?: string }[];
  answer: 'ok' | 'stale' | 'unavailable';
}

const APPROVAL_ID = '33333333-2222-3333-4444-555555555555';

const approver = (permissions: string[]) => ({
  userId: 'user-head',
  userName: 'diana',
  displayName: 'Diana Diretora',
  permissions,
});

const APPROVER_PERMISSIONS = ['organization.read', 'approvals.decide', 'approvals.delegate'];

const money = (amount: string) => ({ amount, currency: 'BRL' });

function instance(overrides: Record<string, unknown> = {}) {
  return {
    id: APPROVAL_ID,
    version: '3',
    status: 'Pending',
    requisitionId: 'req-1',
    requisitionNumber: 'REQ-2027-000001',
    requesterName: 'Rita Requisitante',
    amountEvaluated: money('16200.00'),
    requisitionAmount: money('16200.00'),
    policy: 'ApprovalPolicy:1',
    currentLevel: 2,
    finalLevel: 2,
    nextAction: 'Wait',
    startedAt: '2027-03-01T13:00:00Z',
    steps: [
      {
        level: 1,
        resolver: 'CostCenterManager',
        status: 'Approved',
        assigneeName: 'Carlos Centro',
        escalationCount: 0,
        decision: 'Approved',
        decidedByName: 'Carlos Centro',
        decidedAt: '2027-03-01T15:00:00Z',
      },
      {
        level: 2,
        resolver: 'DepartmentHead',
        status: 'Pending',
        assigneeUserId: 'user-head',
        assigneeName: 'Diana Diretora',
        dueAt: '2020-01-01T00:00:00Z',
        escalationCount: 1,
      },
    ],
    award: {
      rfqId: 'rfq-1',
      rfqNumber: 'RFQ-2027-000001',
      supplierName: 'Fornecedor Alfa Ltda',
      rank: 1,
      awarded: money('16200.00'),
      normalized: money('16200.00'),
      waivers: [],
      comparison: [
        {
          supplierId: 's1',
          supplierName: 'Fornecedor Alfa Ltda',
          rank: 1,
          isValid: true,
          quoted: money('16200.00'),
          normalized: money('16200.00'),
          deliveryDays: 10,
          awarded: true,
        },
        {
          supplierId: 's2',
          supplierName: 'Fornecedor Beta SA',
          rank: 2,
          isValid: true,
          quoted: money('17100.00'),
          normalized: money('17100.00'),
          deliveryDays: 7,
          awarded: false,
        },
      ],
    },
    allowedActions: ['approve', 'reject', 'delegate'],
    ...overrides,
  };
}

const decided = (decision: 'Approved' | 'Rejected', reason?: string) => {
  const current = instance();
  const steps = (current.steps as Record<string, unknown>[]).map((step) =>
    step['level'] === 2
      ? {
          ...step,
          status: decision,
          decision,
          decidedByName: 'Diana Diretora',
          decidedAt: '2027-03-02T12:00:00Z',
          reason,
        }
      : step,
  );
  return instance({
    version: '4',
    status: decision,
    nextAction: 'Completed',
    endedAt: '2027-03-02T12:00:00Z',
    steps,
    allowedActions: [],
  });
};

async function installFixture(page: Page, permissions: string[]) {
  const state: ApprovalState = { instance: instance(), decisions: [], answer: 'ok' };
  let loggedIn = false;
  await page.route('**/api/v2/**', async (route: Route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();
    const json = (body: unknown, status = 200) =>
      route.fulfill({
        status,
        contentType: status >= 400 ? 'application/problem+json' : 'application/json',
        body: JSON.stringify(body),
      });

    if (path === '/api/v2/auth/sessions/refresh')
      return loggedIn
        ? json({ accessToken: 'token' })
        : json({ code: 'auth.session-expired' }, 401);
    if (path === '/api/v2/auth/sessions' && method === 'POST') {
      loggedIn = true;
      return json({ accessToken: 'token' });
    }
    if (path === '/api/v2/account/me') return json(approver(permissions));
    if (path === '/api/v2/approval-instances' && method === 'GET') {
      const pending = state.instance['status'] === 'Pending';
      return json({
        items: pending
          ? [
              {
                id: APPROVAL_ID,
                version: state.instance['version'],
                status: 'Pending',
                requisitionId: 'req-1',
                requisitionNumber: 'REQ-2027-000001',
                amountEvaluated: money('16200.00'),
                currentLevel: 2,
                finalLevel: 2,
                assigneeUserId: 'user-head',
                dueAt: '2020-01-01T00:00:00Z',
                escalated: true,
                startedAt: '2027-03-01T13:00:00Z',
              },
            ]
          : [],
        page: 1,
        pageSize: 50,
        totalItems: pending ? 1 : 0,
        totalPages: 1,
      });
    }
    if (path === `/api/v2/approval-instances/${APPROVAL_ID}`) return json(state.instance);
    if (path === `/api/v2/approval-instances/${APPROVAL_ID}/history`)
      return json({
        id: APPROVAL_ID,
        entries: [
          { sequence: 1, action: 'started', at: '2027-03-01T13:00:00Z' },
          {
            sequence: 2,
            action: 'step-approved',
            level: 1,
            actorName: 'Carlos Centro',
            at: '2027-03-01T15:00:00Z',
          },
          { sequence: 3, action: 'step-escalated', level: 2, at: '2027-03-02T09:00:00Z' },
        ],
      });
    if (path === `/api/v2/approval-instances/${APPROVAL_ID}/steps/2/decision`) {
      const headers = request.headers();
      const body = request.postDataJSON() as Record<string, unknown>;
      state.decisions.push({ body, ifMatch: headers['if-match'], key: headers['idempotency-key'] });
      if (state.answer === 'unavailable')
        return json({ code: 'approval.workflow-unavailable', status: 503 }, 503);
      if (state.answer === 'stale') {
        state.instance = decided('Approved', 'Aprovado por outra sessão');
        return json({ code: 'concurrency.version-mismatch', currentVersion: '4' }, 412);
      }
      state.instance = decided(
        body['decision'] as 'Approved' | 'Rejected',
        body['reason'] as string | undefined,
      );
      return json(state.instance);
    }
    if (path === `/api/v2/approval-instances/${APPROVAL_ID}/steps/2/delegates`)
      return json({
        items: [
          {
            userId: 'user-other',
            userName: 'otto',
            displayName: 'Otto Outro',
            eligible: true,
          },
          {
            userId: 'user-rita',
            userName: 'rita',
            displayName: 'Rita Requisitante',
            eligible: false,
            ineligibleReason: 'separation-of-duties',
          },
        ],
        page: 1,
        pageSize: 20,
        totalItems: 2,
        totalPages: 1,
      });
    return json({ code: 'fixture.unhandled', detail: `${method} ${path}` }, 500);
  });
  return state;
}

async function axeViolations(page: Page) {
  const result = await new AxeBuilder({ page }).analyze();
  return result.violations.map(
    (violation) =>
      `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(' | ')}`,
  );
}

async function login(page: Page) {
  await page.goto('/');
  await page.getByLabel('Usuário').fill('diana');
  await page.getByLabel('Senha').fill('secret');
  await page.getByRole('button', { name: 'Acessar' }).click();
  await expect(page.getByRole('heading', { name: 'Painel' })).toBeVisible();
}

test('approver sees an escalated overdue step, validates the rejection reason and rejects with If-Match', async ({
  page,
}) => {
  const state = await installFixture(page, APPROVER_PERMISSIONS);
  await login(page);
  await page.getByRole('link', { name: 'Aprovações' }).click();
  await expect(page.getByRole('heading', { name: 'Caixa de aprovações', level: 1 })).toBeVisible();

  const row = page.locator(`tr[data-approval="${APPROVAL_ID}"]`);
  await expect(row).toContainText('REQ-2027-000001');
  await expect(row).toContainText('Escalonada');
  await expect(row).toContainText('Prazo vencido');
  expect(await axeViolations(page)).toEqual([]);

  await row.getByRole('link', { name: 'Abrir' }).click();
  await expect(page.getByRole('heading', { name: 'Aprovação REQ-2027-000001' })).toBeVisible();
  await expect(page.locator('li[data-step="2"]')).toContainText('Escalonada 1 vez(es)');
  await expect(page.locator('li[data-step="2"]')).toHaveAttribute('aria-current', 'step');
  await expect(page.getByText('Fornecedor Beta SA')).toBeVisible();
  await expect(page.getByText('Etapa escalonada')).toBeVisible();
  expect(await axeViolations(page)).toEqual([]);

  await page.getByRole('button', { name: 'Rejeitar' }).click();
  await page.getByLabel('Motivo').fill('curto');
  await page.getByRole('button', { name: 'Confirmar rejeição' }).click();
  await expect(page.getByText('Informe um motivo com pelo menos 10 caracteres.')).toBeVisible();
  await expect(page.getByLabel('Motivo')).toHaveAttribute('aria-invalid', 'true');
  expect(state.decisions).toHaveLength(0);
  expect(await axeViolations(page)).toEqual([]);

  await page.getByLabel('Motivo').fill('Preço acima da referência de mercado');
  await page.getByRole('button', { name: 'Confirmar rejeição' }).click();
  await expect(page.getByRole('status')).toContainText('Aprovação atualizada: Rejeitada');
  expect(state.decisions[0]).toMatchObject({
    body: { decision: 'Rejected', reason: 'Preço acima da referência de mercado' },
    ifMatch: '"3"',
  });
  expect(state.decisions[0]!.key).toBeTruthy();
  await expect(page.getByRole('button', { name: 'Aprovar' })).toHaveCount(0);
});

test('a stale approval reloads the current state instead of retrying, and an outage is explained, also on mobile', async ({
  page,
}) => {
  const state = await installFixture(page, APPROVER_PERMISSIONS);
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page);
  await page.goto(`/approvals/${APPROVAL_ID}`);

  state.answer = 'unavailable';
  await page.getByRole('button', { name: 'Aprovar' }).click();
  await page.getByRole('button', { name: 'Confirmar aprovação' }).click();
  await expect(page.getByRole('alert')).toContainText(
    'O serviço de fluxo de aprovação está indisponível',
  );
  expect(await axeViolations(page)).toEqual([]);

  state.answer = 'stale';
  await page.getByRole('button', { name: 'Confirmar aprovação' }).click();
  await expect(page.getByText('Esta aprovação mudou desde que você a abriu.')).toBeVisible();
  await expect(page.locator('li[data-step="2"]')).toContainText('Aprovado por outra sessão');
  await expect(page.getByRole('button', { name: 'Aprovar' })).toHaveCount(0);
  expect(state.decisions).toHaveLength(2);
  expect(await axeViolations(page)).toEqual([]);
});

test('delegation lists only eligible approvers as choices', async ({ page }) => {
  await installFixture(page, APPROVER_PERMISSIONS);
  await login(page);
  await page.goto(`/approvals/${APPROVAL_ID}`);
  await page.getByRole('button', { name: 'Delegar' }).click();
  const select = page.getByLabel('Delegar para');
  // Playwright's enabled state does not apply to <option>; the disabled attribute is the browser contract.
  await expect(select.locator('option', { hasText: 'Otto Outro' })).not.toHaveAttribute('disabled');
  await expect(select.locator('option', { hasText: 'Rita Requisitante' })).toHaveAttribute(
    'disabled',
    '',
  );
  await expect(select.locator('option', { hasText: 'Rita Requisitante' })).toContainText(
    'segregação de funções',
  );
  expect(await axeViolations(page)).toEqual([]);
});

test('a user without approvals.decide sees no approval inbox', async ({ page }) => {
  await installFixture(page, ['organization.read']);
  await login(page);
  await expect(page.getByRole('link', { name: 'Aprovações' })).toHaveCount(0);
  await page.goto('/approvals/inbox');
  await expect(page).toHaveURL(/\/forbidden/);
});
