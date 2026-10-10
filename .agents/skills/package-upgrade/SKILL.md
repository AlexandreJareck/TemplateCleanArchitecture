---
name: package-upgrade
description: Research and stage a coherent NuGet, .NET SDK, or container package-family upgrade using current official sources. Do not activate for ordinary code changes without a version change.
---

# Package upgrade

Inputs are the package family, current TFM/runtime, stability policy, and affected projects. Read [references/upgrade-record.md](references/upgrade-record.md).

1. Inventory direct and transitive versions and trace vulnerable parents.
2. Re-query stable versions and migration notes from official vendor sources; record the consultation date.
3. Change one coherent family, preserve rollback data, restore, audit, build, and test sequentially.
4. Report before/after versions, sources, breaking changes, risk, and validation evidence.

Stop before a major upgrade, unsupported override, or unrelated family unless explicitly authorized. Never infer versions from memory, mix vendor-major/architecture/schema changes, or print feed credentials and configuration values.
