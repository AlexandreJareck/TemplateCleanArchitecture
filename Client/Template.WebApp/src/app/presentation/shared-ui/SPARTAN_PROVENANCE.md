# spartan/ui provenance

- Source: `@spartan-ng/cli` and `@spartan-ng/brain`
- Version: `1.8.0`
- Style preset: `vega`
- Generated primitives: alert, badge, button, checkbox, dialog, empty, field, input, native-select, pagination, select, separator, skeleton, sonner, and table.
- Local primitive: `textarea` (`hlmTextarea`), written in this repository on 2026-10-24 by following the generated `input` Helm (same brain host directives and tokens, multi-line sizing). Replace it with the CLI-generated textarea on the next reviewed update.
- Local change: the `button` default variant hovers with `bg-primary/90` instead of the generated `bg-primary/80`, because white text on `primary/80` measured 4.25:1 (axe `color-contrast`, below WCAG AA 4.5:1). Keep it on updates.
- Generation date: 2026-10-18

The copied Helm sources live only in this directory. The CLI is deliberately not a project dependency because it is needed only during a reviewed update and its 1.8.0 dependency graph carried high-severity development-tool advisories on 2026-10-18. To update, run `pnpm dlx @spartan-ng/cli@<reviewed-exact-version>` in a disposable clean checkout, compare the generated sources with this directory, review accessibility and boundary implications, and then deliberately apply the diff. Never overwrite local primitives without review. Run the spartan health check, component tests, architecture tests, Playwright/axe smoke, and the complete frontend validation after an update.
