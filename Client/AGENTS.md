# Frontend agent guide

`Template.WebApp` is the client-rendered Angular frontend. Keep dependency direction `Domain <- Application <- Infrastructure / Presentation <- Bootstrap`.

- Domain and Application are plain TypeScript: no Angular, RxJS, DOM, browser globals, generated DTOs, or storage APIs.
- Infrastructure owns generated OpenAPI code and maps transport data/errors into Application types.
- Presentation owns Angular components, Signal Forms, routes, and local Signals. It must not import Infrastructure or generated code.
- Bootstrap is the only composition root and may wire all layers.
- Never hand-edit `src/app/infrastructure/api/generated`; regenerate it with `pnpm api:generate`.
- Use exact package versions and update `pnpm-lock.yaml`. Do not add Nx, SSR, micro-frontends, NgRx, or unapproved packages.
- Keep browser runtime configuration non-secret. Tokens and credentials must not appear in public files or browser storage.
- Run `pnpm validate`, `pnpm audit:licenses`, `pnpm audit:security`, and the Playwright smoke when changing the frontend.
