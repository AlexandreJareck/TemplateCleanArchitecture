# spartan/ui provenance

- Source: `@spartan-ng/cli` and `@spartan-ng/brain`
- Version: `1.8.0`
- Style preset: `vega`
- Generated primitives: alert, badge, button, checkbox, dialog, empty, field, input, native-select, pagination, select, separator, skeleton, sonner, and table.
- Generation date: 2026-10-18

The copied Helm sources live only in this directory. The CLI is deliberately not a project dependency because it is needed only during a reviewed update and its 1.8.0 dependency graph carried high-severity development-tool advisories on 2026-10-18. To update, run `pnpm dlx @spartan-ng/cli@<reviewed-exact-version>` in a disposable clean checkout, compare the generated sources with this directory, review accessibility and boundary implications, and then deliberately apply the diff. Never overwrite local primitives without review. Run the spartan health check, component tests, architecture tests, Playwright/axe smoke, and the complete frontend validation after an update.
