import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type Route } from '@playwright/test';

/**
 * Phase 32 purchase orders and receiving against a deterministic same-origin fixture of the v2 purchase-orders and
 * receipts contract (allowedActions, If-Match/ETag, Idempotency-Key, Problem Details, server-computed remaining
 * quantities). The backend unit, functional, PostgreSQL and RabbitMQ suites prove the rules, numbering and exactly-once
 * effects; these journeys prove the browser experience from the approved requisition to the completed order,
 * stale-action handling, permissions and accessibility.
 */
const ORDER_ID = '44444444-2222-3333-4444-555555555555';
const REQUISITION_ID = 'req-1';

interface OrderState {
  version: number;
  status: 'Draft' | 'Issued' | 'PartiallyReceived' | 'Completed' | 'Cancelled';
  number?: string;
  deliveryAddress?: string;
  accepted: number;
  received: number;
  rejected: number;
  document: 'NotRequested' | 'Pending' | 'Ready' | 'Failed';
  notification: 'NotRequested' | 'Pending' | 'Sent' | 'Failed';
  receipts: Record<string, unknown>[];
  issues: { ifMatch?: string; key?: string }[];
  receiptBodies: Record<string, unknown>[];
  corrections: Record<string, unknown>[];
  staleIssue: boolean;
}

const buyerPermissions = [
  'organization.read',
  'requisitions.read-all',
  'purchasing.orders.read',
  'purchasing.orders.manage',
  'purchasing.orders.issue',
  'purchasing.orders.cancel',
];
const receiverPermissions = [
  'organization.read',
  'receiving.receipts.create',
  'receiving.receipts.read',
  'receiving.receipts.correct',
];

const money = (amount: string) => ({ amount, currency: 'BRL' });

function actions(state: OrderState, permissions: string[]) {
  const can = (permission: string) => permissions.includes(permission);
  const result: string[] = [];
  if (state.status === 'Draft') {
    if (can('purchasing.orders.manage')) result.push('update');
    if (can('purchasing.orders.issue')) result.push('issue');
    if (can('purchasing.orders.cancel')) result.push('cancel');
  }
  if (
    (state.status === 'Issued' || state.status === 'PartiallyReceived') &&
    can('receiving.receipts.create')
  )
    result.push('receive');
  if (state.document === 'Ready' && can('purchasing.orders.read')) result.push('download-document');
  return result;
}

function order(state: OrderState, permissions: string[]) {
  const prices = permissions.includes('purchasing.orders.read');
  const remaining = state.status === 'Draft' ? 18 : Math.max(0, 18 - state.accepted);
  return {
    id: ORDER_ID,
    number: state.number ?? null,
    status: state.status,
    version: String(state.version),
    requisitionId: REQUISITION_ID,
    requisitionNumber: 'REQ-2027-000001',
    rfqId: 'rfq-1',
    rfqNumber: 'RFQ-2027-000001',
    supplier: {
      supplierId: 's1',
      legalName: 'Fornecedor Alfa Ltda',
      country: 'BR',
      taxId: '11222333000181',
      address: prices ? 'Avenida Paulista, 1000, São Paulo - SP' : null,
      contact: prices ? { name: 'Ana Contato', email: 'contato@example.invalid' } : null,
    },
    buyerName: 'Bruno Comprador',
    currency: 'BRL',
    subtotal: prices ? money('16200.00') : null,
    freight: prices ? money('0.00') : null,
    discount: prices ? money('0.00') : null,
    taxesIncluded: prices ? money('0.00') : null,
    total: prices ? money('16200.00') : null,
    paymentTerms: '30 dias',
    deliveryAddress: state.deliveryAddress ?? null,
    lines: [
      {
        lineNumber: 1,
        requisitionLineNumber: 1,
        description: 'Notebook 14',
        kind: 'Good',
        unitCode: 'UN',
        unitAllowedDecimals: 0,
        quantity: '18',
        unitPrice: prices ? money('900.00') : null,
        lineTotal: prices ? money('16200.00') : null,
        promisedDeliveryDate: '2027-03-15',
        receivedQuantity: String(state.received),
        acceptedQuantity: String(state.accepted),
        rejectedQuantity: String(state.rejected),
        remainingQuantity: String(remaining),
        receiptCap: '18',
        isShortClosed: false,
      },
    ],
    issueDate: state.number ? '2027-03-08' : null,
    shortClosed: false,
    document: {
      status: state.document,
      attempts: state.document === 'NotRequested' ? 0 : 1,
      templateVersion: state.document === 'Ready' ? 'purchase-order-pdf.v1' : null,
    },
    notification: {
      status: state.notification,
      attempts: state.notification === 'NotRequested' ? 0 : 1,
    },
    receivingPolicy: 'ReceivingPolicy:1',
    pricesVisible: prices,
    allowedActions: actions(state, permissions),
  };
}

function receipt(
  id: string,
  number: string,
  received: number,
  accepted: number,
  rejected: number,
  extra: Record<string, unknown> = {},
) {
  return {
    id,
    number,
    version: '1',
    purchaseOrderId: ORDER_ID,
    purchaseOrderNumber: 'PO-2027-000001',
    receivedOn: '2027-03-09',
    location: 'Doca 2',
    policy: 'ReceivingPolicy:1',
    hasDiscrepancy: rejected > 0,
    postedAt: '2027-03-09T13:00:00Z',
    lines: [
      {
        poLineNumber: 1,
        description: 'Notebook 14',
        unitCode: 'UN',
        receivedQuantity: String(received),
        acceptedQuantity: String(accepted),
        rejectedQuantity: String(rejected),
        correctedAcceptedQuantity: String(accepted),
        discrepancyType: rejected > 0 ? 'Damaged' : null,
        discrepancyNotes: rejected > 0 ? 'Duas caixas amassadas' : null,
      },
    ],
    corrections: [],
    allowedActions: ['correct'],
    ...extra,
  };
}

async function installFixture(
  page: Page,
  permissions: string[],
  overrides: Partial<OrderState> = {},
) {
  const state: OrderState = {
    version: 1,
    status: 'Draft',
    accepted: 0,
    received: 0,
    rejected: 0,
    document: 'NotRequested',
    notification: 'NotRequested',
    receipts: [],
    issues: [],
    receiptBodies: [],
    corrections: [],
    staleIssue: false,
    ...overrides,
  };
  let loggedIn = false;
  await page.route('**/api/v2/**', async (route: Route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();
    const headers = request.headers();
    const json = (body: unknown, status = 200) =>
      route.fulfill({
        status,
        contentType: status >= 400 ? 'application/problem+json' : 'application/json',
        headers:
          status < 400 && body && typeof body === 'object' && 'version' in body
            ? { ETag: `"${(body as { version: string }).version}"` }
            : {},
        body: JSON.stringify(body),
      });
    const current = () => order(state, permissions);

    if (path === '/api/v2/auth/sessions/refresh')
      return loggedIn
        ? json({ accessToken: 'token' })
        : json({ code: 'auth.session-expired' }, 401);
    if (path === '/api/v2/auth/sessions' && method === 'POST') {
      loggedIn = true;
      return json({ accessToken: 'token' });
    }
    if (path === '/api/v2/account/me')
      return json({
        userId: 'user-1',
        userName: 'bruno',
        displayName: 'Bruno Comprador',
        permissions,
      });
    if (path === `/api/v2/purchase-requisitions/${REQUISITION_ID}`)
      return json({
        id: REQUISITION_ID,
        number: 'REQ-2027-000001',
        requesterId: 'user-r',
        costCenter: { id: 'c1', code: 'CC-OPS', name: 'Operações', departmentId: 'd1' },
        purpose: 'Notebooks para a equipe de suporte',
        neededBy: '2027-04-30',
        currency: 'BRL',
        estimatedTotal: money('18000.00'),
        status:
          state.status === 'Draft'
            ? 'Approved'
            : state.status === 'Completed'
              ? 'Closed'
              : 'Ordered',
        revision: 1,
        createdAt: '2027-03-01T13:00:00Z',
        lines: [],
        attachments: [],
        submissions: [],
        allowedActions: [],
        version: '9',
        award: { awardId: 'a1', supplierId: 's1', supplierName: 'Fornecedor Alfa Ltda' },
        purchaseOrderId: ORDER_ID,
      });
    if (path === `/api/v2/purchase-requisitions/${REQUISITION_ID}/history`)
      return json({ id: REQUISITION_ID, entries: [] });
    if (path === `/api/v2/purchase-requisitions/${REQUISITION_ID}/budget-availability`)
      return json({
        costCenterId: 'c1',
        currency: 'BRL',
        asOfDate: '2027-03-01',
        hasOpenAllocation: true,
        available: money('0.00'),
        reserved: money('0.00'),
      });
    if (path === '/api/v2/purchase-orders' && method === 'GET') {
      const status = new URL(request.url()).searchParams.get('status') ?? '';
      const visible = status === '' || status.split(',').includes(state.status);
      const summary = current();
      return json({
        items: visible
          ? [
              {
                id: ORDER_ID,
                number: summary.number,
                status: state.status,
                requisitionId: REQUISITION_ID,
                requisitionNumber: 'REQ-2027-000001',
                supplierId: 's1',
                supplierLegalName: 'Fornecedor Alfa Ltda',
                total: summary.total,
                lineCount: 1,
                documentStatus: state.document,
                notificationStatus: state.notification,
                earliestPromisedDate: '2027-03-15',
                createdAt: '2027-03-08T12:00:00Z',
                updatedAt: '2027-03-08T12:00:00Z',
              },
            ]
          : [],
        page: 1,
        pageSize: 50,
        totalItems: visible ? 1 : 0,
        totalPages: 1,
      });
    }
    if (path === `/api/v2/purchase-orders/${ORDER_ID}` && method === 'GET') {
      if (state.status === 'Draft' && !permissions.includes('purchasing.orders.read'))
        return json({ code: 'not-found', status: 404 }, 404);
      return json(current());
    }
    if (path === `/api/v2/purchase-orders/${ORDER_ID}` && method === 'PUT') {
      if (headers['if-match'] !== `"${state.version}"`)
        return json({ code: 'concurrency.version-mismatch' }, 412);
      const body = request.postDataJSON() as { deliveryAddress?: string };
      state.deliveryAddress = body.deliveryAddress ?? state.deliveryAddress;
      state.version += 1;
      return json(current());
    }
    if (path === `/api/v2/purchase-orders/${ORDER_ID}/issue`) {
      state.issues.push({ ifMatch: headers['if-match'], key: headers['idempotency-key'] });
      if (state.staleIssue) {
        state.staleIssue = false;
        state.version += 1;
        state.deliveryAddress = 'Endereço alterado por outra sessão';
        return json({ code: 'concurrency.version-mismatch', version: String(state.version) }, 412);
      }
      if (!state.deliveryAddress)
        return json({ code: 'purchase-order.not-issuable', ruleId: 'PUR-003', errors: {} }, 422);
      state.status = 'Issued';
      state.number = 'PO-2027-000001';
      state.document = 'Ready';
      state.notification = 'Sent';
      state.version += 1;
      return json(current());
    }
    if (path === `/api/v2/purchase-orders/${ORDER_ID}/history`)
      return json({
        purchaseOrderId: ORDER_ID,
        entries: [
          { sequence: 1, action: 'created', to: 'Draft', at: '2027-03-08T12:00:00Z' },
          ...(state.number
            ? [
                {
                  sequence: 2,
                  action: 'issued',
                  from: 'Draft',
                  to: 'Issued',
                  at: '2027-03-08T13:00:00Z',
                },
                {
                  sequence: 3,
                  action: 'document-ready',
                  from: 'Issued',
                  to: 'Issued',
                  at: '2027-03-08T13:01:00Z',
                },
                {
                  sequence: 4,
                  action: 'notification-sent',
                  from: 'Issued',
                  to: 'Issued',
                  at: '2027-03-08T13:02:00Z',
                },
              ]
            : []),
          ...state.receipts.map((_, index) => ({
            sequence: 5 + index,
            action: 'receipt-posted',
            to: state.status,
            at: '2027-03-09T13:00:00Z',
          })),
        ],
      });
    if (path === `/api/v2/purchase-orders/${ORDER_ID}/document`)
      return route.fulfill({
        status: 200,
        contentType: 'application/pdf',
        body: '%PDF-1.7 e2e',
        headers: { 'Content-Disposition': 'attachment; filename="pedido.pdf"' },
      });
    if (path === '/api/v2/receipts' && method === 'GET')
      return json({
        items: state.receipts,
        page: 1,
        pageSize: 100,
        totalItems: state.receipts.length,
        totalPages: 1,
      });
    if (path === '/api/v2/receipts' && method === 'POST') {
      const body = request.postDataJSON() as {
        lines: { received: string; accepted: string; rejected: string }[];
      };
      state.receiptBodies.push({ ...body, key: headers['idempotency-key'] });
      const line = body.lines[0]!;
      const accepted = Number(line.accepted);
      if (state.accepted + accepted > 18)
        return json(
          {
            code: 'receipt.over-receipt',
            ruleId: 'RCV-003',
            detail: 'The accepted quantity would exceed the receipt cap of the line.',
          },
          422,
        );
      state.accepted += accepted;
      state.received += Number(line.received);
      state.rejected += Number(line.rejected);
      state.status =
        state.accepted >= 18 ? 'Completed' : state.accepted > 0 ? 'PartiallyReceived' : 'Issued';
      state.version += 1;
      const id = `rcv-${state.receipts.length + 1}`;
      const posted = receipt(
        id,
        `RCV-2027-00000${state.receipts.length + 1}`,
        Number(line.received),
        accepted,
        Number(line.rejected),
      );
      state.receipts.push(posted);
      return json({ receipt: posted, purchaseOrder: current() }, 201);
    }
    const receiptMatch = /^\/api\/v2\/receipts\/(rcv-\d+)(\/corrections)?$/.exec(path);
    if (receiptMatch) {
      const found = state.receipts.find((item) => item['id'] === receiptMatch[1])!;
      if (!receiptMatch[2]) return json(found);
      const body = request.postDataJSON() as { acceptedDelta: string; reason: string };
      state.corrections.push({ ...body, ifMatch: headers['if-match'] });
      state.accepted += Number(body.acceptedDelta);
      state.status = state.accepted >= 18 ? 'Completed' : 'PartiallyReceived';
      state.version += 1;
      const corrected = {
        ...found,
        version: '2',
        corrections: [
          {
            id: 'c1',
            sequence: 1,
            poLineNumber: 1,
            acceptedDelta: body.acceptedDelta,
            reason: body.reason,
            actorId: 'user-1',
            at: '2027-03-10T13:00:00Z',
          },
        ],
      };
      Object.assign(found, corrected);
      return json({ receipt: corrected, purchaseOrder: current() });
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

async function login(page: Page, user = 'bruno') {
  await page.goto('/');
  await page.getByLabel('Usuário').fill(user);
  await page.getByLabel('Senha').fill('secret');
  await page.getByRole('button', { name: 'Acessar' }).click();
  await expect(page.getByRole('heading', { name: 'Painel' })).toBeVisible();
}

test('buyer goes from the approved requisition to an issued order with its document', async ({
  page,
}) => {
  const state = await installFixture(page, buyerPermissions);
  await login(page);
  await page.goto(`/requisitions/${REQUISITION_ID}`);
  await page.getByTestId('purchase-order-link').getByRole('link').click();
  await expect(page.getByRole('heading', { name: 'Pedido Rascunho' })).toBeVisible();
  await expect(page.getByText('Fornecedor Alfa Ltda').first()).toBeVisible();
  expect(await axeViolations(page)).toEqual([]);

  await page.getByRole('button', { name: 'Emitir pedido' }).click();
  await page.getByRole('button', { name: 'Confirmar emissão' }).click();
  await expect(page.getByRole('alert')).toContainText('O pedido ainda não pode ser emitido');

  await page.getByLabel('Endereço de entrega').fill('Rua Sintética 100, São Paulo - SP');
  await page.getByRole('button', { name: 'Salvar rascunho' }).click();
  await expect(page.getByRole('status')).toContainText('Rascunho salvo.');
  await page.getByRole('button', { name: 'Emitir pedido' }).click();
  await page.getByRole('button', { name: 'Confirmar emissão' }).click();
  await expect(page.getByRole('heading', { name: 'Pedido PO-2027-000001' })).toBeVisible();
  await expect(page.getByRole('status')).toContainText('Pedido PO-2027-000001 emitido.');
  expect(state.issues.at(-1)).toMatchObject({ ifMatch: '"2"' });
  expect(state.issues.at(-1)!.key).toBeTruthy();
  await expect(page.locator('[data-document="Ready"]')).toContainText('purchase-order-pdf.v1');
  await expect(page.locator('[data-notification="Sent"]')).toBeVisible();

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Baixar PDF' }).click();
  expect((await download).suggestedFilename()).toBe('pedido-PO-2027-000001.pdf');
  expect(await axeViolations(page)).toEqual([]);
});

test('receiver records a partial receipt with a discrepancy, sees the remaining balance, corrects and completes', async ({
  page,
}) => {
  const state = await installFixture(page, receiverPermissions, {
    status: 'Issued',
    number: 'PO-2027-000001',
    deliveryAddress: 'Rua Sintética 100',
    document: 'Ready',
    notification: 'Sent',
    version: 3,
  });
  await login(page, 'rita');
  await page.getByRole('link', { name: 'Recebimento' }).click();
  await expect(page.getByRole('heading', { name: 'Fila de recebimento', level: 1 })).toBeVisible();
  const row = page.locator(`tr[data-order="${ORDER_ID}"]`);
  await expect(row).toContainText('PO-2027-000001');
  expect(await axeViolations(page)).toEqual([]);

  await row.getByRole('link', { name: 'Receber' }).click();
  await expect(
    page.getByRole('heading', { name: 'Novo recebimento do pedido PO-2027-000001' }),
  ).toBeVisible();
  await expect(page.locator('tr[data-line="1"]')).toContainText('18 / 18');
  await page.getByLabel('Quantidade recebida da linha 1').fill('20');
  await expect(page.getByText('O aceito ultrapassa o saldo de 18.')).toBeVisible();
  await page.getByRole('button', { name: 'Registrar recebimento' }).click();
  expect(state.receiptBodies).toHaveLength(0);

  await page.getByLabel('Quantidade recebida da linha 1').fill('12');
  await page.getByLabel('Quantidade aceita da linha 1').fill('10');
  await page.getByLabel('Quantidade rejeitada da linha 1').fill('2');
  await expect(page.getByText('Selecione o tipo de divergência.')).toBeVisible();
  await page.getByLabel('Tipo de divergência da linha 1').selectOption('Damaged');
  await page.getByLabel('Observação da divergência da linha 1').fill('Duas caixas amassadas');
  expect(await axeViolations(page)).toEqual([]);
  await page.getByRole('button', { name: 'Registrar recebimento' }).click();

  await expect(page.getByRole('heading', { name: 'Recebimento RCV-2027-000001' })).toBeVisible();
  await expect(page.locator('tr[data-line="1"]')).toContainText('Avaria');
  await expect(page.locator('[data-status="PartiallyReceived"]')).toBeVisible();
  expect(state.receiptBodies[0]).toMatchObject({
    lines: [
      {
        poLineNumber: 1,
        received: '12',
        accepted: '10',
        rejected: '2',
        discrepancyType: 'Damaged',
      },
    ],
  });
  expect(state.receiptBodies[0]!['key']).toBeTruthy();

  await page.getByLabel('Ajuste da quantidade aceita (ex.: -1 ou 2)').fill('2');
  await page.getByLabel('Motivo').fill('Caixas vistoriadas e aceitas');
  await page.getByRole('button', { name: 'Registrar correção' }).last().click();
  await expect(page.getByRole('status')).toContainText(
    'Correção registrada. Situação do pedido: Recebido parcialmente.',
  );
  await expect(page.locator('li[data-correction="1"]')).toContainText('Linha 1: 2');
  expect(state.corrections[0]).toMatchObject({ acceptedDelta: '2', ifMatch: '"1"' });

  await page.getByRole('link', { name: 'PO-2027-000001' }).click();
  await expect(page.locator('tr[data-line="1"]')).toContainText('6');
  await page.getByRole('link', { name: 'Receber' }).click();
  await page.getByLabel('Quantidade recebida da linha 1').fill('6');
  await page.getByRole('button', { name: 'Registrar recebimento' }).click();
  await expect(page.locator('[data-status="Completed"]')).toBeVisible();
  expect(await axeViolations(page)).toEqual([]);
});

test('a stale issue reloads the order instead of retrying, also on mobile', async ({ page }) => {
  const state = await installFixture(page, buyerPermissions, {
    deliveryAddress: 'Rua 1, Centro',
    staleIssue: true,
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page);
  await page.goto('/purchasing/orders');
  await expect(page.getByRole('heading', { name: 'Pedidos de compra', level: 1 })).toBeVisible();
  await page.locator(`tr[data-order="${ORDER_ID}"]`).getByRole('link', { name: 'Abrir' }).click();
  await page.getByRole('button', { name: 'Emitir pedido' }).click();
  await page.getByRole('button', { name: 'Confirmar emissão' }).click();
  await expect(page.getByText('Este pedido mudou desde que você o abriu.')).toBeVisible();
  await expect(page.getByLabel('Endereço de entrega')).toHaveValue(
    'Endereço alterado por outra sessão',
  );
  expect(state.issues).toHaveLength(1);
  expect(state.status).toBe('Draft');
  expect(await axeViolations(page)).toEqual([]);
});

test('without purchasing permissions there is no order menu and the routes are forbidden', async ({
  page,
}) => {
  await installFixture(page, ['organization.read']);
  await login(page);
  await expect(page.getByRole('link', { name: 'Pedidos de compra' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Recebimento' })).toHaveCount(0);
  await page.goto('/purchasing/orders');
  await expect(page).toHaveURL(/\/forbidden/);
  await page.goto('/receiving/queue');
  await expect(page).toHaveURL(/\/forbidden/);
});
