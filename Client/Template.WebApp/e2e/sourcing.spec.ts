import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type Route } from '@playwright/test';

/**
 * Phase 29 buyer journeys against a deterministic same-origin HTTP fixture of the v2 sourcing contract (ETag/If-Match,
 * Idempotency-Key, Problem Details, server comparison). The backend functional and PostgreSQL suites prove the real
 * rules; these journeys prove the browser experience: no ranking or conversion is computed in the browser.
 */
interface SourcingState {
  version: number;
  status: string;
  invitations: { supplierId: string; supplierLegalName: string }[];
  quotes: { supplierId: string; total: string }[];
  conflictOnFirstQuote: boolean;
  incomplete: boolean;
  awardAttempts: number;
  awardBodies: Record<string, unknown>[];
  ifMatchSeen: string[];
  idempotencyKeys: string[];
}

const buyer = {
  userId: 'user-buyer',
  userName: 'bruno',
  displayName: 'Bruno Comprador',
  permissions: [
    'organization.read',
    'catalog.read',
    'suppliers.read',
    'requisitions.read-all',
    'sourcing.rfqs.manage',
    'sourcing.rfqs.close-early',
    'sourcing.quotes.enter',
    'sourcing.quotes.read',
    'sourcing.awards.submit',
    'sourcing.waivers.low-value',
  ],
};

const suppliers = [
  {
    id: 's1',
    legalName: 'Alfa Comércio Ltda',
    tradeName: 'Alfa',
    taxId: '11222333000181',
    status: 'Active',
    isEligibleForNewCommitments: true,
    version: '1',
  },
  {
    id: 's2',
    legalName: 'Beta Distribuidora SA',
    tradeName: 'Beta',
    taxId: '22333444000172',
    status: 'Active',
    isEligibleForNewCommitments: true,
    version: '1',
  },
];

const money = (amount: string, currency = 'BRL') => ({ amount, currency });

function rfq(state: SourcingState) {
  const actions: Record<string, string[]> = {
    Draft: ['invite', 'remove-invitation', 'publish', 'cancel'],
    Published: [
      'invite',
      'extend-deadline',
      'remind',
      'enter-quote',
      'revise-quote',
      'withdraw-quote',
      'close-early',
      'cancel',
    ],
    Closed: state.incomplete ? ['cancel'] : ['award', 'cancel'],
    AwardPending: [],
  };
  const entries = state.quotes
    .map((quote, index) => ({
      quoteId: `q${index + 1}`,
      quoteRevisionId: `rev-${quote.supplierId}`,
      supplierId: quote.supplierId,
      supplierLegalName: suppliers.find((supplier) => supplier.id === quote.supplierId)!.legalName,
      revisionNumber: 1,
      rawTotal: money(quote.total, state.incomplete && index === 0 ? 'USD' : 'BRL'),
      normalizedTotal: state.incomplete && index === 0 ? undefined : money(quote.total),
      rate: undefined,
      deliveryDays: 7,
      receivedAt: '2027-03-01T13:00:00Z',
      validUntil: '2027-04-30',
      coveredLines: 1,
      totalLines: 1,
      supplierActiveAtClose: true,
      isValid: true,
      invalidReasons: [],
      rank: state.incomplete && index === 0 ? undefined : 0,
    }))
    // the fixture plays the server: it ranks; the browser only displays
    .sort((left, right) => Number(left.rawTotal.amount) - Number(right.rawTotal.amount));
  let next = 1;
  for (const entry of entries) if (entry.rank !== undefined) entry.rank = next++;
  const recommended = state.incomplete ? undefined : entries[0]?.quoteRevisionId;
  return {
    id: 'rfq1',
    number: 'RFQ-2027-000001',
    requisition: {
      id: 'r1',
      number: 'REQ-2027-000001',
      revision: 1,
      estimatedTotal: money('1200.00'),
      neededBy: '2027-04-30',
    },
    roundNumber: 1,
    method: 'Competitive',
    policy: { type: 'SourcingPolicy', version: 1 },
    baseCurrency: 'BRL',
    aboveThreshold: false,
    requiredInvitations: 1,
    deadline: state.status === 'Draft' ? null : '2027-03-04T13:00:00Z',
    status: state.status,
    closedEarly: state.status === 'Closed',
    closeReason: state.status === 'Closed' ? 'Todas as propostas recebidas antes do prazo' : null,
    lines: [
      {
        lineNumber: 1,
        description: 'Notebook 14',
        kind: 'Good',
        categoryCode: 'IT',
        unitCode: 'UN',
        quantity: '12',
        estimatedUnitPrice: money('100.00'),
      },
    ],
    invitations: state.invitations.map((invitation) => ({
      ...invitation,
      supplierTaxId: '11222333000181',
      supplierStatusAtInvitation: 'Active',
      invitedAt: '2027-03-01T13:00:00Z',
      isActive: true,
    })),
    commercialDetailsVisible: true,
    quotes: state.quotes.map((quote, index) => ({
      id: `q${index + 1}`,
      supplierId: quote.supplierId,
      supplierLegalName: suppliers.find((supplier) => supplier.id === quote.supplierId)!.legalName,
      latestRevisionNumber: 1,
      latestStatus: 'Current',
      revisions: [
        {
          id: `rev-${quote.supplierId}`,
          revisionNumber: 1,
          status: 'Current',
          currency: 'BRL',
          total: money(quote.total),
          subtotal: money(quote.total),
          freight: money('0.00'),
          discount: money('0.00'),
          taxesIncluded: money('0.00'),
          validUntil: '2027-04-30',
          deliveryDays: 7,
          paymentTerms: '30 dias',
          source: 'Email',
          receivedAt: '2027-03-01T13:00:00Z',
          enteredAt: '2027-03-01T13:00:00Z',
          lines: [],
        },
      ],
    })),
    comparison: ['Closed', 'AwardPending'].includes(state.status)
      ? {
          id: 'cmp1',
          sequence: 1,
          status: state.incomplete ? 'Incomplete' : 'Complete',
          baseCurrency: 'BRL',
          closeLocalDate: '2027-03-02',
          validQuoteCount: entries.length,
          requiredValidQuotes: 1,
          warnings: state.incomplete ? ['rate-unavailable:USD'] : [],
          missingRateCurrencies: state.incomplete ? ['USD'] : [],
          recommendedQuoteRevisionId: recommended,
          entries,
        }
      : undefined,
    comparisonCount: ['Closed', 'AwardPending'].includes(state.status) ? 1 : 0,
    awards:
      state.status === 'AwardPending'
        ? [
            {
              id: 'a1',
              status: 'PendingApproval',
              supplierId: entries[0]!.supplierId,
              supplierLegalName: entries[0]!.supplierLegalName,
              quoteRevisionId: recommended,
              rank: 1,
              awardedTotal: entries[0]!.rawTotal,
              normalizedTotal: entries[0]!.normalizedTotal,
              requisitionCurrencyTotal: entries[0]!.rawTotal,
              submittedAt: '2027-03-02T13:00:00Z',
            },
          ]
        : [],
    waivers: [],
    exchangeRates: [],
    allowedActions: actions[state.status] ?? [],
    createdAt: '2027-03-01T13:00:00Z',
    updatedAt: '2027-03-01T13:00:00Z',
    version: String(state.version),
  };
}

async function installFixture(page: Page, overrides: Partial<SourcingState> = {}) {
  const state: SourcingState = {
    version: 1,
    status: 'None',
    invitations: [],
    quotes: [],
    conflictOnFirstQuote: false,
    incomplete: false,
    awardAttempts: 0,
    awardBodies: [],
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
          status < 400 && path.startsWith('/api/v2/requests-for-quotation/rfq1')
            ? { ETag: `"${state.version}"` }
            : {},
        body: JSON.stringify(body),
      });
    const page1 = (items: unknown[]) => ({
      items,
      page: 1,
      pageSize: 50,
      totalItems: items.length,
      totalPages: 1,
    });
    const ifMatch = request.headers()['if-match'];
    if (ifMatch) state.ifMatchSeen.push(ifMatch);
    const key = request.headers()['idempotency-key'];
    if (key) state.idempotencyKeys.push(key);
    const change = (mutate: () => void) => {
      if (ifMatch !== `"${state.version}"`)
        return json({ code: 'concurrency.version-mismatch', version: String(state.version) }, 412);
      mutate();
      state.version += 1;
      return json(rfq(state));
    };

    if (path === '/api/v2/auth/sessions/refresh')
      return loggedIn
        ? json({ accessToken: 'token' })
        : json({ code: 'auth.session-expired' }, 401);
    if (path === '/api/v2/auth/sessions' && method === 'POST') {
      loggedIn = true;
      return json({ accessToken: 'token' });
    }
    if (path === '/api/v2/account/me') return json(buyer);
    if (path === '/api/v2/purchase-requisitions' && method === 'GET')
      return json(
        page1(
          state.status === 'None'
            ? [
                {
                  id: 'r1',
                  number: 'REQ-2027-000001',
                  costCenter: { id: 'c1', code: 'CC-OPS', name: 'Operações' },
                  neededBy: '2027-04-30',
                  estimatedTotal: money('1200.00'),
                  status: 'Submitted',
                  revision: 1,
                  createdAt: '2027-03-01T12:00:00Z',
                  version: '3',
                },
              ]
            : [],
        ),
      );
    if (path === '/api/v2/suppliers') return json(page1(suppliers));
    if (path === '/api/v2/requests-for-quotation' && method === 'GET')
      return json(
        page1(
          state.status === 'None'
            ? []
            : [
                {
                  id: 'rfq1',
                  number: 'RFQ-2027-000001',
                  requisitionId: 'r1',
                  requisitionNumber: 'REQ-2027-000001',
                  roundNumber: 1,
                  method: 'Competitive',
                  status: state.status,
                  invitationCount: state.invitations.length,
                  version: String(state.version),
                },
              ],
        ),
      );
    if (path === '/api/v2/requests-for-quotation' && method === 'POST') {
      state.status = 'Draft';
      return json(rfq(state), 201);
    }
    if (path === '/api/v2/requests-for-quotation/rfq1' && method === 'GET') return json(rfq(state));
    if (path === '/api/v2/requests-for-quotation/rfq1/history')
      return json({
        rfqId: 'rfq1',
        entries: [
          {
            sequence: 1,
            action: 'created',
            to: 'Draft',
            at: '2027-03-01T13:00:00Z',
            correlationId: 't',
          },
        ],
      });
    if (path === '/api/v2/requests-for-quotation/rfq1/invitations' && method === 'POST')
      return change(() => {
        const { supplierId } = request.postDataJSON() as { supplierId: string };
        state.invitations.push({
          supplierId,
          supplierLegalName: suppliers.find((supplier) => supplier.id === supplierId)!.legalName,
        });
      });
    if (path === '/api/v2/requests-for-quotation/rfq1/publish')
      return change(() => (state.status = 'Published'));
    if (path === '/api/v2/requests-for-quotation/rfq1/quotes') {
      if (state.conflictOnFirstQuote) {
        state.conflictOnFirstQuote = false;
        state.version += 1; // another buyer changed the RFQ meanwhile
        return json(
          {
            code: 'concurrency.version-mismatch',
            version: String(state.version),
            traceId: 'trace-412',
          },
          412,
        );
      }
      return change(() => {
        const body = request.postDataJSON() as {
          supplierId: string;
          lines: { quantityOffered: string; unitPrice: string }[];
          total?: unknown;
        };
        expect(body.total).toBeUndefined();
        state.quotes.push({
          supplierId: body.supplierId,
          total: (Number(body.lines[0]!.unitPrice) * 12).toFixed(2),
        });
      });
    }
    if (path === '/api/v2/requests-for-quotation/rfq1/close')
      return change(() => (state.status = 'Closed'));
    if (path === '/api/v2/requests-for-quotation/rfq1/award') {
      state.awardAttempts += 1;
      state.awardBodies.push(request.postDataJSON() as Record<string, unknown>);
      if (state.awardAttempts === 1)
        return json(
          { code: 'award.non-leading-requires-waiver', ruleId: 'SRC-018', traceId: 'trace-422' },
          422,
        );
      return change(() => (state.status = 'AwardPending'));
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
  await page.getByLabel('Usuário').fill('bruno');
  await page.getByLabel('Senha').fill('secret');
  await page.getByRole('button', { name: 'Acessar' }).click();
  await expect(page.getByRole('heading', { name: 'Painel' })).toBeVisible();
}

async function enterQuote(page: Page, supplier: string, unitPrice: string) {
  await page.getByLabel('Fornecedor', { exact: true }).last().selectOption({ label: supplier });
  await page.getByLabel('Preço unitário estimado #1').fill(unitPrice);
  await page.getByLabel('Válida até').fill('2027-04-30');
  await page.getByLabel('Prazo de entrega (dias)').fill('7');
  await page.getByLabel('Condições de pagamento').fill('30 dias');
  await page.getByRole('button', { name: 'Salvar' }).click();
}

test('buyer starts an RFQ from the queue, invites, publishes, records quotes after a 412, closes early and submits the recommendation', async ({
  page,
}) => {
  const state = await installFixture(page, { conflictOnFirstQuote: true });
  await login(page);
  await page
    .getByRole('navigation', { name: 'Principal' })
    .getByRole('link', { name: 'Cotações' })
    .click();
  await expect(page.getByRole('heading', { name: 'Requisições aguardando cotação' })).toBeVisible();
  expect(await axeViolations(page)).toEqual([]);
  await page.getByRole('button', { name: 'Iniciar cotação competitiva' }).click();
  await expect(page).toHaveURL(/\/sourcing\/rfq1$/);
  await expect(page.getByTestId('threshold')).toContainText('Até o limite');

  for (const supplier of suppliers) {
    await page
      .getByLabel('Fornecedor', { exact: true })
      .selectOption({ label: supplier.legalName });
    await page.getByRole('button', { name: 'Convidar' }).click();
    await expect(page.getByText('Fornecedor convidado.')).toBeVisible();
  }
  await page.getByLabel('Prazo', { exact: true }).fill('2027-03-04T10:00');
  await page.getByRole('button', { name: 'Publicar' }).click();
  await expect(page.getByText('Cotação publicada; convites enfileirados.')).toBeVisible();
  await expect(page.getByTestId('deadline')).toContainText('2027-03-04');

  await enterQuote(page, 'Beta Distribuidora SA', '95');
  await expect(page.getByText('concurrency.version-mismatch')).toBeVisible();
  await expect(page.getByText('trace-412')).toBeVisible();
  await page.getByRole('button', { name: 'Recarregar versão atual' }).click();
  await enterQuote(page, 'Beta Distribuidora SA', '95');
  await expect(page.getByText('Proposta registrada.')).toBeVisible();
  await enterQuote(page, 'Alfa Comércio Ltda', '90');
  await expect(page.getByText('Proposta registrada.')).toBeVisible();
  expect(await axeViolations(page)).toEqual([]);

  await page.getByRole('button', { name: 'Encerrar antes do prazo' }).click();
  await page
    .getByRole('dialog')
    .getByLabel('Motivo')
    .fill('Todas as propostas recebidas antes do prazo');
  await page.getByRole('dialog').getByRole('button', { name: 'Encerrar antes do prazo' }).click();
  const table = page.getByTestId('comparison-table');
  await expect(table).toBeVisible();
  const rows = table.locator('tbody tr');
  // SRC-015 / CALC-005: the UI shows the server ranking; rank 1 is the recommendation.
  await expect(rows.first()).toContainText('Alfa Comércio Ltda');
  await expect(rows.first()).toContainText('Recomendada');
  await expect(rows.nth(1)).toContainText('Beta Distribuidora SA');

  // Selecting the second-ranked quote: the server asks for the NonLeadingSelection waiver reason.
  await rows.nth(1).getByRole('button', { name: 'Selecionar' }).click();
  await page.getByLabel('Justificativa da seleção').fill('Melhor prazo de garantia');
  await page.getByRole('button', { name: 'Enviar para aprovação' }).click();
  await expect(page.getByText('award.non-leading-requires-waiver')).toBeVisible();
  await page
    .getByLabel('Justificativa: proposta não líder')
    .fill('Fornecedor homologado para esta linha de produtos');
  await page.getByRole('button', { name: 'Enviar para aprovação' }).click();
  await expect(page.getByText('Seleção enviada para aprovação.')).toBeVisible();
  await expect(page.getByTestId('award')).toContainText('Em aprovação');
  expect(await axeViolations(page)).toEqual([]);

  expect(state.awardBodies[0]).toMatchObject({ quoteRevisionId: 'rev-s2', nonLeadingReason: null });
  expect(state.awardBodies[1]).toMatchObject({
    nonLeadingReason: 'Fornecedor homologado para esta linha de produtos',
  });
  expect(state.ifMatchSeen.length).toBeGreaterThanOrEqual(6);
  expect(new Set(state.idempotencyKeys).size).toBe(state.idempotencyKeys.length);
});

test('an incomplete comparison explains the missing rate, offers no award and collapses to cards on mobile', async ({
  page,
}) => {
  await installFixture(page, {
    status: 'Closed',
    incomplete: true,
    version: 7,
    invitations: [
      { supplierId: 's1', supplierLegalName: 'Alfa Comércio Ltda' },
      { supplierId: 's2', supplierLegalName: 'Beta Distribuidora SA' },
    ],
    quotes: [
      { supplierId: 's1', total: '100.00' },
      { supplierId: 's2', total: '1300.00' },
    ],
  });
  await login(page);
  await page.goto('/sourcing/rfq1');
  await expect(page.getByTestId('comparison-warning')).toContainText('Taxa de câmbio indisponível');
  await expect(page.getByTestId('comparison-warning')).toContainText('USD');
  await expect(page.getByRole('button', { name: 'Selecionar' })).toHaveCount(0);

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/sourcing/rfq1');
  await expect(page.getByTestId('comparison-table').locator('thead')).toHaveCSS(
    'position',
    'absolute',
  );
  expect(await axeViolations(page)).toEqual([]);
});
