import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Locator, type Page, type Route } from '@playwright/test';

/**
 * Audit trail (F-03) primary-button contrast across explicit theme preferences. Buttons animate every property for
 * 150 ms (`transition-all`), so right after loading ends the enabled **Pesquisar** button is still fading in from the
 * disabled 50 % opacity. An accessibility scan taken during that fade measures a transient blend, not the design
 * colors; every scan here waits until the button is enabled and its transition has finished.
 */
const auditor = {
  userId: 'd0000000-0000-0000-0000-000000000009',
  userName: 'auditor',
  displayName: 'Auditoria Demo',
  permissions: ['audit.read'],
};

const records = [
  {
    id: 'a1',
    occurredAt: '2026-11-02T16:55:03Z',
    actorId: 'd0000000-0000-0000-0000-000000000002',
    actorName: 'Lucas Almeida',
    action: 'requisition.submitted',
    resourceType: 'PurchaseRequisition',
    resourceId: 'r1',
    fromState: 'Draft',
    toState: 'Submitted',
    ruleIds: ['REQ-004'],
    details: { number: 'REQ-2026-000001' },
  },
];

async function installFixture(page: Page) {
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
    if (path === '/api/v2/account/me') return json(auditor);
    if (path === '/api/v2/audit-records' && method === 'GET') {
      // A realistic round trip keeps the button disabled long enough to exercise the enable transition.
      await new Promise((resolve) => setTimeout(resolve, 300));
      return json({ items: records, page: 1, pageSize: 20, totalItems: 1, totalPages: 1 });
    }
    return json({ code: 'fixture.unhandled', detail: `${method} ${path}` }, 500);
  });
}

/** Waits until the button is enabled and no CSS transition or animation is running on it. */
async function waitUntilStable(button: Locator) {
  await expect(button).toBeEnabled();
  await expect
    .poll(() =>
      button.evaluate(
        (element) =>
          element.getAnimations().length === 0 && getComputedStyle(element).opacity === '1',
      ),
    )
    .toBe(true);
}

/** WCAG contrast of the element's text against its own (opaque) background, colors resolved by the browser. */
function contrast(button: Locator) {
  return button.evaluate((element) => {
    const context = document.createElement('canvas').getContext('2d', {
      willReadFrequently: true,
    })!;
    const rgb = (color: string) => {
      context.clearRect(0, 0, 1, 1);
      context.fillStyle = color;
      context.fillRect(0, 0, 1, 1);
      return [...context.getImageData(0, 0, 1, 1).data];
    };
    const style = getComputedStyle(element);
    const background = rgb(style.backgroundColor);
    const foreground = rgb(style.color);
    const luminance = ([r, g, b]: number[]) =>
      [r, g, b]
        .map((value) => value / 255)
        .map((s) => (s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4))
        .reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index], 0);
    const [light, dark] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
    return {
      opaqueBackground: background[3] === 255,
      opacity: Number(style.opacity),
      ratio: (light + 0.05) / (dark + 0.05),
    };
  });
}

const themes = [
  { name: 'light', preference: 'light', colorScheme: 'light', resolved: 'light' },
  { name: 'dark', preference: 'dark', colorScheme: 'light', resolved: 'dark' },
  { name: 'system (light host)', preference: 'system', colorScheme: 'light', resolved: 'light' },
  { name: 'system (dark host)', preference: 'system', colorScheme: 'dark', resolved: 'dark' },
] as const;

for (const theme of themes) {
  test(`audit primary button keeps 4.5:1 contrast once loaded in ${theme.name} theme`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: theme.colorScheme });
    await page.addInitScript(
      (preference) => localStorage.setItem('procurement-hub.theme', preference),
      theme.preference,
    );
    await installFixture(page);
    await page.goto('/login');
    await page.getByLabel('Usuário').fill('auditor');
    await page.getByLabel('Senha').fill('secret');
    await page.getByRole('button', { name: 'Acessar' }).click();
    await expect(page.getByRole('heading', { name: 'Painel' })).toBeVisible();
    await page
      .getByRole('navigation', { name: 'Principal' })
      .getByRole('link', { name: 'Auditoria' })
      .click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme.resolved);

    const search = page.getByRole('button', { name: 'Pesquisar', exact: true });
    await expect(search).toBeDisabled();
    await waitUntilStable(search);
    await expect(page.getByText('Lucas Almeida').first()).toBeVisible();

    const enabled = await contrast(search);
    expect(enabled.opaqueBackground).toBe(true);
    expect(enabled.opacity).toBe(1);
    expect(enabled.ratio).toBeGreaterThanOrEqual(4.5);
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);

    await search.hover();
    await expect.poll(() => search.evaluate((element) => element.getAnimations().length)).toBe(0);
    expect((await contrast(search)).ratio).toBeGreaterThanOrEqual(4.5);

    await page.mouse.move(0, 0);
    await search.focus();
    await page.keyboard.press('Shift+Tab');
    await page.keyboard.press('Tab');
    await expect(search).toBeFocused();
    expect(await search.evaluate((element) => element.matches(':focus-visible'))).toBe(true);
    expect(await search.evaluate((element) => getComputedStyle(element).boxShadow)).not.toBe(
      'none',
    );
  });
}
