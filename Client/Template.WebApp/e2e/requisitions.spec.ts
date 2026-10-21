import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type Route } from '@playwright/test';

/**
 * Phase 27 requisition journeys against a deterministic same-origin HTTP fixture that follows the v2 contract
 * (ETag/If-Match, Idempotency-Key, Problem Details). The backend functional and PostgreSQL suites prove the real
 * rules; these journeys prove the browser experience.
 */
interface RequisitionState {
  version: number;
  status: string;
  lines: object[];
  total: string;
  available: string;
  conflictOnFirstLine: boolean;
  insufficientOnSubmit: boolean;
  cancelReason?: string;
  ifMatchSeen: string[];
  idempotencyKeys: string[];
}

const requester = {
  userId: 'user-req',
  userName: 'maria',
  displayName: 'Maria Requisitante',
  permissions: [
    'organization.read',
    'catalog.read',
    'requisitions.create',
    'requisitions.submit',
    'requisitions.cancel-own',
  ],
};

function requisition(state: RequisitionState) {
  const actions: Record<string, string[]> = {
    Draft: ['edit', 'discard', 'attach', 'submit', 'cancel'],
    Submitted: ['attach', 'withdraw', 'cancel'],
    Cancelled: ['copy'],
  };
  return {
    id: 'r1',
    number: 'REQ-2027-000001',
    requesterId: requester.userId,
    costCenter: { id: 'c1', code: 'CC-OPS', name: 'Operações', departmentId: 'd1' },
    purpose: 'Notebooks para a equipe de suporte',
    neededBy: '2027-04-30',
    currency: 'BRL',
    estimatedTotal: { amount: state.total, currency: 'BRL' },
    status: state.status,
    revision: 1,
    createdAt: '2027-03-01T13:00:00Z',
    lines: state.lines,
    attachments: [],
    submissions:
      state.status === 'Draft'
        ? []
        : [{ revision: 1, submittedAt: '2027-03-01T13:05:00Z', lines: state.lines }],
    allowedActions: actions[state.status] ?? [],
    version: String(state.version),
  };
}

async function installFixture(page: Page, overrides: Partial<RequisitionState> = {}) {
  const state: RequisitionState = {
    version: 1,
    status: 'Draft',
    lines: [],
    total: '0.00',
    available: '1000.00',
    conflictOnFirstLine: false,
    insufficientOnSubmit: false,
    ifMatchSeen: [],
    idempotencyKeys: [],
    ...overrides,
  };
  let loggedIn = false;
  await page.route('**/api/v2/**', async (route: Route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();
    const json = (body: unknown, status = 200) =>
      route.fulfill({
        status,
        contentType: status >= 400 ? 'application/problem+json' : 'application/json',
        headers:
          status < 400 && path.startsWith('/api/v2/purchase-requisitions/r1')
            ? { ETag: `"${state.version}"` }
            : {},
        body: JSON.stringify(body),
      });
    const page1 = (items: unknown[]) => ({
      items,
      page: 1,
      pageSize: 100,
      totalItems: items.length,
      totalPages: 1,
    });
    const ifMatch = request.headers()['if-match'];
    if (ifMatch) state.ifMatchSeen.push(ifMatch);
    const key = request.headers()['idempotency-key'];
    if (key) state.idempotencyKeys.push(key);

    if (path === '/api/v2/auth/sessions/refresh')
      return loggedIn
        ? json({ accessToken: 'token' })
        : json({ code: 'auth.session-expired' }, 401);
    if (path === '/api/v2/auth/sessions' && method === 'POST') {
      loggedIn = true;
      return json({ accessToken: 'token' });
    }
    if (path === '/api/v2/account/me') return json(requester);
    if (path === '/api/v2/cost-centers')
      return json(
        page1([
          {
            id: 'c1',
            code: 'CC-OPS',
            name: 'Operações',
            departmentId: 'd1',
            isActive: true,
            version: '1',
          },
        ]),
      );
    if (path === '/api/v2/categories')
      return json(page1([{ id: 'g1', code: 'IT', name: 'TI', isActive: true, version: '1' }]));
    if (path === '/api/v2/units-of-measure')
      return json(
        page1([
          {
            id: 'u1',
            code: 'UN',
            name: 'Unidade',
            allowedDecimals: 0,
            isActive: true,
            version: '1',
          },
        ]),
      );
    if (path === '/api/v2/catalog-items') return json(page1([]));
    if (path === '/api/v2/purchase-requisitions' && method === 'GET')
      return json(page1([requisition(state)]));
    if (path === '/api/v2/purchase-requisitions' && method === 'POST')
      return json(requisition(state), 201);
    if (path === '/api/v2/purchase-requisitions/r1' && method === 'GET')
      return json(requisition(state));
    if (path === '/api/v2/purchase-requisitions/r1/lines' && method === 'POST') {
      if (state.conflictOnFirstLine) {
        state.conflictOnFirstLine = false;
        state.version += 1; // someone else saved the draft meanwhile
        return json(
          {
            code: 'concurrency.version-mismatch',
            version: String(state.version),
            traceId: 'trace-412',
          },
          412,
        );
      }
      if (ifMatch !== `"${state.version}"`)
        return json({ code: 'concurrency.version-mismatch' }, 412);
      state.lines = [
        {
          lineNumber: 1,
          description: 'Notebook 14',
          kind: 'Good',
          categoryId: 'g1',
          categoryCode: 'IT',
          unitOfMeasureId: 'u1',
          unitCode: 'UN',
          quantity: '2',
          estimatedUnitPrice: { amount: '150.00', currency: 'BRL' },
          lineTotal: { amount: '300.00', currency: 'BRL' },
        },
      ];
      state.total = '300.00';
      state.version += 1;
      return json(requisition(state));
    }
    if (path === '/api/v2/purchase-requisitions/r1/budget-availability')
      return json({
        costCenterId: 'c1',
        currency: 'BRL',
        asOfDate: '2027-03-01',
        hasOpenAllocation: true,
        fiscalPeriodCode: 'FY27',
        available: { amount: state.available, currency: 'BRL' },
        reserved: { amount: '0.00', currency: 'BRL' },
      });
    if (path === '/api/v2/purchase-requisitions/r1/submit') {
      if (state.insufficientOnSubmit)
        return json(
          {
            code: 'budget.insufficient',
            ruleId: 'BUD-005',
            detail: 'The available budget is lower than the requested amount.',
            traceId: 'trace-422',
          },
          422,
        );
      state.status = 'Submitted';
      state.version += 1;
      return json(requisition(state));
    }
    if (path === '/api/v2/purchase-requisitions/r1/cancel') {
      state.cancelReason = (request.postDataJSON() as { reason: string }).reason;
      state.status = 'Cancelled';
      state.version += 1;
      return json(requisition(state));
    }
    if (path === '/api/v2/purchase-requisitions/r1/history') {
      const entries = [
        { kind: 'state', at: '2027-03-01T13:00:00Z', to: 'Draft', correlationId: 't' },
        ...(state.status !== 'Draft'
          ? [
              {
                kind: 'reservation',
                at: '2027-03-01T13:05:00Z',
                to: 'Reserved',
                amount: { amount: '300.00', currency: 'BRL' },
                correlationId: 't',
              },
              {
                kind: 'state',
                at: '2027-03-01T13:05:00Z',
                from: 'Draft',
                to: 'Submitted',
                correlationId: 't',
              },
            ]
          : []),
        ...(state.status === 'Cancelled'
          ? [
              {
                kind: 'reservation',
                at: '2027-03-01T13:10:00Z',
                from: 'Reserved',
                to: 'Released',
                reasonCode: 'Cancelled',
                amount: { amount: '300.00', currency: 'BRL' },
                correlationId: 't',
              },
              {
                kind: 'state',
                at: '2027-03-01T13:10:00Z',
                from: 'Submitted',
                to: 'Cancelled',
                reason: state.cancelReason,
                correlationId: 't',
              },
            ]
          : []),
      ];
      return json({ requisitionId: 'r1', entries });
    }
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
  await page.getByLabel('Usuário').fill('maria');
  await page.getByLabel('Senha').fill('secret');
  await page.getByRole('button', { name: 'Acessar' }).click();
  await expect(page.getByRole('heading', { name: 'Painel' })).toBeVisible();
}

async function createDraftWithLine(page: Page) {
  await page
    .getByRole('navigation', { name: 'Principal' })
    .getByRole('link', { name: 'Minhas requisições' })
    .click();
  await page.getByRole('link', { name: 'Nova requisição' }).click();
  await page.getByLabel('Centro de custo').selectOption('c1');
  await page.getByLabel('Necessário até').fill('2027-04-30');
  await page.getByLabel('Finalidade').fill('Notebooks para a equipe de suporte');
  await page.getByRole('button', { name: 'Salvar' }).first().click();
  await expect(page).toHaveURL(/\/requisitions\/r1\/edit$/);
  await page.getByLabel('Descrição').fill('Notebook 14');
  await page.getByLabel('Categorias').selectOption('g1');
  await page.getByLabel('Unidades de medida').selectOption('u1');
  await page.getByLabel('Quantidade').fill('2');
  await page.getByLabel('Preço unitário estimado (BRL)').fill('150');
  await expect(page.getByTestId('line-preview')).toContainText('300.00');
}

test('requester creates a draft, resolves a 412 conflict, submits against budget and cancels with a reason', async ({
  page,
}) => {
  const state = await installFixture(page, { conflictOnFirstLine: true });
  await login(page);
  await expect(
    page
      .getByRole('navigation', { name: 'Principal' })
      .getByRole('link', { name: 'Requisições da equipe' }),
  ).toHaveCount(0);
  await createDraftWithLine(page);

  await page.getByRole('button', { name: 'Salvar' }).nth(1).click();
  await expect(page.getByText('concurrency.version-mismatch')).toBeVisible();
  await expect(page.getByText('trace-412')).toBeVisible();
  await page.getByRole('button', { name: 'Recarregar versão atual' }).click();
  await page.getByRole('button', { name: 'Salvar' }).nth(1).click();
  await expect(page.getByText('Total calculado pelo servidor')).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Notebook 14 (IT)' })).toBeVisible();
  await expect(page.getByTestId('available')).toContainText('1.000,00');
  expect(await axeViolations(page)).toEqual([]);

  await page.getByRole('button', { name: 'Submeter' }).click();
  await expect(page.getByRole('dialog')).toContainText('Total 300.00 BRL em 1 item(ns)');
  await page.getByRole('dialog').getByRole('button', { name: 'Submeter' }).click();
  await expect(page).toHaveURL(/\/requisitions\/r1$/);
  await expect(page.getByText('Submetida').first()).toBeVisible();
  await expect(page.getByText('Reserva orçamentária').first()).toBeVisible();

  await page.getByRole('button', { name: 'Cancelar requisição' }).click();
  await page.getByLabel('Motivo').fill('Compra duplicada com outro centro de custo');
  await page.getByRole('dialog').getByRole('button', { name: 'Cancelar requisição' }).click();
  await expect(page.getByTestId('closing-reason')).toContainText(
    'Compra duplicada com outro centro de custo',
  );
  await expect(page.getByRole('button', { name: 'Copiar como novo rascunho' })).toBeVisible();

  expect(state.ifMatchSeen).toContain('"2"');
  expect(state.idempotencyKeys.length).toBeGreaterThanOrEqual(4);
  expect(new Set(state.idempotencyKeys).size).toBe(state.idempotencyKeys.length);
});

test('insufficient budget is explained, the draft stays editable, and the list collapses to cards on mobile', async ({
  page,
}) => {
  await installFixture(page, { available: '250.00', insufficientOnSubmit: true });
  await login(page);
  await createDraftWithLine(page);
  await page.getByRole('button', { name: 'Salvar' }).nth(1).click();
  await expect(page.getByText('A prévia indica saldo insuficiente')).toBeVisible();

  await page.getByRole('button', { name: 'Submeter' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Submeter' }).click();
  await expect(page.getByText('budget.insufficient')).toBeVisible();
  await expect(page.getByText('BUD-005')).toBeVisible();
  await expect(page).toHaveURL(/\/requisitions\/r1\/edit$/);
  await expect(page.getByRole('button', { name: 'Submeter' })).toBeEnabled();

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/requisitions/mine');
  await expect(page.getByRole('heading', { name: 'Minhas requisições' })).toBeVisible();
  const header = page.locator('thead');
  await expect(header).toHaveCSS('position', 'absolute');
  await expect(page.getByText('REQ-2027-000001')).toBeVisible();
  expect(await axeViolations(page)).toEqual([]);
});
