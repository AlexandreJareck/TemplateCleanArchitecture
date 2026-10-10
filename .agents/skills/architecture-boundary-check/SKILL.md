---
name: architecture-boundary-check
description: Detect forbidden project, namespace, and package dependencies after project-reference changes or code moves across Clean Architecture layers. Do not activate for isolated edits that cannot affect dependency direction.
---

# Architecture boundary check

Inputs are changed paths, project/package graph, and temporary exception list. Read [references/boundaries.md](references/boundaries.md), then run `scripts/check-boundaries.ps1` from the repository root.

1. Inspect compile-time project and package dependencies.
2. Run architecture tests and the deterministic source/package scan.
3. For each violation, report the exact edge, governing rule, and smallest correction.
4. Accept only nominal, phase-linked temporary exceptions and verify controlled mutation coverage.

Stop if the graph cannot be resolved or a proposed fix would move behavior beyond the requested phase. Never weaken a rule or broaden an exception merely to obtain green tests, and never inspect secret values. Output a boundary report, test results, exceptions, and unverified checks.
