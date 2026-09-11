# MyInvois-Service — Deployment Runbook

**Target Audience:** IT Operations / Development Lead
**Version:** 3.1
**Last Updated:** 2026-09-09
**Status:** UAT signed off — **production go-live in progress** (certificate and credentials verified)

> **Going to production?** Work through **[Part B — Production Go-Live](#part-b--production-go-live)**
> at the foot of this document. It is a self-contained runbook covering the certificate chain
> import, endpoint and credential switch, and first-submission verification. Parts 1–10 below
> describe the UAT deployment and remain the reference for routine redeploys.

---

## Overview

MyInvois-Service is an ASP.NET Core 8 Worker + API host (`MyInvois.Api`) deployed as a
long-running Windows process on **SRXWEBAPP1**. It:

1. Runs a **daily batch** at 02:00 via `DailyBatchHostedService` (controlled by `BatchScheduler:Enabled`)
2. Exposes a **manual trigger** at `POST /api/v1/batch/process-range` (API-Key protected)
3. Persists an **audit log** to SQLite (`audit.db`, WAL mode, 7-year retention)

The daily scheduler is **not** Windows Task Scheduler — it is an in-process `BackgroundService`.
Keeping the process alive (as a Windows Service or IIS-hosted app) is all that is needed.

---

## Pre-Deployment Checklist

### Infrastructure

- [ ] SRXWEBAPP1 accessible; IIS running
- [ ] IBM i Access ODBC Driver installed (`iSeries Access for Windows` or `IBM i Access Client Solutions`)
- [ ] .NET 8.0 Hosting Bundle installed (`dotnet-hosting-win.exe`)
- [ ] `sqlite3.exe` installed for audit log inspection (`winget install SQLite.SQLite`)
- [ ] Firewall allows outbound HTTPS to `preprod-api.myinvois.hasil.gov.my` (port 443)
- [ ] Firewall allows outbound ODBC to AS400 (port 446 or 8471)

### Certificate

**UAT / pre-prod:**
- [ ] Trial cert file present: `C:\Certs\MyInvois\SRX_GLOBAL_(MALAYSIA)_SDN._BHD..p12`
- [ ] Trial cert expiry confirmed: **2026-09-05**
- [ ] NTFS ACL on `C:\Certs\MyInvois\` restricted to service account + Administrators
- [ ] EFS encryption enabled on `C:\Certs\MyInvois\`

**Production:** the trial cert does not apply and the production certificate arrives as a
three-file chain, not a single `.p12`. See **[Part B](#part-b--production-go-live)**.

### Data directory

- [ ] `E:\data\` exists
- [ ] App pool identity (`IIS_IUSRS` or dedicated pool) has **write** access to that directory
- [ ] `audit.db` will be created automatically on first startup — do NOT create manually

---

## UAT Deploy Workflow (Current)

The UAT server (`SRXWEBAPP1`) does not have a direct connection to the dev machine. The workflow is:

1. **Push changes** to the `develop` branch on GitHub from your dev machine
2. **On SRXWEBAPP1**: download the branch as a zip from GitHub, extract to `C:\Projects\MyInvois-Service\v2.0\MyInvois-Service-master\`
3. **Publish** from that extracted folder to IIS:
   ```powershell
   cd "C:\Projects\MyInvois-Service\v2.0\MyInvois-Service-master"
   dotnet publish .\src\MyInvois.Api\MyInvois.Api.csproj `
       --configuration Release `
       --output "C:\inetpub\wwwroot\MyInvois-Api\"
   ```
4. **Recycle the app pool** in IIS Manager (or `Restart-WebAppPool -Name "<pool-name>"`)
5. **Verify** the health endpoint responds: `Invoke-RestMethod http://localhost:5051/api/v1/health`

> **IIS site details:** Name = `MyInvoisAPI`, Port = **5051**, root site (no virtual path prefix),
> physical path = `C:\inetpub\wwwroot\MyInvois-Api`. PID 4 owning port 5051 is normal — that is
> IIS kernel-mode driver (`http.sys`), not the .NET process.

> Do **not** overwrite `appsettings.UAT.json` when deploying — it contains secrets and lives on
> the server only. The zip from GitHub will not contain it.

---

## Step 1 — Build the Release Package (Dev Machine)

Run from your development machine before pushing to GitHub:

```powershell
cd "c:\Projects\MyInvois-Service"

# 1a. Confirm all tests pass before pushing
dotnet test --configuration Release --filter "Category!=Sandbox&Category!=RequiresDb2"
# Expected: Passed! Failed: 0, Passed: 336

# 1b. Push to GitHub
git push origin develop
```

---

## Step 2 — Prepare the UAT Configuration Override

Create `appsettings.UAT.json` **on SRXWEBAPP1** (never commit this file — it contains secrets).
Place it alongside the published files in `C:\inetpub\wwwroot\MyInvois\`.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "MyInvois": "Debug"
    }
  },
  "ConnectionStrings": {
    "AuditLog": "Data Source=E:\\data\\audit.db"
  },
  "MovexDb": {
    "ConnectionString": "<MOVEX ODBC connection string — from IT Ops>",
    "ActiveCompanyCodes": [ "100" ],
    "ArMinYear": 2025
  },
  "MyInvoisApi": {
    "BaseUrl": "https://preprod-api.myinvois.hasil.gov.my",
    "IdentityBaseUrl": "https://preprod-api.myinvois.hasil.gov.my",
    "Environment": "preprod",
    "CertificatePath": "C:\\Certs\\MyInvois\\SRX_GLOBAL_(MALAYSIA)_SDN._BHD..p12",
    "CertificatePassword": "<certificate password — from secure store>",
    "ClientId": "&lt;pre-prod ClientId — from IT Ops&gt;",
    "ClientSecret": "<pre-prod client secret — from IT Ops>"
  },
  "ApiKeys": {
    "Primary": "<generate with New-Guid — note it for SM-Portal config>",
    "Admin":   "<generate with New-Guid>"
  },
  "BatchScheduler": {
    "Enabled": true,
    "DailyRunHour": 2,
    "DailyRunMinute": 0,
    "LookbackDays": 1
  }
}
```

> **`BatchScheduler:Enabled: true`** is the only switch that controls the daily batch.
> The old `EnableBatchProcessing` flag no longer exists — it was dead config and has been removed.
>
> `LookbackDays: 1` means each nightly run processes yesterday's invoices.
> Increase to `7` temporarily if you need to catch up after a missed run.

Set the environment variable on SRXWEBAPP1 so ASP.NET Core loads this file:

```powershell
# Run on SRXWEBAPP1 — persistent, machine-level
[System.Environment]::SetEnvironmentVariable(
    "ASPNETCORE_ENVIRONMENT", "UAT",
    [System.EnvironmentVariableTarget]::Machine)
```

---

## Step 3 — Create the Audit Data Directory

Run on SRXWEBAPP1 as Administrator:

```powershell
New-Item -ItemType Directory -Force "E:\data"

# Grant the app pool write access (adjust identity to match your IIS pool name)
icacls "E:\data" /grant "IIS_IUSRS:(OI)(CI)F" /T

# Verify
icacls "E:\data"
```

EF Core creates `audit.db` automatically on first startup — do not create the file manually.

---

## Step 4 — Copy Build Artifacts to SRXWEBAPP1

```powershell
# From your dev machine
$dest = "\\SRXWEBAPP1\c$\inetpub\wwwroot\MyInvois"

Copy-Item "c:\Projects\MyInvois-Service\publish\uat\*" $dest -Recurse -Force

# Verify key files arrived
Get-ChildItem $dest | Where-Object { $_.Name -match "MyInvois.Api|appsettings" }
```

Do not overwrite `appsettings.UAT.json` if it already exists on the server (it contains secrets).

---

## Step 5 — Configure the Certificate (Windows Store — Recommended)

The certificate must be loaded from the **Windows Certificate Store by thumbprint** for IIS deployments.
Loading from file path fails under IIS app pool identities due to CNG key store access restrictions,
and passwords containing special characters (e.g. `{`) are corrupted by ASP.NET Core config token substitution.

**5a. Import the certificate into the LocalMachine store:**
```powershell
# Run as Administrator
certlm.msc
# Navigate to: Personal → Certificates → right-click → All Tasks → Import
# Import: C:\Certs\MyInvois\SRX_GLOBAL_(MALAYSIA)_SDN._BHD..p12
# Store: Local Machine → Personal
```

**5b. Grant the app pool read access to the private key:**
```powershell
certlm.msc
# Personal → Certificates → right-click SRX GLOBAL cert
# All Tasks → Manage Private Keys → Add
# Object: IIS AppPool\MyInvoisAPI → Check Names → OK
# Permission: Read → OK
```

**5c. Get the thumbprint and verify:**
```powershell
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store("My","LocalMachine")
$store.Open("ReadOnly")
$cert = $store.Certificates | Where-Object { $_.Subject -like "*SRX*" }
Write-Host "Thumbprint:   $($cert.Thumbprint)"
Write-Host "Expires:      $($cert.NotAfter)   (trial cert: 2026-09-05)"
Write-Host "Has Priv Key: $($cert.HasPrivateKey)"   # Must be True
Write-Host "Days left:    $(($cert.NotAfter - (Get-Date)).Days)"
$store.Close()
```

**5d. Set thumbprint in `appsettings.json`** on the server (no password needed):
```json
"MyInvoisApi": {
  "CertificateThumbprint": "A0E772A9F4EC1D26B732515A3430728E82D78FD7"
}
```

> **UAT thumbprint:** `A0E772A9F4EC1D26B732515A3430728E82D78FD7` (trial cert, expires 2026-09-05)
>
> **Do not** store `CertificatePassword` in `appsettings.json` or `web.config` — passwords containing
> `{` or `}` are silently corrupted by ASP.NET Core config token substitution in all delivery mechanisms.

---

## Step 6 — First-Run Smoke Test (Console Mode)

Start the application as a console process before registering it as a service:

```powershell
# On SRXWEBAPP1
cd "C:\inetpub\wwwroot\MyInvois"
$env:ASPNETCORE_ENVIRONMENT = "UAT"
.\MyInvois.Api.exe
```

Watch for startup errors. Expected log output (Serilog JSON to console):
```
[INF] [] Now listening on: http://localhost:5051
[INF] [] DailyBatchHostedService started. Next run at 02:00.
[INF] [] Application started.
```

If you see `audit.db` creation messages, EF Core is initialising the database — that is expected.

---

## Step 7 — Manual Batch Trigger Test

With the app running (Step 6), open a second PowerShell window on SRXWEBAPP1:

```powershell
$apiKey   = "<ApiKeys:Primary from appsettings.UAT.json>"
$yesterday = (Get-Date).AddDays(-1).ToString("yyyy-MM-dd")
$today     = (Get-Date).ToString("yyyy-MM-dd")

$response = Invoke-RestMethod `
    -Uri "http://localhost:5051/api/v1/batch/process-range" `
    -Method POST `
    -Headers @{ "X-API-Key" = $apiKey } `
    -ContentType "application/json" `
    -Body (@{ fromDate = $yesterday; toDate = $today } | ConvertTo-Json)

$response | ConvertTo-Json
```

Expected response:
```json
{
  "totalInvoices": 12,
  "successCount": 12,
  "failedCount": 0,
  "skippedCount": 0,
  "durationMs": 14200
}
```

Check the audit DB immediately after:
```powershell
$db = "E:\data\audit.db"
sqlite3 $db "SELECT InvoiceNumber, Status, MyInvoisStatus, MyInvoisUUID FROM AuditLogs ORDER BY Timestamp DESC LIMIT 10;"
```

> **Table is `AuditLogs`, timestamp column is `Timestamp`.** Earlier revisions of this runbook
> referenced `SubmissionAuditLog` / `SubmittedAt`, which do not exist — those queries fail with
> "no such table". Column names are defined in `src/MyInvois.Service/Data/AuditLogEntity.cs`.

---

## Step 8 — Confirm LHDN Pre-Prod Portal Shows Submissions

Log in to the LHDN pre-prod portal and search for one of the submitted invoice numbers.

- Status immediately after submission: **Submitted**
- Status after 2–5 minutes (Step 08 async validator): **Valid**

> **HTTP 200 does not mean the invoice is valid.** Step 08 runs asynchronously 2–5 minutes later.
> Always confirm **Valid** status in the portal before declaring the batch successful.

> **CF321 (date too old):** AP invoices carrying old supplier issue dates may receive this error
> in pre-prod. This is an environmental constraint only — production submissions close to the
> issue date will not trigger it.

---

## Step 9 — Configure IIS App Pool for Persistent Scheduling

The `DailyBatchHostedService` is an in-process `BackgroundService` that must stay alive until 02:00 AM.
IIS has a default **idle timeout of 20 minutes** — if no HTTP requests arrive, IIS shuts down the worker
process, killing the scheduler before it fires.

> **This was confirmed in UAT (2026-06-03):** app started at 12:33, manual batch triggered at 12:34,
> then silence → IIS killed the process at 12:54 (exactly 20 minutes). Overnight batch never ran.

**Disable idle timeout and periodic recycling:**

```powershell
# Run on SRXWEBAPP1 as Administrator
& "$env:windir\system32\inetsrv\appcmd.exe" set apppool "MyInvoisAPI" `
    /processModel.idleTimeout:"00:00:00"

& "$env:windir\system32\inetsrv\appcmd.exe" set apppool "MyInvoisAPI" `
    /recycling.periodicRestart.time:"00:00:00"

# Verify
& "$env:windir\system32\inetsrv\appcmd.exe" list apppool "MyInvoisAPI" /processModel.idleTimeout
& "$env:windir\system32\inetsrv\appcmd.exe" list apppool "MyInvoisAPI" /recycling.periodicRestart.time
```

Or via IIS Manager:
- Application Pools → MyInvoisAPI → Advanced Settings
- **Idle Time-out (minutes):** `0`
- **Regular Time Interval (minutes):** `0`

**Enable Always Running (optional but recommended):**

In IIS Manager → Application Pools → MyInvoisAPI → Advanced Settings:
- **Start Mode:** `AlwaysRunning` (starts the worker process immediately on IIS start, before any request)

**Verify the scheduler is armed after recycling:**

```powershell
# Check stdout log for scheduler startup message
Get-Content "C:\inetpub\wwwroot\MyInvois-Api\logs\stdout*.log" |
    Select-String "DailyBatch" | Select-Object -Last 5
# Expected: [DailyBatch] Next run in XXX minutes (02:00 local)
```

> **Rule:** Any ASP.NET Core app hosting a `BackgroundService` scheduler under IIS must have
> idle timeout = 0 and periodic recycling = 0. Otherwise the scheduler is silently killed.

---

## Step 10 — Verify the Overnight Batch (Next Morning)

After the 02:00 AM scheduled run:

```powershell
$db = "E:\data\audit.db"

# Summary of overnight batch — Status is the sync (Step 4) result
sqlite3 $db @"
SELECT Status, COUNT(*) AS Count
FROM AuditLogs
WHERE Timestamp > datetime('now', '-10 hours')
GROUP BY Status;
"@

# Step 8 async validation outcome — this is the one that matters
sqlite3 $db @"
SELECT COALESCE(MyInvoisStatus, '(not polled)') AS Step8Status, COUNT(*) AS Count
FROM AuditLogs
WHERE Timestamp > datetime('now', '-10 hours')
GROUP BY MyInvoisStatus;
"@

# Any failures?
sqlite3 $db @"
SELECT InvoiceNumber, StatusCode, MyInvoisStatus, ErrorMessage
FROM AuditLogs
WHERE Status = 'Failed'
  AND Timestamp > datetime('now', '-10 hours');
"@
```

> **`Status` vs `MyInvoisStatus`.** `Status` is LHDN's synchronous Step 4 acceptance.
> `MyInvoisStatus` is the asynchronous Step 8 validation result, polled ~5 minutes after the
> batch. An invoice can be `Status='Success'` and `MyInvoisStatus='Invalid'` — LHDN accepted the
> submission then rejected it on validation. When Step 8 returns `Invalid`, the service sets
> `Status='Failed'` so the invoice can be corrected and resubmitted (duplicate detection only
> blocks re-submission of `Status='Success'` rows).

---

## Triggering a Manual Catch-Up Batch

If the daily batch was missed (service was down), re-run for any date range:

```powershell
$apiKey = "<ApiKeys:Primary>"

Invoke-RestMethod `
    -Uri "http://localhost:5051/api/v1/batch/process-range" `
    -Method POST `
    -Headers @{ "X-API-Key" = $apiKey } `
    -ContentType "application/json" `
    -Body (@{
        fromDate = "2026-05-20"
        toDate   = "2026-05-24"
    } | ConvertTo-Json)
```

The service has built-in duplicate detection via the audit log (`IsAlreadySubmittedAsync`) —
re-running for an already-processed date range will skip already-submitted invoices.

---

## Running the Smoke Test (Developers Only)

The smoke test (`Category=Smoke`) hits the real DB2 and real LHDN pre-prod endpoint.
It requires user secrets set on the **test project** (secrets ID: `myinvois-service-smoketest`):

```powershell
cd "c:\Projects\MyInvois-Service"

dotnet user-secrets --project tests/MyInvois.Service.Tests `
    set "MovexDb:ConnectionString" "DSN=MOVEX_AS400;UID=<user>;PWD=<password>;"

dotnet user-secrets --project tests/MyInvois.Service.Tests `
    set "MyInvoisApi:ClientId" "&lt;pre-prod ClientId — from IT Ops&gt;"

dotnet user-secrets --project tests/MyInvois.Service.Tests `
    set "MyInvoisApi:ClientSecret" "<secret>"

dotnet user-secrets --project tests/MyInvois.Service.Tests `
    set "MyInvoisApi:CertificatePassword" "<cert password>"

dotnet user-secrets --project tests/MyInvois.Service.Tests `
    set "ConnectionStrings:AuditLog" "Data Source=./data/audit.db"
```

Run the smoke test:
```powershell
dotnet test tests/MyInvois.Service.Tests `
    --filter "Category=Smoke" `
    --logger "console;verbosity=detailed"
```

Run all other tests (excludes smoke):
```powershell
dotnet test --filter "Category!=Smoke"
# Expected: Passed! Failed: 0, Passed: 336
```

---

## Configuration Reference

### Where each setting lives

| Setting | File | Notes |
|---------|------|-------|
| Logging, MovexDb defaults, MyInvoisApi defaults, Companies, ForeignPartyDefaults | `appsettings.json` (root) | Committed to source; safe defaults only |
| Dev overrides (debug log, dev SQLite path, ArMinYear) | `appsettings.Development.json` | Committed; dev only |
| ApiKeys, BatchScheduler, AllowedHosts | `src/MyInvois.Api/appsettings.json` | Committed; empty ApiKeys (filled by secrets) |
| All secrets + UAT/prod overrides | `appsettings.UAT.json` / `appsettings.Production.json` | **Never commit** — on server only |

### BatchScheduler settings (in `appsettings.UAT.json`)

| Key | Value | Effect |
|-----|-------|--------|
| `Enabled` | `true` | Daily batch runs automatically |
| `Enabled` | `false` | Daily batch disabled; manual trigger still works |
| `DailyRunHour` | `2` | Fires at 02:00 server local time |
| `LookbackDays` | `1` | Processes yesterday's invoices |
| `LookbackDays` | `7` | Catch-up: processes last 7 days |

### LHDN API endpoints (pre-prod)

Both token and submission use the **same host** — `preprod-api.myinvois.hasil.gov.my`:

| Endpoint | Path |
|----------|------|
| OAuth token | `POST /connect/token` |
| Submit invoice | `POST /api/v1.0/documentsubmissions` |
| Document status | `GET /api/v1.0/documents/{uuid}/details` |

> `sandbox.myinvois.*` is browser-only (App Proxy). `identity.myinvois.*` does not exist for M2M.

---

## Monitoring & Audit Log

### Daily health check

```powershell
$db = "E:\data\audit.db"

# Certificate days remaining — read from the store, no password needed
$thumb = "<configured CertificateThumbprint>"
$cert  = Get-ChildItem "Cert:\LocalMachine\My\$thumb"
Write-Host "Cert expires in: $(($cert.NotAfter - (Get-Date)).Days) days  ($($cert.NotAfter))"

# Audit DB accessible + WAL mode
sqlite3 $db "PRAGMA journal_mode;"           # Expected: wal
sqlite3 $db "SELECT COUNT(*) FROM AuditLogs;"

# Failures in last 24 hours (includes Step 8 rejections — those set Status='Failed')
sqlite3 $db @"
SELECT COUNT(*) AS Failures
FROM AuditLogs
WHERE Status = 'Failed'
  AND Timestamp > datetime('now', '-24 hours');
"@

# Anything accepted but never Step 8 polled (should be zero after the polling window)
sqlite3 $db @"
SELECT COUNT(*) AS AwaitingStep8
FROM AuditLogs
WHERE Status = 'Success' AND MyInvoisStatus = 'Submitted'
  AND Timestamp > datetime('now', '-24 hours');
"@
```

### Monthly summary

```powershell
$db = "E:\data\audit.db"
sqlite3 $db @"
SELECT strftime('%Y-%m', Timestamp) AS Month,
       COUNT(*) AS Total,
       SUM(CASE WHEN MyInvoisStatus='Valid'   THEN 1 ELSE 0 END) AS Step8Valid,
       SUM(CASE WHEN MyInvoisStatus='Invalid' THEN 1 ELSE 0 END) AS Step8Invalid,
       SUM(CASE WHEN Status='Failed' THEN 1 ELSE 0 END) AS Failed,
       ROUND(SUM(CASE WHEN MyInvoisStatus='Valid' THEN 1.0 ELSE 0 END) * 100 / COUNT(*), 1) AS ValidRate
FROM AuditLogs
WHERE Action = 'MyInvois_Submit'
GROUP BY Month
ORDER BY Month DESC;
"@
```

---

## Backup & Disaster Recovery

### SQLite audit log — daily backup

The `audit.db` file (plus WAL journal) must be included in the server's daily backup to
satisfy the 7-year LHDN/ISO 27001 retention requirement.

```powershell
# Schedule: daily at 22:30 via Task Scheduler
$auditDbDir = "C:\inetpub\wwwroot\MyInvois\data"
$backupDest = "\\backup-server\MyInvois\SQLiteAudit"
$dateSuffix = Get-Date -Format 'yyMMdd'

New-Item -ItemType Directory -Force "$backupDest\$dateSuffix" | Out-Null

# SQLite online backup (safe even while app is writing)
sqlite3 "$auditDbDir\audit.db" ".backup '$backupDest\$dateSuffix\audit_$dateSuffix.db'"

Write-Host "Backup complete: $backupDest\$dateSuffix\audit_$dateSuffix.db"
```

Files to include in every backup:
- `audit.db` — primary database
- `audit.db-wal` — WAL journal (data loss risk if omitted)
- `audit.db-shm` — shared memory (recommended)

### Certificate backup (encrypted, quarterly)

Back up **all three production files** — the `.p12` alone is not sufficient to rebuild the server.
Without the two CA certificates the chain cannot be reconstructed from backup.

```powershell
$sourceDir = "C:\Certs\MyInvois\Prod"       # .p12 + intermediate .cer + root .cer
$backup    = "\\backup-server\Certificates\MyInvois\myinvois-prod-chain.BACKUP.7z"
$archPass  = "<backup archive password — different from cert password>"

7z a -tzip -mem=AES256 -p$archPass "$backup" "$sourceDir\*"
7z l "$backup"   # Verify all three files are listed
```

Store the archive password and the `.p12` password separately from the archive itself, in the
organisation's credential store. Record the certificate **thumbprint** alongside them — it is
needed for config and is not secret.

---

## Rollback Procedure

### Stop the service

```powershell
Stop-Service -Name "MyInvois-UAT" -Force
```

### Restore previous build

```powershell
# Copy previous publish artifact back to the server
Copy-Item "\\backup-server\MyInvois\builds\previous\*" `
    "C:\inetpub\wwwroot\MyInvois\" -Force
```

### Restore audit DB (if corrupted)

```powershell
$db     = "E:\data\audit.db"
$backup = "\\backup-server\MyInvois\SQLiteAudit\{yyMMdd}\audit_{yyMMdd}.db"

Stop-Service "MyInvois-UAT"
Copy-Item $backup $db -Force
Start-Service "MyInvois-UAT"
```

---

---

# Part B — Production Go-Live

**Status:** UAT signed off 2026-08-11; go-live approved.

This part is self-contained. Work through B1 → B8 in order. Steps B1–B4 are preparation and can
be done ahead of the cutover window; B5 onward changes live behaviour.

> **The single most important difference from UAT:** you now have a **certificate chain**, not
> just a `.p12`. The two extra `.cer` files must be imported into the correct stores or LHDN will
> reject signatures it cannot chain to a trusted root.

---

## B0 — What You Received, and Where Each File Goes

Finance supplied three files plus a password:

| File | What it is | Destination | Goes in config? |
|---|---|---|---|
| `*.p12` | Leaf certificate **+ private key** — signs documents | `LocalMachine\My` (Personal) | Yes — by thumbprint |
| `*.cer` (one of two) | **Intermediate CA** — chain link | `LocalMachine\CA` (Intermediate CAs) | **No** |
| `*.cer` (other) | **Root CA** — trust anchor | `LocalMachine\Root` (Trusted Root CAs) | **No** |

**The `.cer` files are never referenced by the application and are never embedded in the signed
document.** The signing code sends only the leaf certificate — a single `X509Certificate` element
built from the leaf's raw bytes (`UblDocumentBuilder.BuildUblExtensions`). This is correct per LHDN
SDK v1.5 and does not change for production. The CA certificates exist so Windows — and LHDN's
validator — can build a trusted chain from your leaf up to a root they recognise.

**Identify which `.cer` is which** before importing:

```powershell
Get-ChildItem "C:\Certs\MyInvois\Prod\*.cer" | ForEach-Object {
    $c = Get-PfxCertificate -FilePath $_.FullName
    [PSCustomObject]@{
        File       = $_.Name
        Subject    = $c.Subject
        Issuer     = $c.Issuer
        SelfSigned = ($c.Subject -eq $c.Issuer)   # True  => ROOT
        Expires    = $c.NotAfter
    }
} | Format-List
```

- `SelfSigned = True` → **root CA** → `LocalMachine\Root`
- `SelfSigned = False` → **intermediate CA** → `LocalMachine\CA`

Sanity check: the intermediate's `Subject` should match the `.p12` leaf's `Issuer`, and the
intermediate's `Issuer` should match the root's `Subject`.

---

## B1 — Secure the Certificate Files

```powershell
# On the production server, as Administrator
New-Item -ItemType Directory -Force "C:\Certs\MyInvois\Prod"

# Copy the .p12 and both .cer files there, then lock the directory down
icacls "C:\Certs\MyInvois\Prod" /inheritance:r
icacls "C:\Certs\MyInvois\Prod" /grant "Administrators:(OI)(CI)F"
icacls "C:\Certs\MyInvois\Prod" /grant "SYSTEM:(OI)(CI)F"
icacls "C:\Certs\MyInvois\Prod"   # verify — no Users/Everyone entries
```

> **Never commit any of these files, or the password, to source control.** They belong on the
> server and in your organisation's secure credential store only.

---

## B2 — Import the Chain (Order Matters)

Import **root first, then intermediate, then the leaf**. Importing the leaf before its issuers
can leave Windows unable to build the chain until a refresh.

```powershell
# Run as Administrator on the production server

# 1. Root CA -> Trusted Root Certification Authorities
Import-Certificate -FilePath "C:\Certs\MyInvois\Prod\<root>.cer" `
    -CertStoreLocation Cert:\LocalMachine\Root

# 2. Intermediate CA -> Intermediate Certification Authorities
Import-Certificate -FilePath "C:\Certs\MyInvois\Prod\<intermediate>.cer" `
    -CertStoreLocation Cert:\LocalMachine\CA

# 3. Leaf + private key -> Personal
$pw = Read-Host -AsSecureString "Production .p12 password"
Import-PfxCertificate -FilePath "C:\Certs\MyInvois\Prod\<production>.p12" `
    -CertStoreLocation Cert:\LocalMachine\My `
    -Password $pw
```

The `Import-PfxCertificate` output includes the **thumbprint** — record it, you need it in B4.

### If `Import-PfxCertificate` rejects the file — use `certutil`

**This happened with the June 2026 Pos Digicert certificate and will likely recur at renewal.**

`Import-PfxCertificate` failed with:

```
The PFX file you are trying to import requires either a different password
or membership in an Active Directory principal to which it is protected.
```

The password was correct. `certutil` imported the same file with the same password without complaint — the cmdlet is stricter about certain PKCS#12 encryption profiles than the underlying CryptoAPI.

**First, confirm the password is genuinely correct** (this isolates a password problem from a cmdlet limitation):

```powershell
$pw = Read-Host -AsSecureString "Production .p12 password"
try {
    $c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
        "C:\Certs\MyInvois\Prod\<production>.p12", $pw, 'EphemeralKeySet')
    Write-Host "PASSWORD OK" -ForegroundColor Green
    $c | Format-List Subject, NotAfter, HasPrivateKey, Thumbprint
}
catch { Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red }
```

If that reports `PASSWORD OK`, fall back to `certutil`:

```powershell
# -enterprise My  targets LocalMachine\My (NOT the current user's store)
certutil -f -importpfx -enterprise -p "<password>" My "C:\Certs\MyInvois\Prod\<production>.p12"
# Expected: Certificate "<BRN>" added to store.
```

> **The password appears in plain text in your shell history.** Clear it immediately afterwards:
> ```powershell
> Clear-History
> Remove-Item (Get-PSReadlineOption).HistorySavePath -ErrorAction SilentlyContinue
> ```
> Prefer `Import-PfxCertificate` whenever it works; use `certutil` only as a fallback.

**Confirm it landed in the machine store, not the user store.** Without `-enterprise`, `certutil`
imports into `CurrentUser\My`, which the IIS app pool cannot read:

```powershell
Get-ChildItem Cert:\LocalMachine\My |
    Select-Object Thumbprint, NotAfter, HasPrivateKey,
                  @{n='DaysLeft';e={($_.NotAfter - (Get-Date)).Days}} |
    Format-List
```

> **Use `Format-List`, not `Format-Table -AutoSize`.** The Subject on these certificates is long
> enough that `-AutoSize` silently drops the `NotAfter` and `HasPrivateKey` columns — which are
> precisely the two fields you are checking.

### Two certificates now share the same Subject

After importing production, both the trial and production certificates are in `LocalMachine\My`
with **identical Subject strings** (same CN, same BRN, same TIN). They differ only by thumbprint
and expiry.

| Certificate | Thumbprint | Expires |
|---|---|---|
| Trial (pre-prod) | `A0E772A9F4EC1D26B732515A3430728E82D78FD7` | 2026-09-05 |
| **Production** | `47A8CE0C681723F6F38D2D8A1BD54E61B97165A7` | **2029-06-12** |

**Always select by thumbprint, never by Subject match.** Any script using
`Where-Object { $_.Subject -like "*SRX*" }` now matches both and picks arbitrarily. The
application itself is safe — `MyInvoiceSubmitter.LoadCertificate` looks up strictly by
thumbprint — but ad-hoc operational scripts are not.

**Do not delete the trial certificate yet.** It is the fallback if you need to return to pre-prod.
Remove it only after production has run cleanly for several batches.

---

## B3 — Verify the Chain Builds (Do Not Skip)

This is the step that catches a misplaced or missing intermediate. A signature that fails to chain
is accepted at Step 4 and rejected at Step 8, minutes later.

```powershell
$thumb = "<production thumbprint from B2>"
$cert  = Get-ChildItem "Cert:\LocalMachine\My\$thumb"

# Basic facts
$cert | Format-List Subject, Issuer, NotBefore, NotAfter, HasPrivateKey, Thumbprint

# Chain validation
$chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
$built = $chain.Build($cert)
Write-Host "Chain builds: $built"
$chain.ChainElements | ForEach-Object { "  $($_.Certificate.Subject)" }
$chain.ChainStatus    | ForEach-Object { "  STATUS: $($_.Status) - $($_.StatusInformation)" }
```

**All of the following must hold before continuing:**

- [ ] `HasPrivateKey` = `True` — signing throws `"Certificate does not contain a private key"` otherwise
- [ ] Chain lists three elements: leaf → intermediate → root
- [ ] No `PartialChain` and no `UntrustedRoot` in `ChainStatus`
- [ ] `NotAfter` is comfortably in the future — record the date in B8

If `PartialChain` or `UntrustedRoot` appears, the intermediate or root landed in the wrong store.
Re-check B0's identification and re-import.

### `RevocationStatusUnknown` is expected — and is not a failure

On a server without outbound access to the CA's CRL/OCSP endpoints, `Build()` returns **`False`**
with:

```
STATUS: RevocationStatusUnknown - The revocation function was unable to check revocation
```

**This does not block signing.** It means only that Windows could not reach Pos Digicert to ask
whether the certificate has been revoked — it is a network result, not a trust defect. The
application never performs revocation checking when signing (`rsa.SignData`), and LHDN validates
the signature against its own trust list.

The statuses that *do* indicate a real problem are `PartialChain` and `UntrustedRoot`. Neither
should be present.

**Confirm the trust path independently** by suppressing only the revocation check:

```powershell
$chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
$chain.ChainPolicy.RevocationMode = 'NoCheck'
$built = $chain.Build($cert)

Write-Host "Chain builds (revocation ignored): $built"
$chain.ChainStatus | ForEach-Object { "  STATUS: $($_.Status)" }
if (-not $chain.ChainStatus) {
    Write-Host "  (no status entries - trust path is clean)" -ForegroundColor Green
}
```

`True` with no status entries means the trust path is sound. **That is the pass condition.**

**Verified result (2026-09-09 production import):**

```
Chain builds (revocation ignored): True
  (no status entries - trust path is clean)

  E=AP.Malaysia@srxglobal.com, SERIALNUMBER=<BRN>, CN=SRX GLOBAL (MALAYSIA) SDN. BHD., ...
  CN=LHDNM Sub CA G3, OU=Terms of use at http://www.posdigicert.com.my, O=LHDNM, C=MY
  CN=Pos Digicert Class 2 Root CA G3, OU=457608-K, O=Pos Digicert Sdn. Bhd., C=MY
```

`HasPrivateKey = True`, valid 2026-06-12 → **2029-06-12** (1008 days at import).

> **Optional hardening:** revocation checking is a genuine security control. If the server can be
> permitted outbound access to Pos Digicert's CRL/OCSP endpoints, allow it — you would then learn
> if the certificate were ever revoked. Not a go-live blocker. The endpoints are in the cert:
> ```powershell
> $cert.Extensions |
>     Where-Object { $_.Oid.FriendlyName -match 'CRL|Authority Information' } |
>     ForEach-Object { $_.Format($true) }
> ```

---

## B4 — Grant the App Pool Private-Key Access

Without this the certificate loads but signing fails with *"Keyset does not exist"*.

```powershell
certlm.msc
# Personal -> Certificates -> right-click the production certificate
# All Tasks -> Manage Private Keys -> Add
# Object name: IIS AppPool\MyInvoisAPI      (match your actual pool name)
# Permission:  Read   (Full Control is not required)
```

Confirm the pool name first if unsure:

```powershell
& "$env:windir\system32\inetsrv\appcmd.exe" list apppool
```

---

## B5 — Prepare `appsettings.Production.json`

Create on the production server, alongside the published files. **Never commit this file.**

```json
{
  "Logging": {
    "LogLevel": { "Default": "Information", "MyInvois": "Information" }
  },
  "ConnectionStrings": {
    "AuditLog": "Data Source=E:\\data\\audit.db"
  },
  "MovexDb": {
    "ConnectionString": "<PRODUCTION MOVEX ODBC connection string>",
    "ActiveCompanyCodes": [ "100" ],
    "ArMinYear": 2025
  },
  "MyInvoisApi": {
    "BaseUrl": "https://api.myinvois.hasil.gov.my",
    "IdentityBaseUrl": "https://api.myinvois.hasil.gov.my",
    "Environment": "production",
    "CertificateThumbprint": "<PRODUCTION THUMBPRINT FROM B2>",
    "CertificatePath": "",
    "CertificatePassword": "",
    "ClientId": "<PRODUCTION ClientId — NOT the pre-prod one>",
    "ClientSecret": "<PRODUCTION ClientSecret>"
  },
  "ApiKeys": {
    "Primary": "<regenerate with New-Guid — do not reuse the UAT key>",
    "Admin":   "<regenerate with New-Guid>"
  },
  "BatchScheduler": {
    "Enabled": false,
    "DailyRunHour": 2,
    "DailyRunMinute": 0,
    "LookbackDays": 1
  }
}
```

**Five things change together — the certificate alone is not enough:**

| Setting | UAT | Production |
|---|---|---|
| `BaseUrl` / `IdentityBaseUrl` | `preprod-api.myinvois.hasil.gov.my` | `api.myinvois.hasil.gov.my` |
| `Environment` | `preprod` | `production` |
| `CertificateThumbprint` | trial cert | production cert |
| `ClientId` / `ClientSecret` | pre-prod credentials | **production credentials** |
| `MovexDb.ActiveCompanyCodes` | `["100"]` if already prod data | `["100"]` (CONO 100 = production) |

> **`BatchScheduler:Enabled` starts `false` deliberately.** Enable it only after the single-invoice
> verification in B7 passes. This prevents the scheduler firing a full batch before the
> configuration is proven.

> **Clear `CertificatePath` and `CertificatePassword`.** `LoadCertificate` tries the thumbprint
> first and falls back to file path. With both the trial and production certificates now in
> `LocalMachine\My`, a stale path would silently sign production submissions with the **trial**
> key. Blank both fields.

### Production credentials are separate from pre-prod — they are NOT reusable

`ClientId` and `ClientSecret` are issued per environment. The pre-prod pair exists only in
`preprod-api.myinvois.hasil.gov.my`'s identity store; against the production host it fails with
`invalid_client` at token acquisition, before any submission is attempted.

Obtain them from the **production** MyInvois portal (`myinvois.hasil.gov.my`, not the pre-prod
portal — the two look nearly identical):

1. Log in with an account holding the **Director / Admin** role for the TIN (usually Finance)
2. **View Taxpayer Profile → Representatives / ERP System**
3. Register the ERP system, or open the existing registration
4. Generate **Client ID** and **Client Secret**

> The secret is displayed **once**. It cannot be retrieved later — regenerating invalidates the
> previous one. Capture it straight into the credential store.

**The ERP registration TIN must match the certificate.** The production certificate's subject
carries `OID.2.5.4.97=<TIN>`. A mismatch produces Step 8 signature failures that present as
certificate problems but are not.

**Test token acquisition in isolation before B7** — this separates credential, endpoint and
network problems from certificate and document problems:

```powershell
$body = @{
    client_id     = "<PRODUCTION ClientId>"
    client_secret = "<PRODUCTION ClientSecret>"
    grant_type    = "client_credentials"
    scope         = "InvoicingAPI"
}
try {
    $r = Invoke-RestMethod -Method POST `
        -Uri "https://api.myinvois.hasil.gov.my/connect/token" `
        -ContentType "application/x-www-form-urlencoded" -Body $body
    Write-Host "TOKEN OK - expires in $($r.expires_in)s" -ForegroundColor Green
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    $_.ErrorDetails.Message
}
```

`TOKEN OK - expires in 3600s` confirms credentials, endpoint and connectivity together.
*(Verified 2026-09-09.)*

### Verify `ActiveCompanyCodes` actually resolves to 100 only

**This is the highest-risk misconfiguration at go-live, and B7 will not reveal it.**

Base `appsettings.json` ships `"ActiveCompanyCodes": [ "100", "300" ]`. The .NET configuration
binder **merges arrays by index across layers rather than replacing them** — the behaviour is
documented on `MovexDbSettings.ActiveCompanyCodes` itself. An override of `[ "100" ]` therefore
risks resolving to `["100", "300"]`:

| Index | Base | Production override | Resolved |
|---|---|---|---|
| 0 | `100` | `100` | `100` |
| 1 | `300` | *(absent)* | **`300` survives** |

CONO 300 is **Development / UAT** data. If it survives the merge, production batches submit
UAT invoices to the live tax authority.

**Check what the application resolved — not what the file says.** `DirectQueryDataSource` logs the
company list on every fetch:

```powershell
Get-Content "C:\inetpub\wwwroot\MyInvois-Api\logs\stdout*.log" |
    Select-String "Companies:" | Select-Object -Last 5
```

- `Companies: 100` → correct, proceed
- `Companies: 100,300` → **stop**; the merge issue is live

If `300` persists, override via environment variables, which bind cleanly:

```powershell
[System.Environment]::SetEnvironmentVariable(
    "MovexDb__ActiveCompanyCodes__0", "100", [System.EnvironmentVariableTarget]::Machine)
[System.Environment]::SetEnvironmentVariable(
    "MovexDb__ActiveCompanyCodes__1", "",    [System.EnvironmentVariableTarget]::Machine)
```

Recycle the app pool and re-check the log before continuing.

Set the environment variable:

```powershell
[System.Environment]::SetEnvironmentVariable(
    "ASPNETCORE_ENVIRONMENT", "Production",
    [System.EnvironmentVariableTarget]::Machine)
```

Then recycle the app pool so it is picked up.

---

## B6 — Firewall and Connectivity

```powershell
# Production LHDN host must be reachable on 443
Test-NetConnection -ComputerName "api.myinvois.hasil.gov.my" -Port 443
```

- [ ] Outbound HTTPS to `api.myinvois.hasil.gov.my` allowed (this is a **different host** from pre-prod — firewall rules naming the pre-prod host will not cover it)
- [ ] Outbound ODBC to AS400 (port 446 or 8471) allowed
- [ ] The pre-prod host may remain allowed; it is no longer used

---

## B7 — First Production Submission (One Invoice)

**Submit exactly one invoice before enabling the scheduler.** Signature and chain problems surface
at Step 8, two to five minutes after an HTTP 200 — a full batch would multiply any error across
every invoice in it.

```powershell
$apiKey = "<ApiKeys:Primary from appsettings.Production.json>"
$day    = "<a date with exactly one known invoice, yyyy-MM-dd>"

Invoke-RestMethod `
    -Uri "http://localhost:5051/api/v1/batch/process-range" `
    -Method POST `
    -Headers @{ "X-API-Key" = $apiKey } `
    -ContentType "application/json" `
    -Body (@{ fromDate = $day; toDate = $day } | ConvertTo-Json)
```

Wait **at least 6 minutes** (the service polls Step 8 five minutes after submission), then:

```powershell
$db = "E:\data\audit.db"
sqlite3 $db @"
SELECT InvoiceNumber, Status, MyInvoisStatus, StatusCode, ErrorMessage
FROM AuditLogs
WHERE Action = 'MyInvois_Submit'
ORDER BY Timestamp DESC LIMIT 5;
"@
```

**Pass criteria — both must hold:**

- [ ] `Status` = `Success`
- [ ] `MyInvoisStatus` = **`Valid`** ← this is the real signal

| Observed | Meaning | Action |
|---|---|---|
| `MyInvoisStatus = 'Valid'` | Signature, chain and content all accepted | Proceed to B8 |
| `MyInvoisStatus = 'Invalid'`, code `DS320`/`DS322` | Signature or digest problem | Re-run B3; check the production cert chains fully. Do **not** enable the scheduler |
| `MyInvoisStatus = 'Submitted'` after 6+ min | Step 8 poll did not complete | Check logs for polling errors; verify UUID in the LHDN production portal manually |
| `Status = 'Failed'` at submission | Rejected at Step 4 | Read `StatusCode`/`ErrorMessage` — usually credentials or endpoint, not the certificate |

Cross-check the same invoice in the **production** LHDN portal (not pre-prod).

> **Worth doing while you are here:** if any invoice in this first run contains an ampersand,
> apostrophe, `+`-prefixed phone number, or an accented character in a party name, note it. The
> DS322 encoder fix (ADR-018) is only genuinely exercised by such an invoice — a run of plain-ASCII
> invoices proves nothing about it.

---

## B8 — Enable the Scheduler and Hand Over

Only after B7 shows `MyInvoisStatus = 'Valid'`:

1. Set `"BatchScheduler": { "Enabled": true }` in `appsettings.Production.json`
2. Recycle the app pool
3. Confirm the scheduler armed:

```powershell
Get-Content "C:\inetpub\wwwroot\MyInvois-Api\logs\stdout*.log" |
    Select-String "DailyBatch" | Select-Object -Last 5
# Expected: [DailyBatch] Next run in XXX minutes (02:00 local)
```

4. Verify IIS idle timeout and periodic recycling are both `0` — see [Step 9](#step-9--configure-iis-app-pool-for-persistent-scheduling). **A production server that has never hosted this app will have the 20-minute default, which silently kills the scheduler.**

**Record and diarise:**

- [ ] Production certificate expiry date: `________________`
- [ ] Renewal reminder set **60 days** before expiry
- [ ] Production thumbprint recorded in the secure credential store
- [ ] `.p12` and both `.cer` files backed up (encrypted) — see [Certificate backup](#certificate-backup-encrypted-quarterly)
- [ ] Finance informed that live submissions have begun

---

## B9 — Production Rollback

If the first production batch goes wrong:

1. **Stop further submissions immediately** — set `BatchScheduler:Enabled = false`, recycle the pool. This is the fastest containment; it does not require a redeploy.
2. **Documents already submitted cannot be un-submitted.** Invalid ones must be cancelled or corrected through the LHDN portal by Finance, within LHDN's cancellation window.
3. **Identify the blast radius:**

```powershell
sqlite3 "E:\data\audit.db" @"
SELECT InvoiceNumber, MyInvoisUUID, MyInvoisStatus, ErrorMessage
FROM AuditLogs
WHERE Timestamp > datetime('now', '-6 hours')
  AND (Status = 'Failed' OR MyInvoisStatus = 'Invalid');
"@
```

4. Invoices whose Step 8 returned `Invalid` are set to `Status='Failed'`, so they are **eligible for resubmission** once corrected — duplicate detection only blocks `Status='Success'`.
5. Reverting to the pre-prod endpoint is a config change only (B5 values), no redeploy needed.

---

## Go-Live Checklist (Consolidated)

**Preparation**
- [x] Production `.p12` + both `.cer` files received and identified (B0)
- [x] Files secured on server with restricted ACL (B1)
- [x] Certificate chain imported — `certutil` fallback required (B2)
- [x] Chain builds cleanly; `HasPrivateKey = True`; expires 2029-06-12 (B3)
- [ ] App pool granted private-key Read access (B4)
- [x] Production `ClientId` / `ClientSecret` obtained and token verified (B5)
- [ ] `appsettings.Production.json` prepared, scheduler **disabled** (B5)
- [ ] **`ActiveCompanyCodes` confirmed as `100` only in the resolved log output** (B5)
- [ ] `ASPNETCORE_ENVIRONMENT=Production` set (B5)
- [x] Outbound HTTPS to `api.myinvois.hasil.gov.my:443` confirmed (token call succeeded)
- [ ] `E:\data` exists with app-pool write access
- [ ] `BatchScheduler:LookbackDays` confirmed with Finance

**Cutover**
- [ ] Single invoice submitted; `MyInvoisStatus = 'Valid'` confirmed (B7)
- [ ] That invoice contained `&`, `'`, `+` or a non-ASCII character — otherwise the DS322 encoder fix (ADR-018) remains unexercised in production
- [ ] Same invoice verified in the production LHDN portal
- [ ] Scheduler enabled and armed (B8)
- [ ] IIS idle timeout = 0 and periodic recycling = 0 (B8 / Step 9)

**Post go-live**
- [ ] Certificate expiry diarised with 60-day reminder
- [ ] Certificate and audit DB backups configured
- [ ] First overnight batch reviewed the following morning (Step 10)
- [ ] Finance sign-off on the first production batch

**Known open items at go-live**
- [ ] Manual miscellaneous invoices are still submitted — no exclusion filter exists yet. Discriminator unidentified; see `src/Database/Diagnostics/AR_Manual_Miscellaneous_Invoice_Profiling.sql`. **Finance should expect these until a rule is agreed.**
- [ ] Genuine AR credit notes would not be submitted under the current `ESTRCD='10'` filter (ADR-019). No such document exists in three years of data, but the business process is unconfirmed.

---

## Related Documents

- [SETUP.md](SETUP.md) — Local developer setup
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) — Common issue diagnosis
- [SETUP_USER_SECRETS.md](SETUP_USER_SECRETS.md) — Smoke test secrets setup
- [ai/memory/01-system-architecture.md](../ai/memory/01-system-architecture.md) — Architecture reference
- [ai/evidence/decision-log.md](../ai/evidence/decision-log.md) — ADR history

---

**Deployment Owner:** Hector Salazar (Development & Integration Lead)
**Last Updated:** 2026-08-11
**Next Review:** After first production batch

### Change history

| Version | Date | Change |
|---|---|---|
| 3.1 | 2026-09-09 | Recorded the actual production import: `certutil -importpfx` fallback when `Import-PfxCertificate` rejects a valid file; `RevocationStatusUnknown` explained as expected, with the `NoCheck` pass condition; two co-resident certificates sharing one Subject (select by thumbprint only); production credentials are not reusable from pre-prod, with an isolated token test; `ActiveCompanyCodes` array-merge warning. |
| 3.0 | 2026-08-11 | Added Part B (production go-live: certificate chain, endpoint/credential switch, single-invoice verification, rollback). Corrected all audit queries — table is `AuditLogs`/`Timestamp`, not `SubmissionAuditLog`/`SubmittedAt`. Added Step 8 (`MyInvoisStatus`) monitoring. Updated cert backup to cover the full chain. |
| 2.0 | 2026-05-27 | UAT deployment workflow, IIS idle-timeout fix, Windows Store certificate loading |
