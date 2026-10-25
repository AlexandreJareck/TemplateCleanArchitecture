import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Browser, type Locator, type Page } from '@playwright/test';

/*
 * SC-01 on the real stack: requester -> budget reservation -> competitive RFQ with three quotes -> two-level durable
 * approval -> purchase order with its PDF -> full receipt -> requisition PDF report -> history. It creates its own
 * requisition, so the seeded demo drafts stay untouched.
 */
const password = process.env['E2E_DEMO_PASSWORD'] ?? '';
const RUN = Date.now().toString(36);
/** Content-Security-Policy violations reported by any page of the journey (NFR-SEC-5). */
const cspViolations: string[] = [];

async function signIn(browser: Browser, userName: string): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  page.on('console', (message) => {
    if (/Content Security Policy/i.test(message.text())) cspViolations.push(message.text());
  });
  await page.goto('/login');
  await page.getByLabel('Usuário').fill(userName);
  await page.getByLabel('Senha').fill(password);
  await page.getByRole('button', { name: 'Acessar' }).click();
  await expect(page.getByRole('heading', { name: 'Painel' })).toBeVisible();
  return page;
}

async function nav(page: Page, name: string) {
  await page
    .getByRole('navigation', { name: 'Principal' })
    .getByRole('link', { name, exact: true })
    .click();
}

async function axe(page: Page) {
  expect(cspViolations, 'no Content-Security-Policy violation').toEqual([]);
  const result = await new AxeBuilder({ page }).analyze();
  return result.violations.map(
    (v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`,
  );
}

/**
 * Polls until the locator exists while Outbox -> RabbitMQ -> Worker/Functions work completes. `refresh` re-navigates
 * inside the app when possible; a full reload costs a session refresh, which is rate limited (10/min), so reloads
 * are spaced 8 s apart.
 */
async function waitFor(
  page: Page,
  locator: () => Locator,
  refresh?: () => Promise<void>,
  timeout = 240_000,
) {
  await page.waitForLoadState('networkidle');
  await expect
    .poll(
      async () => {
        if ((await locator().count()) > 0) return true;
        if (refresh) await refresh();
        else await page.reload();
        await page.waitForLoadState('networkidle');
        return (await locator().count()) > 0;
      },
      { timeout, intervals: [8_000] },
    )
    .toBe(true);
}

/** Leaves and re-enters a list page through the app shell: fresh data without a reload. */
function reenter(page: Page, name: string) {
  return async () => {
    await nav(page, 'Painel');
    await nav(page, name);
  };
}

function isoDate(daysFromNow: number) {
  return new Date(Date.now() + daysFromNow * 86_400_000).toISOString().slice(0, 10);
}

async function realOptions(select: Locator) {
  return select
    .locator('option')
    .evaluateAll((options) =>
      options.map((o) => (o as HTMLOptionElement).value).filter((v) => v !== ''),
    );
}

/** Options arrive from the API after the form renders, so wait for at least one real choice. */
async function firstRealOption(select: Locator) {
  await expect.poll(async () => (await realOptions(select)).length).toBeGreaterThan(0);
  await select.selectOption((await realOptions(select))[0]!);
}

test('SC-01 requisition to receipt, report and history on the real stack', async ({ browser }) => {
  expect(password, 'E2E_DEMO_PASSWORD must be provided by scripts/real-journey.ps1').not.toBe('');
  let requisitionPath = '';
  let requisitionId = '';
  let requisitionNumber = '';
  let orderPath = '';

  await test.step('requester creates and submits a requisition (budget reserved)', async () => {
    const page = await signIn(browser, 'demo.requester');
    await nav(page, 'Minhas requisições');
    await page.getByRole('link', { name: 'Nova requisição' }).click();
    // TEC-INFRA is managed by demo.manager (level 1); its department head is demo.department-head (level 2).
    const costCenter = page.getByLabel('Centro de custo');
    await expect(costCenter.locator('option', { hasText: 'TEC-INFRA' })).toHaveCount(1);
    await costCenter.selectOption({
      label: await costCenter.locator('option', { hasText: 'TEC-INFRA' }).innerText(),
    });
    await page.getByLabel('Necessário até').fill(isoDate(45));
    await page.getByLabel('Finalidade').fill(`Jornada de release ${RUN}: monitores para o time`);
    await page.getByRole('button', { name: 'Salvar' }).first().click();
    await expect(page).toHaveURL(/\/requisitions\/[0-9a-f-]+\/edit$/);
    await page.getByLabel('Descrição').fill(`Monitor 27 polegadas ${RUN}`);
    await firstRealOption(page.getByLabel('Categorias'));
    await firstRealOption(page.getByLabel('Unidades de medida'));
    await page.getByLabel('Quantidade').fill('8');
    await page.getByLabel('Preço unitário estimado (BRL)').fill('1500');
    await page.getByRole('button', { name: 'Salvar' }).nth(1).click();
    await expect(page.getByText('Total calculado pelo servidor')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Submeter' })).toBeEnabled();
    await page.mouse.move(0, 0);
    expect(await axe(page)).toEqual([]);

    await page.getByRole('button', { name: 'Submeter' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Submeter' }).click();
    await expect(page).toHaveURL(/\/requisitions\/[0-9a-f-]+$/);
    await expect(page.getByText('Submetida').first()).toBeVisible();
    await expect(page.getByText('Reserva orçamentária').first()).toBeVisible();
    requisitionPath = new URL(page.url()).pathname;
    requisitionId = requisitionPath.split('/').pop()!;
    requisitionNumber = (await page.locator('main').innerText()).match(/REQ-\d{4}-\d{6}/)![0];
    await page.context().close();
  });

  await test.step('buyer runs a competitive RFQ with three quotes and submits the award', async () => {
    const page = await signIn(browser, 'demo.buyer');
    page.on('response', async (response) => {
      // Release evidence: any rejected quote is reported with its Problem Details code and fields (no secrets).
      if (response.url().includes('/quotes') && response.status() >= 400)
        console.log('quote rejected', response.status(), await response.text());
    });
    await nav(page, 'Cotações');
    const queueRow = page.locator('tr', { hasText: requisitionNumber });
    await waitFor(page, () => queueRow, reenter(page, 'Cotações'));
    await queueRow.getByRole('button', { name: 'Iniciar cotação competitiva' }).click();
    await expect(page).toHaveURL(/\/sourcing\/[0-9a-f-]+$/);

    const supplierSelect = page.getByLabel('Fornecedor', { exact: true }).first();
    await expect
      .poll(async () => (await realOptions(supplierSelect)).length)
      .toBeGreaterThanOrEqual(3);
    const suppliers = await supplierSelect
      .locator('option')
      .evaluateAll((options) =>
        options
          .filter((o) => (o as HTMLOptionElement).value !== '')
          .map((o) => o.textContent!.trim()),
      );
    expect(suppliers.length).toBeGreaterThanOrEqual(3);
    for (const supplier of suppliers.slice(0, 3)) {
      await supplierSelect.selectOption({ label: supplier });
      await page.getByRole('button', { name: 'Convidar' }).click();
      // The list re-renders without the invited supplier (and resets the selection) before the next choice.
      await expect(supplierSelect.locator('option', { hasText: supplier })).toHaveCount(0);
    }
    await page.getByLabel('Prazo', { exact: true }).fill(`${isoDate(3)}T10:00`);
    await page.getByRole('button', { name: 'Publicar' }).click();
    await expect(page.getByText('Cotação publicada; convites enfileirados.')).toBeVisible();

    for (const [index, supplier] of suppliers.slice(0, 3).entries()) {
      await page.getByLabel('Fornecedor', { exact: true }).last().selectOption({ label: supplier });
      await page.getByLabel('Preço unitário estimado #1').fill(String(1400 + index * 50));
      await page.getByLabel('Válida até').fill(isoDate(30));
      await page.getByLabel('Prazo de entrega (dias)').fill('10');
      await page.getByLabel('Condições de pagamento').fill('30 dias');
      const saved = page.waitForResponse(
        (response) => response.url().endsWith('/quotes') && response.request().method() === 'POST',
      );
      await page.getByRole('button', { name: 'Salvar' }).click();
      expect((await saved).status()).toBe(200);
      // The page reloads the RFQ (new version) after each quote; the next save must use it.
      await page.waitForLoadState('networkidle');
      await expect(page.getByText('Proposta registrada.')).toBeVisible();
    }
    expect(await axe(page)).toEqual([]);

    await page.getByRole('button', { name: 'Encerrar antes do prazo' }).click();
    await page
      .getByRole('dialog')
      .getByLabel('Motivo')
      .fill('Todas as propostas recebidas antes do prazo');
    await page.getByRole('dialog').getByRole('button', { name: 'Encerrar antes do prazo' }).click();
    const rows = page.getByTestId('comparison-table').locator('tbody tr');
    await expect(rows.first()).toContainText('Recomendada');
    await rows.first().getByRole('button', { name: 'Selecionar' }).click();
    await page.getByLabel('Justificativa da seleção').fill('Menor preço com prazo adequado');
    await page.getByRole('button', { name: 'Enviar para aprovação' }).click();
    await expect(page.getByText('Seleção enviada para aprovação.')).toBeVisible();
    await expect(page.getByTestId('award')).toContainText('Em aprovação');
    await page.context().close();
  });

  for (const [approver, level] of [
    ['demo.manager', 1],
    ['demo.department-head', 2],
  ] as const) {
    await test.step(`${approver} approves level ${level} (durable workflow)`, async () => {
      const page = await signIn(browser, approver);
      await nav(page, 'Aprovações');
      const row = page.locator('tr[data-approval]', { hasText: requisitionNumber });
      await waitFor(page, () => row, reenter(page, 'Aprovações'));
      await row.getByRole('link', { name: 'Abrir' }).click();
      await expect(
        page.getByRole('heading', { name: `Aprovação ${requisitionNumber}` }),
      ).toBeVisible();
      await page.getByRole('button', { name: 'Aprovar' }).click();
      await page.getByRole('button', { name: 'Confirmar aprovação' }).click();
      await expect(page.getByRole('status')).toContainText('Aprovação atualizada');
      expect(await axe(page)).toEqual([]);
      await page.context().close();
    });
  }

  await test.step('buyer issues the purchase order and its PDF is generated', async () => {
    const page = await signIn(browser, 'demo.buyer');
    await page.goto(requisitionPath);
    await waitFor(page, () => page.getByTestId('purchase-order-link').getByRole('link'));
    await page.getByTestId('purchase-order-link').getByRole('link').click();
    await expect(page.getByRole('heading', { name: 'Pedido Rascunho' })).toBeVisible();
    orderPath = new URL(page.url()).pathname;
    await page.getByLabel('Endereço de entrega').fill('Rua Sintética 100, São Paulo - SP');
    await page.getByRole('button', { name: 'Salvar rascunho' }).click();
    await expect(page.getByRole('status')).toContainText('Rascunho salvo.');
    await page.getByRole('button', { name: 'Emitir pedido' }).click();
    await page.getByRole('button', { name: 'Confirmar emissão' }).click();
    await expect(page.getByRole('heading', { name: /Pedido PO-\d{4}-\d{6}/ })).toBeVisible();
    await waitFor(page, () => page.locator('[data-document="Ready"]'));
    await waitFor(page, () => page.locator('[data-notification="Sent"]'));
    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Baixar PDF' }).click();
    expect((await download).suggestedFilename()).toMatch(/^pedido-PO-\d{4}-\d{6}\.pdf$/);
    expect(await axe(page)).toEqual([]);
    await page.context().close();
  });

  await test.step('receiver records the full receipt and the order completes', async () => {
    const page = await signIn(browser, 'demo.receiver');
    await page.goto(orderPath);
    await page.getByRole('link', { name: 'Receber' }).click();
    await page.getByLabel('Quantidade recebida da linha 1').fill('8');
    await page.getByRole('button', { name: 'Registrar recebimento' }).click();
    await expect(page.getByRole('heading', { name: /Recebimento RCV-\d{4}-\d{6}/ })).toBeVisible();
    await expect(page.locator('[data-status="Completed"]')).toBeVisible();
    expect(await axe(page)).toEqual([]);
    await page.context().close();
  });

  await test.step('requester generates the requisition PDF report and sees the closed history', async () => {
    const page = await signIn(browser, 'demo.requester');
    await nav(page, 'Relatórios');
    await page.getByLabel('Requisição (ID)').fill(requisitionId);
    await page.getByRole('button', { name: 'Gerar relatório' }).click();
    const row = page.locator('tr[data-job]').first();
    await expect(row).toContainText('Pronto', { timeout: 120_000 });
    const download = page.waitForEvent('download');
    await row.getByRole('button', { name: 'Baixar' }).click();
    expect((await download).suggestedFilename()).toBe(`requisicao-${requisitionNumber}.pdf`);

    await page.goto(requisitionPath);
    await expect(page.locator('main')).toContainText('Submetida');
    await expect(page.locator('main')).toContainText(/Encerrada|Concluída|Pedido/);
    expect(await axe(page)).toEqual([]);
    await page.context().close();
  });
});
