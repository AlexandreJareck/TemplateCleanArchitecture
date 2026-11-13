import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type Route } from '@playwright/test';

interface FixtureState {
  loggedIn: boolean;
  accessToken: string;
  refreshes: number;
  staleDepartmentToken: boolean;
  departmentVersion: string;
  supplierStatus: string;
  permissions: string[];
  catalogCreateAttempts: number;
  lastDepartmentQuery: string | null;
  longContact: boolean;
}

const user = {
  userId: 'user-1',
  userName: 'admin',
  displayName: 'Administrador',
  permissions: [
    'organization.read',
    'organization.manage',
    'suppliers.read',
    'suppliers.manage',
    'suppliers.status.change',
    'catalog.read',
    'catalog.manage',
  ],
};

async function installApiFixture(page: Page, permissions = user.permissions) {
  const state: FixtureState = {
    loggedIn: false,
    accessToken: 'token-1',
    refreshes: 0,
    staleDepartmentToken: false,
    departmentVersion: '1',
    supplierStatus: 'Draft',
    permissions,
    catalogCreateAttempts: 0,
    lastDepartmentQuery: null,
    longContact: false,
  };
  await page.route('**/api/v2/**', async (route) => handleApi(route, state));
  return state;
}

async function handleApi(route: Route, state: FixtureState) {
  const request = route.request();
  const path = new URL(request.url()).pathname;
  const method = request.method();
  const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
    route.fulfill({ status, contentType: 'application/json', headers, body: JSON.stringify(body) });

  if (path === '/api/v2/auth/sessions/refresh' && method === 'POST') {
    if (!state.loggedIn) return json({ code: 'session.expired' }, 401);
    state.refreshes += 1;
    state.accessToken = `token-${state.refreshes + 1}`;
    return json({ accessToken: state.accessToken });
  }
  if (path === '/api/v2/auth/sessions' && method === 'POST') {
    state.loggedIn = true;
    return json({ accessToken: state.accessToken });
  }
  if (path.startsWith('/api/v2/auth/sessions') && method === 'DELETE') {
    state.loggedIn = false;
    return route.fulfill({ status: 204 });
  }
  if (path === '/api/v2/account/me') return json({ ...user, permissions: state.permissions });

  if (path === '/api/v2/departments' && method === 'GET') {
    state.lastDepartmentQuery = new URL(request.url()).searchParams.get('q');
    if (state.staleDepartmentToken && request.headers()['authorization'] === 'Bearer token-1')
      return json({ code: 'session.expired' }, 401);
    return json({
      items: [
        {
          id: 'd1',
          code: 'FIN',
          name: 'Financeiro',
          headUserId: 'u1',
          isActive: true,
          version: state.departmentVersion,
        },
      ],
      page: 1,
      pageSize: 20,
      totalItems: 1,
      totalPages: 1,
    });
  }
  if (path === '/api/v2/departments' && method === 'POST')
    return json({ id: 'd2', code: 'OPS', name: 'Operações', isActive: true, version: '1' }, 201);
  if (path === '/api/v2/departments/d1' && method === 'PUT') {
    if (request.headers()['if-match'] === '"1"') {
      state.departmentVersion = '2';
      return json(
        { code: 'concurrency.version_mismatch', version: '2', traceId: 'trace-conflict' },
        412,
      );
    }
    return json({ id: 'd1', code: 'FIN', name: 'Financeiro', isActive: true, version: '3' });
  }
  if (path.includes('/departments/d1/') && method === 'POST')
    return json({ id: 'd1', code: 'FIN', name: 'Financeiro', isActive: false, version: '2' });

  if (path === '/api/v2/catalog-items' && method === 'GET')
    return json({ items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 });
  if (path === '/api/v2/catalog-items' && method === 'POST') {
    state.catalogCreateAttempts += 1;
    if (state.catalogCreateAttempts === 1)
      return json({ code: 'validation', errors: { description: ['Descrição inválida'] } }, 400);
    return json(
      {
        id: 'i1',
        code: 'ITEM',
        description: 'Item',
        kind: 'Good',
        categoryId: 'c1',
        unitOfMeasureId: 'u1',
        isActive: true,
        version: '1',
      },
      201,
    );
  }
  if (/\/api\/v2\/(categories|units-of-measure|cost-centers)$/.test(path) && method === 'GET')
    return json({ items: [], page: 1, pageSize: 20, totalItems: 0, totalPages: 0 });

  if (path === '/api/v2/suppliers' && method === 'GET')
    return json({
      items: [
        {
          id: 's1',
          legalName: 'Fornecedor Exemplo',
          tradeName: 'Exemplo',
          formattedTaxId: '12.345.678/0001-95',
          status: state.supplierStatus,
          isEligibleForNewCommitments: false,
          version: '1',
        },
      ],
      page: 1,
      pageSize: 20,
      totalItems: 1,
      totalPages: 1,
    });
  if (path === '/api/v2/suppliers/s1' && method === 'GET') return json(supplier(state));
  if (path === '/api/v2/suppliers' && method === 'POST') return json(supplier(state), 201);
  if (path === '/api/v2/suppliers/s1' && method === 'PUT') return json(supplier(state));
  if (path.includes('/api/v2/suppliers/s1/') && method === 'POST') {
    state.supplierStatus = path.split('/').at(-1) === 'activate' ? 'Active' : state.supplierStatus;
    return json(supplier(state));
  }
  if (path.includes('/api/v2/suppliers/s1/') && method === 'PUT') return json(supplier(state));
  if (path.includes('/api/v2/enrichment/company-registrations/'))
    return json({ code: 'enrichment.unavailable', detail: 'Provider unavailable' }, 503);
  return json({ code: 'fixture.unhandled', detail: `${method} ${path}` }, 500);
}

function supplier(state: FixtureState) {
  return {
    id: 's1',
    legalName: 'Fornecedor Exemplo',
    tradeName: 'Exemplo',
    country: 'BR',
    taxId: '12345678000195',
    formattedTaxId: '12.345.678/0001-95',
    status: state.supplierStatus,
    isEligibleForNewCommitments: false,
    categoryIds: [],
    contacts: state.longContact
      ? [
          {
            id: 'k1',
            name: 'Ana Souza',
            email: 'relacionamento.comercial.fornecedores@alfa-tecnologia-servicos.test',
            phone: '+55 11 99999-0000',
            role: 'Vendas',
          },
        ]
      : [],
    addresses: state.longContact
      ? [
          {
            id: 'a1',
            street: 'Avenida das Nações Unidas',
            number: '12901',
            city: 'São Paulo',
            state: 'SP',
            postalCode: '04578-910',
            country: 'BR',
          },
        ]
      : [],
    statusHistory: [],
    missingForActivation: 'Contato e endereço obrigatórios',
    version: '1',
  };
}

async function login(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/login\?reason=expired/);
  await page.getByLabel('Usuário').fill('admin');
  await page.getByLabel('Senha').fill('secret');
  await page.getByRole('button', { name: 'Acessar' }).click();
  await expect(page.getByRole('heading', { name: 'Painel' })).toBeVisible();
}

test('login, permission-aware shell, refresh race, conflict, logout and accessibility', async ({
  page,
}) => {
  const state = await installApiFixture(page);
  await login(page);
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);

  state.staleDepartmentToken = true;
  await page
    .getByRole('navigation', { name: 'Principal' })
    .getByRole('link', { name: 'Departamentos' })
    .click();
  await page.getByRole('button', { name: 'Atualizar' }).click();
  await expect(page.getByRole('cell', { name: 'FIN', exact: true })).toBeVisible();
  expect(state.refreshes).toBe(1);
  await page.getByLabel('Pesquisar').fill('financeiro');
  await page.getByRole('button', { name: 'Atualizar' }).click();
  expect(state.lastDepartmentQuery).toBe('financeiro');

  await page.getByRole('button', { name: 'Editar' }).click();
  await page.getByLabel('Nome').fill('Financeiro novo');
  await page.getByRole('button', { name: 'Salvar' }).click();
  // Localized message first; the stable code and trace stay under optional technical details (F-04).
  await expect(page.getByText('Este registro foi alterado por outra pessoa.')).toBeVisible();
  await expect(page.getByText('trace-conflict')).toBeHidden();
  await page.getByText('Detalhes técnicos').click();
  await expect(page.getByText('trace-conflict')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByRole('button', { name: 'Editar' })).toBeFocused();

  await page.getByRole('button', { name: 'Encerrar todas' }).click();
  await expect(page).toHaveURL(/\/login$/);
  expect(state.loggedIn).toBe(false);
});

test('supplier detail/status and enrichment failure remain actionable', async ({ page }) => {
  await installApiFixture(page);
  await login(page);
  await page
    .getByRole('navigation', { name: 'Principal' })
    .getByRole('link', { name: 'Fornecedores' })
    .click();
  await page.getByRole('button', { name: 'Detalhes' }).click();
  await expect(page.getByText('Contato e endereço obrigatórios')).toBeVisible();
  await page.getByRole('button', { name: 'Ativar', exact: true }).click();
  await page.getByLabel('Motivo').fill('Cadastro conferido para ativação');
  await page.getByRole('button', { name: 'Confirmar' }).click();
  await expect(page.getByText('Ativo', { exact: true }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Consultar CNPJ' }).click();
  await expect(page.getByText('Serviço indisponível', { exact: true })).toBeVisible();
});

for (const viewport of [
  { name: 'desktop', width: 1365, height: 768, columns: 4 },
  { name: 'mobile', width: 390, height: 844, columns: 1 },
]) {
  test(`read-only supplier summary wraps long contact values on ${viewport.name} (F-01)`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const state = await installApiFixture(page, ['organization.read', 'suppliers.read']);
    state.longContact = true;
    await login(page);
    await page.goto('/suppliers');
    const details = page.getByRole('button', { name: /^Detalhes/ });
    await details.focus();
    await page.keyboard.press('Enter');
    const summary = page.getByTestId('supplier-readonly');
    await expect(summary).toContainText(
      'relacionamento.comercial.fornecedores@alfa-tecnologia-servicos.test',
    );
    const dialog = page.getByRole('dialog');
    await expect(dialog.locator('input, select, textarea, button[type="submit"]')).toHaveCount(0);
    await expect(dialog.getByText('Rascunho', { exact: true })).toBeVisible();

    const layout = await summary.evaluate((dl) => ({
      columns: getComputedStyle(dl).gridTemplateColumns.split(' ').length,
      cells: [...dl.querySelectorAll<HTMLElement>(':scope > div')].map((cell) => ({
        label: cell.querySelector('dt')?.textContent?.trim(),
        overflow: cell.scrollWidth - cell.clientWidth,
        valueOverflow:
          cell.querySelector('dd')!.scrollWidth - cell.querySelector('dd')!.clientWidth,
        rect: cell.getBoundingClientRect().toJSON() as DOMRect,
      })),
    }));
    expect(layout.columns).toBe(viewport.columns);
    for (const cell of layout.cells) {
      expect(cell.overflow, `${cell.label} cell overflow`).toBeLessThanOrEqual(0);
      expect(cell.valueOverflow, `${cell.label} value overflow`).toBeLessThanOrEqual(0);
    }
    // No two summary cells overlap.
    for (const [i, a] of layout.cells.entries())
      for (const b of layout.cells.slice(i + 1)) {
        const overlaps =
          a.rect.left < b.rect.right - 1 &&
          b.rect.left < a.rect.right - 1 &&
          a.rect.top < b.rect.bottom - 1 &&
          b.rect.top < a.rect.bottom - 1;
        expect(overlaps, `${a.label} overlaps ${b.label}`).toBe(false);
      }
    const overflow = await page.evaluate(() => ({
      page: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      dialog: (() => {
        const d = document.querySelector<HTMLElement>('[role="dialog"]')!;
        return d.scrollWidth - d.clientWidth;
      })(),
    }));
    expect(overflow).toEqual({ page: 0, dialog: 0 });
    expect(
      (await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations,
    ).toEqual([]);

    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(details).toBeFocused();
  });
}

test('keyboard only: sign in, skip to content, open a page from the menu and use the drawer', async ({
  page,
}) => {
  // Phase 33 keyboard smoke of the critical shell flows (WCAG 2.1.1, 2.4.1, 2.4.3); no mouse event is used.
  await installApiFixture(page);
  await page.goto('/login');
  await expect(page.getByLabel('Usuário')).toBeEditable();
  await page.getByLabel('Usuário').focus();
  await page.keyboard.type('admin');
  await page.keyboard.press('Tab');
  await expect(page.getByLabel('Senha')).toBeFocused();
  await page.keyboard.type('secret');
  await expect(page.getByRole('button', { name: 'Acessar' })).toBeEnabled();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Painel' })).toBeVisible();
  // Every navigation moves focus to the main content (WCAG 2.4.3).
  await expect(page.locator('#main-content')).toBeFocused();

  // The skip link is the first focusable element of the page and Enter on it moves focus to the content.
  const firstFocusable = await page.evaluate(() =>
    document
      .querySelector<HTMLElement>('a[href], button, input, select, textarea')
      ?.textContent?.trim(),
  );
  expect(firstFocusable).toBe('Ir para o conteúdo');
  const skip = page.getByRole('link', { name: 'Ir para o conteúdo' });
  await skip.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#main-content')).toBeFocused();

  const categories = page
    .getByRole('navigation', { name: 'Principal' })
    .getByRole('link', { name: 'Categorias' });
  await categories.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Categorias', level: 1 })).toBeVisible();

  const create = page.getByRole('button', { name: 'Cadastrar' });
  await create.focus();
  await page.keyboard.press('Enter');
  const drawer = page.getByRole('dialog', { name: 'Cadastrar' });
  await expect(drawer).toBeVisible();
  for (let index = 0; index < 8; index++) {
    await page.keyboard.press('Tab');
    expect(await drawer.evaluate((element) => element.contains(document.activeElement))).toBe(true);
  }
  await page.keyboard.press('Escape');
  await expect(drawer).toBeHidden();
  await expect(create).toBeFocused();
});

test('catalog empty, filter and create journey uses the isolated HTTP fixture', async ({
  page,
}) => {
  await installApiFixture(page);
  await login(page);
  await page
    .getByRole('navigation', { name: 'Principal' })
    .getByRole('link', { name: 'Itens de catálogo' })
    .click();
  await expect(page.getByRole('heading', { name: 'Nenhum resultado' })).toBeVisible();
  await page.getByRole('button', { name: 'Cadastrar' }).click();
  const drawer = page.getByRole('dialog', { name: 'Cadastrar' });
  await expect(drawer).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  await page.getByLabel('Código').fill('ITEM');
  await page.getByLabel('Nome').fill('Item');
  await page.getByLabel('ID relacionado').fill('c1');
  await page.getByLabel('ID da unidade').fill('u1');
  await page.getByRole('button', { name: 'Salvar' }).click();
  await expect(page.getByText('Revise os campos informados')).toBeVisible();
  await expect(page.getByText('Descrição: Valor inválido.')).toBeVisible();
  await page.getByRole('button', { name: 'Salvar' }).click();
  await expect(drawer).toBeHidden();
  await expect(page.getByRole('button', { name: 'Cadastrar' })).toBeFocused();
  await expect(page.getByRole('heading', { name: 'Itens de catálogo' })).toBeVisible();
});

test('forbidden navigation is separated from an expired session', async ({ page }) => {
  await installApiFixture(page, ['organization.read']);
  await login(page);
  await page.goto('/catalog/items');
  await expect(page.getByRole('heading', { name: 'Acesso negado' })).toBeVisible();
});

test.describe('dark theme', () => {
  test.use({ colorScheme: 'dark' });

  test('login, dashboard and a list have no accessibility violations in dark mode', async ({
    page,
  }) => {
    await installApiFixture(page);
    await page.goto('/');
    await expect(page).toHaveURL(/\/login\?reason=expired/);
    await expect(page.locator('html')).toHaveClass(/dark/);
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);

    await page.getByLabel('Usuário').fill('admin');
    await page.getByLabel('Senha').fill('secret');
    await page.getByRole('button', { name: 'Acessar' }).click();
    await expect(page.getByRole('heading', { name: 'Painel' })).toBeVisible();
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);

    await page
      .getByRole('navigation', { name: 'Principal' })
      .getByRole('link', { name: 'Fornecedores' })
      .click();
    await expect(page.getByRole('heading', { name: 'Fornecedores', level: 1 })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Detalhes' }).first()).toBeVisible();
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  });
});
