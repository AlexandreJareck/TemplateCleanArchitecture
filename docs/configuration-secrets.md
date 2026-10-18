# Configuration secrets

Since Phase 22, no committed file carries a secret value. The tracked `appsettings*.json` files keep only non-secret settings (for example `JwtSettings:Issuer`, `JwtSettings:Audience`, `JwtSettings:DurationInMinutes`, `RabbitMqSettings:UserName`). Every secret comes from a non-committed source, and the WebApi fails at startup when a required one is missing. Validation messages name the key, never the value.

This page names configuration keys and environment variables only. Never copy a value into source, documentation, logs, issues or chat.

## Secret keys

| Configuration key | Environment variable | Required when | Startup behavior when missing |
|---|---|---|---|
| `JwtSettings:Key` | `JwtSettings__Key` | always | `JwtSettings:Key is required.` (or the minimum-length message below 32 UTF-8 bytes) |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | `UseInMemoryDatabase` is false | migrations fail at startup |
| `ConnectionStrings:IdentityConnection` | `ConnectionStrings__IdentityConnection` | `UseInMemoryDatabase` is false | migrations fail at startup |
| `ConnectionStrings:RedisConnection` | `ConnectionStrings__RedisConnection` | `CacheSettings:Enabled` is true outside Test | the product cache misses (fail-soft, unchanged) |
| `RabbitMqSettings:Password` | `RabbitMqSettings__Password` | `RabbitMqSettings:Enabled` is true | `RabbitMqSettings requires ... credentials ...` |

## Sources by profile

| Profile | Source |
|---|---|
| Development (`dotnet run`, Visual Studio) | `dotnet user-secrets` (the WebApi project already has a `UserSecretsId`); environment variables override it |
| Docker Compose | the git-ignored `.env`, rendered into the `webapi` service (`JwtSettings__Key=${JwtSettings__Key}`, connection strings and RabbitMQ credentials from `POSTGRES_*`, `REDIS_PASSWORD` and `RABBITMQ_DEFAULT_*`); `.env.example` holds placeholders only |
| Production | environment variables or the deployment's secret store; Production never reads user secrets |
| Test (functional suite) | `CustomWebApplicationFactory` generates a random signing key and a random synthetic broker password for each host, in memory only |
| Validation scripts | `compose-baseline.ps1`, the rehearsal scripts and `capture-api-baseline.ps1` generate a per-run key with `RandomNumberGenerator`; it lives only in the process environment or the run's git-ignored env file under `artifacts/validation` and is never printed |

## Development setup (one time per machine)

Run from the repository root. The first command generates a 48-byte key with a cryptographically secure generator and stores it without printing it.

```powershell
$bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
dotnet user-secrets set "JwtSettings:Key" ([Convert]::ToBase64String($bytes)) --project Src\Presentation\Template.WebApi
```

When running against local PostgreSQL, Redis and RabbitMQ, store their connection details the same way, replacing each placeholder:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Username=<user>;Password=<password>;Database=TemplateCleanArch;SearchPath=public" --project Src\Presentation\Template.WebApi
dotnet user-secrets set "ConnectionStrings:IdentityConnection" "Host=localhost;Port=5432;Username=<user>;Password=<password>;Database=TemplateCleanArchIdentity;SearchPath=public" --project Src\Presentation\Template.WebApi
dotnet user-secrets set "ConnectionStrings:RedisConnection" "localhost:6379,password=<password>" --project Src\Presentation\Template.WebApi
dotnet user-secrets set "RabbitMqSettings:Password" "<password>" --project Src\Presentation\Template.WebApi
```

`dotnet user-secrets list --project Src\Presentation\Template.WebApi` prints values, so avoid it on shared screens or recorded sessions.

## Docker Compose setup

Copy `.env.example` to `.env` (git-ignored) and replace every `CHANGE_ME`. For `JwtSettings__Key`, generate the value with the command shown in `.env.example` and paste it into `.env`. Never commit `.env`. To check the rendering without your real `.env`, run `docker compose --env-file .env.example config -q`.

## Rotation procedure

1. Generate a new key with a cryptographically secure generator (at least 32 bytes; the commands above use 48).
2. Store it in the profile's source: `dotnet user-secrets set "JwtSettings:Key" ...` (Development), `JwtSettings__Key` in `.env` (Compose), or the deployment's secret store (Production).
3. Restart the WebApi. Every token signed with the previous key fails validation (401), and clients authenticate again. There is no dual-key window (owner decision D4, Phase 22).
4. Never reuse a previous key. The key committed before Phase 22 is compromised and must not be used anywhere. It was removed from every branch by the D6 history rewrite (2026-10-18), but old clones and GitHub pull-request refs may still hold it.

## Guard rails

- `Tests/Template.Architecture.Tests/CommittedSecretsTests.cs` fails when a committed `appsettings*.json` or `launchSettings.json` has a non-empty value under a property ending in `Key`, `Password`, `Pwd`, `Secret` or `Token`, or a connection string with a password, and when a Compose file or `.env.example` assigns `JwtSettings__Key` anything other than `${JwtSettings__Key}` or `CHANGE_ME`.
- `scripts/secret-scan.ps1` applies the same structural rule and also checks that the compromised pre-Phase 22 key value appears in no tracked or untracked, non-ignored file and in no validation evidence. `run-phase-validation.ps1` runs it as step `13-secret-scan`.
