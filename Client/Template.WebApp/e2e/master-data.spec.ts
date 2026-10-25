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
    contacts: [],
    addresses: [],
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
  await expect(page.getByText('concurrency.version_mismatch')).toBeVisible();
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
  await expect(page.getByText('Active', { exact: true }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Consultar CNPJ' }).click();
  await expect(page.getByText('enrichment.unavailable')).toBeVisible();
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
  await expect(page.getByText('validation')).toBeVisible();
  await expect(page.getByText('description: Descrição inválida')).toBeVisible();
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
