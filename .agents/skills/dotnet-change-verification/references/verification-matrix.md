# Verification matrix

| Change | Minimum checks | Wider checks |
|---|---|---|
| Domain/Application C# | affected unit tests; WebApi build | integration, functional, architecture |
| Persistence or mapping | WebApi build; integration tests | migration SQL review; functional tests |
| WebApi/controller | WebApi build; functional tests | OpenAPI comparison |
| `.csproj`, props, targets, SDK | restore; affected build/tests | package audit and all application suites |
| Compose | `docker compose config -q` | owned-service smoke test |

Use repository `AGENTS.md` for currently verified commands. Keep VSIX validation separate unless VSIX files changed.
