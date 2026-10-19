# Frontend design-system strategy

Status: accepted by the owner on 2026-10-18.

## Direction

Procurement Hub uses a restrained, information-dense corporate interface without copying a branded consumer design. Tailwind CSS supplies layout/utilities; semantic CSS variables define product meaning; spartan/ui `brain` supplies reviewed behavior and accessibility primitives; copied `helm` code is owned inside Presentation and kept deliberately small. Angular CDK supplies overlay and accessibility infrastructure.

Angular Material/CDK is the documented fallback if the Phase 26B spartan spike fails compatibility, dependency, license or accessibility gates. PrimeNG and React shadcn/ui are not baseline dependencies.

## Tokens

Tokens cover:

- color: canvas, surface, elevated surface, text, muted text, border, action, action-hover, success, warning, danger, information and focus;
- typography: system UI stack, readable body scale, compact table scale and tabular numerals for numeric columns;
- spacing: a controlled 4 px-derived scale;
- radius: small controls, medium panels and round badges only;
- elevation: none, raised panel and modal;
- motion: short/standard durations with `prefers-reduced-motion` disabling nonessential animation;
- layout: content widths and responsive breakpoints aligned to 360 px phone, 768 px tablet and 1024 px desktop requirements.

Raw brand colors and arbitrary spacing do not appear in feature templates when a semantic token exists. Status always has text and, where useful, an icon; color alone never carries meaning.

## Theme decision

Phase 26 ships a light theme only with `color-scheme: light`. Dark mode is deferred because each data/form state would double the contrast and visual regression matrix. Token names remain theme-neutral so a future dark palette does not require feature rewrites.

## Primitive ownership

The minimum Phase 26 set is button, link, form field, text input, native/select wrapper, checkbox, dialog, alert/toast, status badge, table, pagination, skeleton, empty state, error state, confirmation and permission visibility. Prefer native semantic elements; use `brain`/CDK only where keyboard/focus/ARIA behavior is nontrivial. Do not install an entire component suite or a second data-grid engine.

Copied spartan `helm` files live under `presentation/shared-ui`, carry source package/version/provenance in a manifest, and are reviewed as first-party code. Updating means comparing upstream, applying changes manually, running spartan health checks and rerunning component, keyboard, axe and Playwright tests. Automated replacement is forbidden once a copied component is customized.

## Accessibility

Target WCAG 2.2 AA: semantic landmarks, skip link, one `h1`, labelled controls, linked field errors and summary, visible focus, 4.5:1 text contrast, 3:1 meaningful non-text contrast, keyboard-only operation, focus restoration, live feedback, reduced motion, 200% zoom/reflow and high-contrast checks. Responsive tables become labelled cards below 768 px or use a labelled scroll region when column comparison is essential.

Automated axe checks are necessary but insufficient. The release checklist includes keyboard traversal, focus traps/restoration, screen-reader names, Windows high contrast, reduced motion and zoom.
