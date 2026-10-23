import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type Route } from '@playwright/test';

/**
 * Phase 30 report center against a deterministic same-origin fixture of the v2 report-jobs contract (202 + Location,
 * Idempotency-Key, polling, API-streamed download, Problem Details). Backend functional, PostgreSQL and rendering
 * suites prove the real rules and documents; these journeys prove the browser experience and accessibility.
 */
interface ReportState {
  jobs: Record<string, unknown>[];
  polls: number;
  postedBodies: Record<string, unknown>[];
  idempotencyKeys: string[];
  downloads: number;
  expireOnDownload: boolean;
}

const requester = (permissions: string[]) => ({
  userId: 'user-requester',
  userName: 'rita',
  displayName: 'Rita Requisitante',
  permissions,
});

const REQUESTER_PERMISSIONS = [
  'organization.read',
  'catalog.read',
  'requisitions.create',
  'requisitions.submit',
  'reports.request',
];

const types = [
  {
    type: 'RequisitionPdf',
    format: 'Pdf',
    available: true,
    requiredPermissions: ['reports.request'],
    parameters: ['requisitionId'],
  },
  {
    type: 'QuoteComparisonPdf',
    format: 'Pdf',
    available: false,
    requiredPermissions: ['reports.request', 'sourcing.quotes.read'],
    parameters: ['rfqId'],
  },
  {
    type: 'BudgetConsumptionXlsx',
    format: 'Xlsx',
    available: false,
    requiredPermissions: ['reports.request', 'budget.read'],
    parameters: ['fiscalPeriodId'],
  },
  {
    type: 'SupplierQuotesXlsx',
    format: 'Xlsx',
    available: false,
    requiredPermissions: ['reports.request'],
    parameters: ['from', 'to'],
  },
];

const JOB_ID = '11111111-2222-3333-4444-555555555555';

function job(overrides: Record<string, unknown> = {}) {
  return {
    id: JOB_ID,
    reportType: 'RequisitionPdf',
    format: 'Pdf',
    status: 'Pending',
    parameters: { requisitionId: 'r1' },
    requestedBy: 'user-requester',
    requestedAt: '2027-03-01T13:00:00Z',
    asOf: '2027-03-01T13:00:00Z',
    timeZone: 'America/Sao_Paulo',
    policyVersion: 1,
    templateVersion: 'requisition-pdf.v1',
    attempts: 0,
    maxAttempts: 3,
    progress: 0,
    allowedActions: ['cancel'],
    version: '1',
    ...overrides,
  };
}

const ready = (id = JOB_ID) =>
  job({
    id,
    status: 'Ready',
    attempts: 1,
    progress: 100,
    fileName: 'requisicao-REQ-2027-000001.pdf',
    mediaType: 'application/pdf',
    length: 12,
    expiresAt: '2027-03-08T13:00:00Z',
    contentUrl: `/api/v2/report-jobs/${id}/content`,
    allowedActions: ['download'],
    version: '4',
  });

async function installFixture(
  page: Page,
  permissions: string[],
  initialJobs: Record<string, unknown>[] = [],
) {
  const state: ReportState = {
    jobs: initialJobs,
    polls: 0,
    postedBodies: [],
    idempotencyKeys: [],
    downloads: 0,
    expireOnDownload: false,
  };
  let loggedIn = false;
  await page.route('**/api/v2/**', async (route: Route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();
    const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
      route.fulfill({
        status,
        contentType: status >= 400 ? 'application/problem+json' : 'application/json',
        headers,
        body: JSON.stringify(body),
      });
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
    if (path === '/api/v2/account/me') return json(requester(permissions));
    if (path === '/api/v2/report-jobs/types') return json(types);
    if (path === '/api/v2/report-jobs' && method === 'GET')
      return json({
        items: state.jobs,
        page: 1,
        pageSize: 50,
        totalItems: state.jobs.length,
        totalPages: 1,
      });
    if (path === '/api/v2/report-jobs' && method === 'POST') {
      const body = request.postDataJSON() as Record<string, unknown>;
      state.postedBodies.push(body);
      const id =
        state.postedBodies.length === 1
          ? JOB_ID
          : `22222222-2222-3333-4444-55555555555${state.postedBodies.length}`;
      const created = job({
        id,
        reportType: body['reportType'],
        parameters: { requisitionId: body['requisitionId'] },
      });
      state.jobs = [created, ...state.jobs];
      return json(created, 202, {
        Location: `/api/v2/report-jobs/${id}`,
        'Retry-After': '2',
        ETag: '"1"',
      });
    }
    const status = path.match(/^\/api\/v2\/report-jobs\/([^/]+)$/);
    if (status && method === 'GET') {
      state.polls += 1;
      const next =
        state.polls === 1
          ? job({ id: status[1], status: 'Running', progress: 40, attempts: 1, allowedActions: [] })
          : ready(status[1]);
      state.jobs = state.jobs.map((item) => (item['id'] === status[1] ? next : item));
      return json(next);
    }
    const content = path.match(/^\/api\/v2\/report-jobs\/([^/]+)\/content$/);
    if (content) {
      state.downloads += 1;
      if (state.expireOnDownload) return json({ code: 'report.expired', ruleId: 'RPT-006' }, 410);
      return route.fulfill({
        status: 200,
        contentType: 'application/pdf',
        headers: {
          'Content-Disposition':
            'attachment; filename="requisicao-REQ-2027-000001.pdf"; filename*=UTF-8\'\'requisicao-REQ-2027-000001.pdf',
          'X-Content-Type-Options': 'nosniff',
          'Cache-Control': 'private, no-store',
        },
        body: '%PDF-1.7 e2e',
      });
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
  await page.getByLabel('Usuário').fill('rita');
  await page.getByLabel('Senha').fill('secret');
  await page.getByRole('button', { name: 'Acessar' }).click();
  await expect(page.getByRole('heading', { name: 'Painel' })).toBeVisible();
}

test('requester asks for a requisition PDF, follows the job until ready and downloads it through the API', async ({
  page,
}) => {
  const state = await installFixture(page, REQUESTER_PERMISSIONS);
  await login(page);
  await page.getByRole('link', { name: 'Relatórios' }).click();
  await expect(page.getByRole('heading', { name: 'Relatórios', level: 1 })).toBeVisible();

  const typeSelect = page.getByLabel('Tipo de relatório');
  await expect(typeSelect.locator('option')).toHaveCount(1); // only the types the server says are available
  await page.getByLabel('Requisição (ID)').fill('r1');
  await page.getByRole('button', { name: 'Gerar relatório' }).click();

  const row = page.locator(`tr[data-job="${JOB_ID}"]`);
  await expect(row).toContainText('Na fila');
  expect(state.postedBodies[0]).toMatchObject({
    reportType: 'RequisitionPdf',
    requisitionId: 'r1',
    rfqId: null,
  });
  expect(state.idempotencyKeys.length).toBeGreaterThan(0);
  await expect(row).toContainText('Em processamento', { timeout: 10_000 });
  await expect(row).toContainText('Pronto', { timeout: 15_000 });
  await expect(page.getByRole('status')).toContainText('Requisição de compra: Pronto');
  expect(await axeViolations(page)).toEqual([]);

  const downloaded = page.waitForEvent('download');
  await row.getByRole('button', { name: 'Baixar' }).click();
  const file = await downloaded;
  expect(file.suggestedFilename()).toBe('requisicao-REQ-2027-000001.pdf');
  expect(state.downloads).toBe(1);
});

test('failed and expired jobs explain themselves, offer a retry and an expired download is reported, also on mobile', async ({
  page,
}) => {
  const state = await installFixture(page, REQUESTER_PERMISSIONS, [
    job({
      id: 'aaaaaaaa-2222-3333-4444-555555555555',
      status: 'Failed',
      attempts: 3,
      failureCode: 'report.attempts-exhausted',
      allowedActions: ['retry'],
    }),
    job({
      id: 'bbbbbbbb-2222-3333-4444-555555555555',
      status: 'Expired',
      attempts: 1,
      allowedActions: ['retry'],
    }),
    ready('cccccccc-2222-3333-4444-555555555555'),
  ]);
  state.expireOnDownload = true;
  state.polls = 10; // any poll answers Ready
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page);
  await page.goto('/reports');

  const failed = page.locator('tr[data-job="aaaaaaaa-2222-3333-4444-555555555555"]');
  await expect(failed).toContainText('A geração falhou após 3 tentativas.');
  await expect(page.locator('tr[data-job="bbbbbbbb-2222-3333-4444-555555555555"]')).toContainText(
    'O arquivo expirou; gere novamente.',
  );
  expect(await axeViolations(page)).toEqual([]);

  await failed.getByRole('button', { name: 'Gerar novamente' }).click();
  await expect.poll(() => state.postedBodies.length).toBe(1);
  expect(state.postedBodies[0]).toMatchObject({
    reportType: 'RequisitionPdf',
    requisitionId: 'r1',
  });

  await page
    .locator('tr[data-job="cccccccc-2222-3333-4444-555555555555"]')
    .getByRole('button', { name: 'Baixar' })
    .click();
  await expect(page.getByRole('alert')).toContainText('O recurso não está mais disponível');
  await expect(page.getByRole('alert')).toContainText('report.expired');
});

test('a user without reports.request sees no report center', async ({ page }) => {
  await installFixture(page, ['organization.read', 'catalog.read']);
  await login(page);
  await expect(page.getByRole('link', { name: 'Relatórios' })).toHaveCount(0);
  await page.goto('/reports');
  await expect(page).toHaveURL(/\/forbidden/);
});
