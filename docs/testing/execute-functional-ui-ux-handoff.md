# Execute Handoff: Functional and UI/UX Remediation

Execute this handoff end-to-end. Make the required code changes, add regression tests, run all applicable validations, and update the source report. Do not stop after reviewing the findings or proposing a plan.

## Source Report

Use the following report as the authoritative scope and evidence source:

`docs/testing/functional-ui-ux-test-report-2026-11-05.md`

First, read the repository's root `AGENTS.md` and every applicable nested `AGENTS.md` before changing files.

## Objective

- Resolve findings F-01 through F-09.
- Prioritize F-02, F-01, and F-03, in that order.
- Follow the implementation sequence, architectural constraints, reproduction steps, and acceptance criteria defined in the source report.
- Keep the interface in Brazilian Portuguese and fully responsive.
- Add automated regression coverage for every correction.
- Preserve the repository's Clean Architecture boundaries and existing product rules.

## Before Implementation

1. Read the complete source report.
2. Reproduce and confirm every finding against the current application.
3. Check whether relevant code has changed since the report was created.
4. Inspect the worktree and preserve unrelated user changes.
5. Provide a concise implementation plan that groups related fixes and identifies the verification required for each group.
6. Continue directly into implementation after the plan; do not wait for approval unless a genuinely scope-changing decision or unsafe action requires user input.

## Implementation Requirements

- Complete every safe correction that clearly belongs to F-01 through F-09.
- Do not manually edit generated API client files.
- Do not weaken server-side authentication, authorization, validation, audit allow-lists, or concurrency protection.
- Preserve ETag/version handling and optimistic concurrency behavior.
- Keep backend validation authoritative; client validation should provide earlier and clearer feedback.
- Keep permission checks consistent across routes, navigation, data access, and action rendering.
- Reuse shared presentation, localization, status, date/time, error-feedback, and validation components where appropriate.
- Do not expose audit fields beyond the product's allow-listed details.
- Keep raw GUIDs, API field names, and technical messages out of the primary user experience unless they are operationally necessary.
- When a technical identifier remains useful, place it in optional technical details rather than the main message.
- Add or update component, application, and integration tests in proportion to each change.
- Do not silently change the HTTP contract. If an API contract must change, follow the repository's API contract instructions and update OpenAPI plus contract tests.

## Required Findings

The complete definitions and acceptance criteria remain in the source report. The implementation must address:

1. **F-01:** Supplier `Detalhes` is inert for read-only users.
2. **F-02:** An existing requisition draft opens with a blank cost center in the editor.
3. **F-03:** The Auditor persona has no audit-record screen or navigation entry.
4. **F-04:** Report validation exposes raw English server text, API field names, and a trace ID.
5. **F-05:** Supplier status is displayed as an untranslated English enum value.
6. **F-06:** Dates, instants, decimal editor values, and actor identifiers are not presented in a human-friendly localized form.
7. **F-07:** Report filters require users to paste raw entity IDs.
8. **F-08:** The master-data create form enables submission for a syntactically invalid code.
9. **F-09:** Requisition attachment help text is stale.

## Recommended Execution Order

1. Fix F-02 and add load-order/data-preservation regression tests.
2. Fix F-01 and implement F-03, including permission tests.
3. Introduce or consolidate shared localized presentation primitives for F-04, F-05, and F-06.
4. Implement typed report selectors and dynamic validation for F-04 and F-07.
5. Mirror stable syntactic domain validation client-side for F-08 without replacing server validation.
6. Correct the current attachment behavior or copy for F-09.
7. Run the complete regression suite and update the source report.

## Verification

Run the frontend validation from `Client/Template.WebApp`:

```powershell
pnpm validate
```

If C#, project files, solution files, HTTP contracts, or backend behavior change, run the repository's proportionate .NET restore, build, test, architecture, migration, and contract checks required by the applicable agent skills and instructions.

Rebuild the affected local services:

```powershell
docker compose up -d --build webapp webapi worker workflow-functions
docker compose ps
```

Then verify:

1. All affected containers are running and applicable health checks pass.
2. The browser console contains no new warnings or errors.
3. Service logs contain no new unhandled errors.
4. Desktop behavior works in the default viewport.
5. Mobile behavior works at 390 × 844 without horizontal overflow.
6. Light, dark, and system themes remain usable.
7. Permission-based navigation and actions remain correct for the seeded personas.
8. No unrelated behavior or permissions regress.

## Mandatory Browser Regression Checklist

1. A read-only supplier user can open supplier details but cannot mutate them.
2. `REQ-2026-000001` shows `TEC-INFRA` selected in edit mode, and a no-change save preserves its cost-center ID.
3. `demo.auditor` can discover and use the audit screen according to its permissions.
4. An incomplete report request is blocked client-side with localized field feedback and without raw API keys in the main message.
5. Supplier and workflow statuses are localized consistently.
6. Local dates and instants use the expected pt-BR presentation and business time zone.
7. Timeline actors are human-readable when the user is allowed to see that information.
8. Report selectors display business identifiers and names while submitting stable IDs.
9. Currency is selected from the supported controlled values rather than arbitrary free text.
10. The invalid master-data code `x` is rejected before any network request.
11. Attachment copy accurately describes the implemented behavior.
12. Repeat the relevant flows on desktop and mobile.

## Update the Source Report

Update `docs/testing/functional-ui-ux-test-report-2026-11-05.md` with the final outcome of every finding, F-01 through F-09.

Mark each finding as one of:

- Fixed
- Partially fixed
- Not fixed

For every finding, record:

- implementation summary;
- modified files;
- automated tests added or updated;
- manual verification evidence;
- commands executed and their results;
- remaining risks or limitations.

Do not erase the original reproduction evidence. Add the resolution status and verification evidence alongside it.

## Completion Criteria

Do not finish while a safe, in-scope correction remains incomplete.

The final response must include:

- a concise implementation summary;
- all modified files;
- the status of F-01 through F-09;
- tests and validation commands with their results;
- manual browser verification performed;
- remaining risks, limitations, or follow-up work;
- evidence that every applicable acceptance criterion was satisfied.

If a finding cannot be completed, explain the exact blocker, what was attempted, the evidence collected, and the smallest next action needed to finish it.
