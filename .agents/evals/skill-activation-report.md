# Skill activation evaluation

- Date: 2026-10-10 (America/Sao_Paulo)
- Dataset: `skill-eval-case/v1`
- Runner: recorded static routing review against each skill's frontmatter description
- Cases: 12 (6 implicit triggers, 6 non-triggers)
- Result: 12 passed, 0 failed, 0 not run

The eval harness validated the JSONL dataset and graded all `trigger_equals` checks successfully. This run verifies deterministic activation decisions; it does not constitute an independent model-run or semantic quality evaluation.

The bundled `skill-creator` validator could not start because its Python environment lacks `PyYAML`. No dependency was installed. An equivalent local check verified required frontmatter, folder/name equality, and absence of scaffold TODOs for all six skills. The skill scripts were also executed: change-selection produced the expected checks, and the architecture scan correctly detected the known Application vendor-package baseline violation.
