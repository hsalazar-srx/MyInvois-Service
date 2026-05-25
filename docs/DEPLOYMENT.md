# MyInvois-Service — Deployment Runbook

**Target Audience:** IT Operations / Development Lead
**Version:** 2.0
**Last Updated:** 2026-05-25
**Status:** UAT Active (pre-prod LHDN endpoint)

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

- [ ] Trial cert file present: `C:\Certs\MyInvois\SRX_GLOBAL_(MALAYSIA)_SDN._BHD..p12`
- [ ] Trial cert expiry confirmed: **2026-09-05** (production cert required before go-live)
- [ ] NTFS ACL on `C:\Certs\MyInvois\` restricted to service account + Administrators
- [ ] EFS encryption enabled on `C:\Certs\MyInvois\`

### Data directory

- [ ] `E:\data\` exists
- [ ] App pool identity (`IIS_IUSRS` or dedicated pool) has **write** access to that directory
- [ ] `audit.db` will be created automatically on first startup — do NOT create manually

---

## Step 1 — Build the Release Package

Run from your development machine:

```powershell
cd "c:\Projects\MyInvois-Service"

# 1a. Confirm all 276 tests pass before building
dotnet test --configuration Release --filter "Category!=Smoke"
# Expected: Passed! Failed: 0, Passed: 276

# 1b. Publish
dotnet publish src/MyInvois.Api/MyInvois.Api.csproj `
    --configuration Release `
    --output ".\publish\uat\" `
    --runtime win-x64 `
    --self-contained false

# 1c. Verify output
Get-ChildItem ".\publish\uat\" | Select-Object Name, Length
# Must include: MyInvois.Api.exe, MyInvois.Api.dll, appsettings.json
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
    "ClientId": "a777bc19-e8b9-4adb-b793-7c8b64368a5a",
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

## Step 5 — Verify the Certificate

Run on SRXWEBAPP1:

```powershell
$certPath = "C:\Certs\MyInvois\SRX_GLOBAL_(MALAYSIA)_SDN._BHD..p12"
$certPass = "<certificate password>"

Test-Path $certPath   # Expected: True

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
    $certPath, $certPass)

Write-Host "Subject:      $($cert.Subject)"
Write-Host "Expires:      $($cert.NotAfter)   (trial cert: 2026-09-05)"
Write-Host "Has Priv Key: $($cert.HasPrivateKey)"   # Must be True
Write-Host "Days left:    $(($cert.NotAfter - (Get-Date)).Days)"
```

If `Has Priv Key` is `False`, the certificate file is corrupt or missing the private key — contact Finance.

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
[INF] [] Now listening on: http://localhost:5000
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
    -Uri "http://localhost:5000/api/v1/batch/process-range" `
    -Method POST `
    -Headers @{ "X-Api-Key" = $apiKey } `
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
sqlite3 $db "SELECT InvoiceNumber, Status, MyInvoisUUID FROM SubmissionAuditLog ORDER BY SubmittedAt DESC LIMIT 10;"
```

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

## Step 9 — Register as Windows Service (Persistent)

Stop the console process from Step 6, then register the service:

```powershell
# Run on SRXWEBAPP1 as Administrator
New-Service `
    -Name        "MyInvois-UAT" `
    -BinaryPathName '"C:\inetpub\wwwroot\MyInvois\MyInvois.Api.exe"' `
    -DisplayName "MyInvois UAT Service" `
    -StartupType Automatic

# Set environment so the UAT appsettings loads
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\MyInvois-UAT"
New-ItemProperty -Path $regPath -Name "Environment" -PropertyType MultiString `
    -Value "ASPNETCORE_ENVIRONMENT=UAT" -Force

Start-Service -Name "MyInvois-UAT"
Get-Service  -Name "MyInvois-UAT"   # Expected: Running
```

Verify the daily scheduler is armed:
```powershell
# Should appear in the service's Serilog output / Event Log
Get-EventLog -LogName Application -Source "MyInvois*" -Newest 20 -ErrorAction SilentlyContinue
```

---

## Step 10 — Verify the Overnight Batch (Next Morning)

After the 02:00 AM scheduled run:

```powershell
$db = "E:\data\audit.db"

# Summary of overnight batch
sqlite3 $db @"
SELECT Status, COUNT(*) AS Count
FROM SubmissionAuditLog
WHERE SubmittedAt > datetime('now', '-10 hours')
GROUP BY Status;
"@

# Any failures?
sqlite3 $db @"
SELECT InvoiceNumber, ErrorCode, ErrorMessage
FROM SubmissionAuditLog
WHERE Status = 'Failed'
  AND SubmittedAt > datetime('now', '-10 hours');
"@
```

---

## Triggering a Manual Catch-Up Batch

If the daily batch was missed (service was down), re-run for any date range:

```powershell
$apiKey = "<ApiKeys:Primary>"

Invoke-RestMethod `
    -Uri "http://localhost:5000/api/v1/batch/process-range" `
    -Method POST `
    -Headers @{ "X-Api-Key" = $apiKey } `
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
    set "MyInvoisApi:ClientId" "a777bc19-e8b9-4adb-b793-7c8b64368a5a"

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
# Expected: Passed! Failed: 0, Passed: 276
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

# Certificate days remaining
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
    "C:\Certs\MyInvois\SRX_GLOBAL_(MALAYSIA)_SDN._BHD..p12", "<password>")
Write-Host "Cert expires in: $(($cert.NotAfter - (Get-Date)).Days) days"

# Service running?
Get-Service -Name "MyInvois-UAT"

# Audit DB accessible + WAL mode
sqlite3 $db "PRAGMA journal_mode;"           # Expected: wal
sqlite3 $db "SELECT COUNT(*) FROM SubmissionAuditLog;"

# Failures in last 24 hours
sqlite3 $db @"
SELECT COUNT(*) AS Failures
FROM SubmissionAuditLog
WHERE Status = 'Failed'
  AND SubmittedAt > datetime('now', '-24 hours');
"@
```

### Monthly summary

```powershell
$db = "E:\data\audit.db"
sqlite3 $db @"
SELECT strftime('%Y-%m', SubmittedAt) AS Month,
       COUNT(*) AS Total,
       SUM(CASE WHEN Status='Success' THEN 1 ELSE 0 END) AS Success,
       SUM(CASE WHEN Status='Failed'  THEN 1 ELSE 0 END) AS Failed,
       ROUND(SUM(CASE WHEN Status='Success' THEN 1.0 ELSE 0 END) * 100 / COUNT(*), 1) AS SuccessRate
FROM SubmissionAuditLog
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

```powershell
$source   = "C:\Certs\MyInvois\SRX_GLOBAL_(MALAYSIA)_SDN._BHD..p12"
$backup   = "\\backup-server\Certificates\MyInvois\myinvois-cert.BACKUP.7z"
$archPass = "<backup archive password — different from cert password>"

7z a -tzip -mem=AES256 -p$archPass "$backup" "$source"
7z l "$backup"   # Verify
```

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

## Go-Live Checklist (Production — After UAT Sign-Off)

These items are **not yet completed** — UAT is the current phase.

- [ ] Production digital certificate received from Finance (not trial cert)
- [ ] Production cert tested in pre-prod before switching
- [ ] `appsettings.Production.json` prepared with production LHDN endpoint (`api.myinvois.hasil.gov.my`)
- [ ] `ASPNETCORE_ENVIRONMENT=Production` set on production server
- [ ] `BatchScheduler:LookbackDays` confirmed with Finance (how far back to submit)
- [ ] SQL Server audit mirror evaluated (if single-instance SQLite becomes a concern)
- [ ] Finance sign-off on UAT results (success rate ≥ 95%)
- [ ] Submission window agreed with Finance (avoid month-end close conflict)

---

## Related Documents

- [SETUP.md](SETUP.md) — Local developer setup
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) — Common issue diagnosis
- [SETUP_USER_SECRETS.md](SETUP_USER_SECRETS.md) — Smoke test secrets setup
- [ai/memory/01-system-architecture.md](../ai/memory/01-system-architecture.md) — Architecture reference
- [ai/evidence/decision-log.md](../ai/evidence/decision-log.md) — ADR history

---

**Deployment Owner:** Hector Salazar (Development & Integration Lead)
**Last Updated:** 2026-05-25
**Next Review:** After UAT sign-off / production go-live
