# User Secrets Setup Guide

**Last Updated:** 2026-05-25
**Audience:** Developers

There are **two separate secrets scopes** in this solution. Set the right one depending
on what you are doing.

---

## Scope 1 — `src/MyInvois.Api` (day-to-day development)

Used when running the API host locally (`dotnet run` from `src/MyInvois.Api`).

```powershell
cd c:\Projects\MyInvois-Service\src\MyInvois.Api

# MOVEX DB2/ODBC connection
dotnet user-secrets set "MovexDb:ConnectionString" `
    "Driver={IBM i Access ODBC Driver};System=<AS400_HOST>;UID=<user>;PWD=<password>"

# MyInvois pre-prod OAuth credentials
dotnet user-secrets set "MyInvoisApi:ClientId"     "a777bc19-e8b9-4adb-b793-7c8b64368a5a"
dotnet user-secrets set "MyInvoisApi:ClientSecret" "<secret from IT Ops>"

# Certificate password (trial cert, valid 2026-03-09 → 2026-09-05)
dotnet user-secrets set "MyInvoisApi:CertificatePassword" "<certificate password>"

# API keys for BatchController and SM-Portal integration
dotnet user-secrets set "ApiKeys:Primary" "$(New-Guid)"
dotnet user-secrets set "ApiKeys:Admin"   "$(New-Guid)"
```

The audit log SQLite path is already set in `appsettings.Development.json`
(`Data Source=./data/audit.db`) — no secret needed for local dev.

**Verify:**
```powershell
dotnet user-secrets list
# Expected keys: MovexDb:ConnectionString, MyInvoisApi:ClientId,
#                MyInvoisApi:ClientSecret, MyInvoisApi:CertificatePassword,
#                ApiKeys:Primary, ApiKeys:Admin
```

---

## Scope 2 — `tests/MyInvois.Service.Tests` (smoke test only)

The smoke test (`Category=Smoke`) is the only test that requires real infrastructure.
All 276 unit/integration/E2E tests run without any secrets — never run the smoke test
as part of your normal `dotnet test` run.

Secrets ID: **`myinvois-service-smoketest`**

```powershell
cd c:\Projects\MyInvois-Service

dotnet user-secrets --project tests/MyInvois.Service.Tests `
    set "MovexDb:ConnectionString" `
    "Driver={IBM i Access ODBC Driver};System=<AS400_HOST>;UID=<user>;PWD=<password>"

dotnet user-secrets --project tests/MyInvois.Service.Tests `
    set "MyInvoisApi:ClientId" "a777bc19-e8b9-4adb-b793-7c8b64368a5a"

dotnet user-secrets --project tests/MyInvois.Service.Tests `
    set "MyInvoisApi:ClientSecret" "<secret>"

dotnet user-secrets --project tests/MyInvois.Service.Tests `
    set "MyInvoisApi:CertificatePassword" "<certificate password>"

dotnet user-secrets --project tests/MyInvois.Service.Tests `
    set "ConnectionStrings:AuditLog" "Data Source=E:\data\audit.db"
```

**Run smoke test:**
```powershell
dotnet test tests/MyInvois.Service.Tests `
    --filter "Category=Smoke" `
    --logger "console;verbosity=detailed"
```

**Run all non-smoke tests (normal CI):**
```powershell
dotnet test --filter "Category!=Smoke"
# Expected: Passed! Failed: 0, Passed: 276
```

---

## Common Errors

| Error | Cause | Fix |
|-------|-------|-----|
| `Format of the initialization string does not conform to specification starting at index 0` | `MovexDb:ConnectionString` secret not set; placeholder `{{FROM_USER_SECRETS}}` used literally | Set the secret for the correct scope (Scope 1 or Scope 2) |
| `Invalid client` (OAuth 401) | Wrong `ClientId` or `ClientSecret` | Verify against IT Ops — pre-prod ClientId is `a777bc19-...` |
| `MyInvoisApi:ClientId not configured` | Smoke test secrets not set | Set Scope 2 secrets above |
| `File not found` for certificate | Wrong path in `appsettings.json` or cert not copied | Verify `C:\Certs\MyInvois\SRX_GLOBAL_(MALAYSIA)_SDN._BHD..p12` exists |
| `The supplied password is incorrect` | Wrong certificate password | Get correct password from IT Ops secure store |

---

## LHDN Pre-Prod Endpoint Notes

The **same host** serves both OAuth tokens and API submissions:
```
https://preprod-api.myinvois.hasil.gov.my/connect/token      ← token
https://preprod-api.myinvois.hasil.gov.my/api/v1.0/documentsubmissions  ← submit
```

Do **not** use:
- `sandbox.myinvois.*` — browser-only App Proxy, not M2M
- `identity.myinvois.*` — does not exist for machine-to-machine auth

---

## Party Data Source

`MovexDb:PartyDataSource` controls how supplier/customer TIN and BRN are resolved:

| Value | Behaviour |
|-------|-----------|
| `Placeholder` | Returns stub data; validation will fail (expected in early dev) |
| `MovexMaster` | Queries CIDMAS (suppliers) and OCUSMA (customers) in DB2 |

Switch when TIN/BRN column names are confirmed by Finance:
```powershell
dotnet user-secrets set "MovexDb:PartyDataSource"    "MovexMaster"
dotnet user-secrets set "MovexDb:SupplierTinColumn"  "IDCFC1"
dotnet user-secrets set "MovexDb:SupplierBrnColumn"  "IDCORG"
dotnet user-secrets set "MovexDb:CustomerTinColumn"  "OKCFC1"
dotnet user-secrets set "MovexDb:CustomerBrnColumn"  "OKCORG"
```

---

## User Secrets File Locations (Windows)

```
src/MyInvois.Api secrets:
  %APPDATA%\Microsoft\UserSecrets\<UserSecretsId from .csproj>\secrets.json

tests/MyInvois.Service.Tests secrets (ID: myinvois-service-smoketest):
  %APPDATA%\Microsoft\UserSecrets\myinvois-service-smoketest\secrets.json
```

Run `dotnet user-secrets list` from the project directory to see current values.
