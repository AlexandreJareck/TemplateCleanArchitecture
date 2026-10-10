---
name: dotnet-change-verification
description: Select and run proportionate restore, build, and test checks after changes to C#, project files, solution files, or .NET build configuration. Do not activate for documentation-only edits.
---

# .NET change verification

Use the changed paths, requested scope, and available dependencies as inputs. Read [references/verification-matrix.md](references/verification-matrix.md) before choosing checks.

1. Inspect the diff without exposing configuration values.
2. Select the smallest relevant checks, then widen only after they pass. Run .NET commands sequentially.
3. Report exact commands, exit results, test counts, warnings, and checks not run.

Stop at the first failed prerequisite or reproducible failed gate and preserve its output. Do not change code merely to make a check pass, start live dependencies without authorization, or claim an unavailable check passed. Output a concise verification report; never print secrets or `.env` contents.
