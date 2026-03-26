# MyInvois-Service — Local Setup Guide

**Duration:** 5-10 minutes  
**Prerequisites:** .NET 8.0 SDK, IBM DB2 iSeries Access ODBC driver, Windows domain account

---

## Step 1: Clone Repository, Restore & Enable Hooks

```bash
git clone https://github.com/hsalazar-srx/MyInvois-Service.git
cd MyInvois-Service
dotnet restore
```

**Expected:** No errors, all packages restored.

### Activate Git Hooks (one-time per developer)

The repo ships with a pre-commit hook that enforces the **Skills-First Architecture** rule
(blocks `src/` commits unless `ai/memory/00-skills-audit.md` exists).

```powershell
# Option A — automated setup script (recommended)
.\setup-hooks.ps1

# Option B — manual one-liner
git config core.hooksPath .githooks
```

**Verify** the hook is active:

```bash
git config --get core.hooksPath
# Expected: .githooks
```

> **Note:** The hook requires `powershell.exe` (Windows PowerShell 5) or `pwsh`
> (PowerShell Core 7+). Both are supported. See [.githooks/README.md](../.githooks/README.md)
> for details.

---

## Step 2: Configure User Secrets

Open PowerShell and run these commands:

### Set MOVEX DB2/AS400 Connection

```powershell
dotnet user-secrets set "MovexDb:ConnectionString" "Server=YOUR_AS400_SERVER;Database=YOUR_DB;UserID=YOUR_USER;Password=YOUR_PASSWORD;"
```

**Where to get it:** Ask IT Ops for AS400/DB2 credentials. MOVEX tables: `fpledg` (AP), `fsledg` (AR), `fgledg` (GL) on schemas `mvxcdta`/`mvxc300`.

### Set MyInvois Credentials

```powershell
dotnet user-secrets set "MyInvoisApi:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "MyInvoisApi:ClientSecret" "YOUR_CLIENT_SECRET"
```

**Where to get it:** MyInvois sandbox portal (myinvois.hasil.gov.my)

### Set Audit Log SQLite Path (optional)

The audit database defaults to `./data/audit.db` relative to the application's content root. Override only if you need a specific path:

```powershell
dotnet user-secrets set "ConnectionStrings:AuditLog" "Data Source=C:\inetpub\wwwroot\MyInvois\data\audit.db"
```

For local development the default (`Data Source=./data/audit.db`) is already set in `appsettings.Development.json` — no secret needed.

### Set Certificate Password (Production Only)

For **production environments**, the certificate password must be stored securely:

#### Option A: Windows Credential Manager (Recommended)

```powershell
# Store certificate password in Credential Manager
cmdkey /add:MyInvoisCert /user:admin /pass:*

# System will prompt for password securely (press Enter, then paste password)
# Password is now stored in Windows Credential Locker (encrypted)

# Application retrieves it at runtime (never visible in memory):
$credManager = New-Object System.Net.NetworkCredential("MyInvoisCert",(Get-StoredCredential -Target 'MyInvoisCert').Password)
```

#### Option B: DPAPI (for encrypted config files)

```powershell
# Encrypt password using DPAPI
$password = "YourCertificatePassword"
$secureString = ConvertTo-SecureString $password -AsPlainText -Force
$encrypted = ConvertFrom-SecureString $secureString

# Store encrypted value in secure configuration file, decrypt at runtime
$decrypted = ConvertTo-SecureString $encrypted
```

### Verify Secrets

```powershell
dotnet user-secrets list
```

**Expected:** 4 secrets configured (MovexDb:ConnectionString, MyInvoisApi:ClientId, MyInvoisApi:ClientSecret, plus certificate password via Credential Manager). `ConnectionStrings:AuditLog` is only needed if overriding the default SQLite path.

---

## Step 3: Verify Audit Log Database (Auto-Created)

The SQLite audit database (`audit.db`) is **created automatically** on first startup via EF Core `EnsureCreated`. No manual schema setup is required.

### Verify After First Run

```powershell
# Check the file was created
Test-Path ".\data\audit.db"

# Verify WAL mode and row count using sqlite3.exe
$db = ".\data\audit.db"
& sqlite3 $db "PRAGMA journal_mode;"           # Expected: wal
& sqlite3 $db "SELECT COUNT(*) FROM AuditLogs;" # Expected: 0
```

> **Note:** `sqlite3.exe` can be downloaded from https://sqlite.org/download.html or installed via `winget install SQLite.SQLite`. It is only needed for manual inspection — the service itself does not require it.

---

## Step 3.5: Production Certificate Setup (Encrypted Server Storage)

> **NOTE:** This step is for **production server setup only**. Development can skip to Step 4.

### Create Encrypted Certificate Directory

Run as **Administrator** on the production server:

```powershell
# Create certificate directory
mkdir "C:\Certs\MyInvois"

# Restrict permissions (only service account + admins can access)
icacls "C:\Certs\MyInvois" /inheritance:r
icacls "C:\Certs\MyInvois" /grant:r "BUILTIN\Administrators:(OI)(CI)F"
icacls "C:\Certs\MyInvois" /grant:r "NT SERVICE\MyInvoisAppPool:(OI)(CI)RX"

# Verify permissions
icacls "C:\Certs\MyInvois" /T
```

**Expected output:** Only Administrators and MyInvoisAppPool listed (no "EVERYONE" or "USERS")

### Enable Windows EFS Encryption

```powershell
# Encrypt directory
cipher /e "C:\Certs\MyInvois"
cipher /s:"C:\Certs\MyInvois" /c /h

# Verify encryption
cipher /s:"C:\Certs\MyInvois"
```

**Expected:** Output shows encryption is enabled

### Copy Certificate File

```powershell
# Copy PFX from Finance-provided location (secure transfer)
copy "C:\Downloads\myinvois-cert.pfx" "C:\Certs\MyInvois\"

# Verify
dir "C:\Certs\MyInvois\myinvois-cert.pfx"
```

### Document Certificate Details

Create secure documentation file (`C:\Admin\Secrets\MyInvoisCertDetails.txt`):

```
Certificate Information (Encrypted File - Admin Only)
────────────────────────────────────────────────────
Location: C:\Certs\MyInvois\myinvois-cert.pfx
Issued By: [Certificate Authority Name]
Issued To: [Company Legal Name]
Thumbprint: [From Finance documentation]
Expiry Date: [From Finance documentation]
Created: [Today's date]
Stored by: [Your name]
```

### Test Certificate Access

```powershell
# Verify application service account can read certificate
$certPath = "C:\Certs\MyInvois\myinvois-cert.pfx"
$password = "CertificatePassword"  # From secure storage

Try {
  $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
    $certPath,
    $password
  )
  Write-Host "✓ Certificate loaded successfully"
  Write-Host "  Subject: $($cert.Subject)"
  Write-Host "  Thumbprint: $($cert.Thumbprint)"
  Write-Host "  Expires: $($cert.NotAfter)"
  Write-Host "  Has Private Key: $($cert.HasPrivateKey)"
}
Catch {
  Write-Host "✗ ERROR: Cannot access certificate"
  Write-Host "  Details: $_"
}
```

**Expected:**
```
✓ Certificate loaded successfully
  Subject: CN=MyCompany, O=Organization, C=MY
  Thumbprint: [40-character hex string]
  Expires: [Future date]
  Has Private Key: True
```

---

## Step 4: Test MOVEX DB2/AS400 Access

```powershell
# From MyInvois-Service folder
dotnet run -- --test-movex-db
```

**Expected:** Successful connection to DB2, sample query returns rows from MOVEX tables.

**Troubleshooting:**
- "Connection refused" → Check AS400 server is running, verify hostname and port
- "Authorization failure" → Check DB2 credentials in User Secrets (MovexDb:ConnectionString)
- "SQL0204N - table not found" → Verify schema names (mvxcdta/mvxc300) in MovexDb settings
- "Timeout" → Check network connectivity to AS400; increase CommandTimeoutSeconds

---

## Step 5: Test MyInvois Sandbox Access

```powershell
# From MyInvois-Service folder
dotnet run -- --test-myinvois-sandbox
```

**Expected:** OAuth token obtained, sandbox is accessible

**Troubleshooting:**
- ❌ "Invalid client" → Check ClientId/ClientSecret in User Secrets
- ❌ "Connection refused" → MyInvois API is down (check myinvois.hasil.gov.my status)

---

## Step 6: Run Local Service

```powershell
dotnet run --launch-profile Development
```

**Expected Output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5001
info: MyInvois.Service[0]
      Service started. Ready for batch processing.
```

**Test Endpoint:**
```powershell
curl http://localhost:5001/health
```

**Expected:** HTTP 200 OK

---

## Step 7: Configure and Test MyInvois.Api

`MyInvois.Api` is the internal HTTP API host that exposes invoice data to SM-Portal. It runs on `http://localhost:5051` (IIS-only; not exposed externally).

> **Prerequisites:** Complete Steps 1–4 first. MyInvois.Api shares the MOVEX DB2 connection but has its own User Secrets project (`src/MyInvois.Api/`).

### Step 7.1: Configure API Keys

API keys are **required** — all endpoints except `/api/v1/health` return `401 Unauthorized` without them.

Navigate to the Api project directory and set secrets:

```powershell
cd src/MyInvois.Api

# Primary key — used by SM-Portal and internal callers
dotnet user-secrets set "ApiKeys:Primary" "$(New-Guid)"

# Admin key — elevated access for operations/monitoring (optional but recommended)
dotnet user-secrets set "ApiKeys:Admin" "$(New-Guid)"

# MOVEX DB2 connection (same value as the service-level secret, scoped to this project)
dotnet user-secrets set "MovexDb:ConnectionString" "DSN=AS400;UID=YOUR_USER;PWD=YOUR_PASSWORD;"
```

> **Note:** `$(New-Guid)` generates a random GUID. Note down both values — SM-Portal will need `ApiKeys:Primary` configured as well.

**Verify secrets:**
```powershell
dotnet user-secrets list
# Expected: ApiKeys:Primary, ApiKeys:Admin, MovexDb:ConnectionString
```

### Step 7.2: Run MyInvois.Api Locally

```powershell
# From src/MyInvois.Api/
dotnet run
```

**Expected Output:**
```
[HH:mm:ss INF] [] Now listening on: http://localhost:5051
[HH:mm:ss INF] [] Application started.
```

### Step 7.3: Test Health Endpoint (No Auth Required)

The health endpoint is exempt from API key authentication — used by IIS application pool health checks.

```powershell
curl http://localhost:5051/api/v1/health
```

**Expected:** HTTP 200 OK
```json
{"status":"healthy","timestamp":"2026-03-18T..."}
```

**Failure cases:**
- Connection refused → Api is not running; check `dotnet run` output for startup errors
- HTTP 503 → `ApiKeys:Primary` not configured; run Step 7.1

### Step 7.4: Test Authenticated Invoice Request

All invoice endpoints require the `X-API-Key` header. Use the `ApiKeys:Primary` value set in Step 7.1.

```powershell
# Store key in variable (retrieve from user-secrets list output)
$apiKey = "YOUR_PRIMARY_KEY_HERE"

# Fetch invoices for a date range (both AP and AR)
curl -H "X-API-Key: $apiKey" `
     "http://localhost:5051/api/v1/invoices?fromDate=2026-01-01&toDate=2026-01-31&type=ALL"
```

**Expected:** HTTP 200 OK
```json
{
  "totalCount": 12,
  "fromDate": "2026-01-01",
  "toDate": "2026-01-31",
  "items": [...]
}
```

**Filter by type:**
```powershell
# AP invoices only
curl -H "X-API-Key: $apiKey" `
     "http://localhost:5051/api/v1/invoices?fromDate=2026-01-01&toDate=2026-01-31&type=AP"

# AR invoices only
curl -H "X-API-Key: $apiKey" `
     "http://localhost:5051/api/v1/invoices?fromDate=2026-01-01&toDate=2026-01-31&type=AR"
```

**Test unauthorized access (confirm auth is working):**
```powershell
# No key — should return 401
curl -i http://localhost:5051/api/v1/invoices?fromDate=2026-01-01&toDate=2026-01-31

# Wrong key — should return 401
curl -i -H "X-API-Key: wrong-key" `
     "http://localhost:5051/api/v1/invoices?fromDate=2026-01-01&toDate=2026-01-31"
```

**Expected for both:** HTTP 401 Unauthorized
```json
{"code":"UNAUTHORIZED","message":"Invalid or missing API key.","correlationId":"...","timestamp":"..."}
```

**Test admin key access:**
```powershell
$adminKey = "YOUR_ADMIN_KEY_HERE"
curl -H "X-Admin-Key: $adminKey" `
     "http://localhost:5051/api/v1/invoices?fromDate=2026-01-01&toDate=2026-01-31"
```

**Expected:** HTTP 200 OK (same response as primary key)

### Troubleshooting MyInvois.Api

| Issue | Solution |
|-------|----------|
| HTTP 401 on all requests | Set `ApiKeys:Primary` via `dotnet user-secrets` in `src/MyInvois.Api/` |
| HTTP 503 on all requests | `ApiKeys:Primary` is empty/not configured — check `dotnet user-secrets list` |
| HTTP 502 on invoice requests | DB2 connection failed — check `MovexDb:ConnectionString` secret in `src/MyInvois.Api/` |
| Connection refused on port 5051 | Api not running; start with `dotnet run` from `src/MyInvois.Api/` |
| Empty `items` array | No MOVEX data for date range — try a wider range or check DB2 connectivity (Step 4) |

> **IIS Deployment:** For UAT/Production IIS setup, see [Phase 11 of the Deployment Guide](../ai/memory/05-deployment-guide.md#phase-11-myinvoisapi-iis-site-setup). The IIS app pool is `MyInvoisApi`, the site binds to `http://localhost:5051`.

---

## Configuration Files

### appsettings.json (Production/Default)
- Defines all configuration keys with safe defaults
- Sensitive values reference `{{FROM_USER_SECRETS}}` — set via `dotnet user-secrets`
- Batch sizes, API timeouts, validation rules

### appsettings.Development.json (Local Overrides)
- Overrides for local development
- Uses local SQLite database (`./data/audit.db`)
- Lower log levels, sandbox API endpoint

### Key Configuration Options

#### Party Data Source (`MovexDb:PartyDataSource`)

Controls how supplier/customer TIN, BRN, and address are resolved:

| Value | Behaviour | When to use |
|-------|-----------|-------------|
| `Placeholder` | Returns stub data; logs a warning on every call | Default for development — submissions will fail validation |
| `MovexMaster` | Queries CIDMAS (suppliers) and OCUSMA (customers) in DB2 | Switch to this once TIN/BRN column names are confirmed by Finance |

Switch via appsettings or user-secrets:
```powershell
dotnet user-secrets set "MovexDb:PartyDataSource" "MovexMaster"
```

#### TIN/BRN Column Mapping (MovexMaster only)

When `PartyDataSource` is `MovexMaster`, the columns used for TIN and BRN must be configured.
Leave empty until Finance confirms the column names — the provider returns `null` TIN/BRN when empty
(which will trigger MyInvois validation errors).

```powershell
dotnet user-secrets set "MovexDb:SupplierTinColumn" "IDCFC1"
dotnet user-secrets set "MovexDb:SupplierBrnColumn" "IDCORG"
dotnet user-secrets set "MovexDb:CustomerTinColumn" "OKCFC1"
dotnet user-secrets set "MovexDb:CustomerBrnColumn" "OKCORG"
```

#### AR Query Filters

AR invoices are filtered by division, transaction code, and customer status. Defaults match
production values — override only if required:

| Key | Default | Column |
|-----|---------|--------|
| `MovexDb:ArDivision` | `L` | `FSLEDG.ESDIVI` |
| `MovexDb:ArTransCode` | `10` | `FSLEDG.ESTRCD` |
| `MovexDb:ArCustomerStatus` | `20` | `OCUSMA.OKSTAT` (active customers) |
| `MovexDb:ArMinYear` | `0` (last year) | `FSLEDG.ESYEA4` — set explicit year if needed |

---

## User Secrets Location

- **Windows:** `%APPDATA%\Microsoft\UserSecrets\<ProjectGuid>\secrets.json`
- **Linux/Mac:** `~/.microsoft/usersecrets/<ProjectGuid>/secrets.json`

View all secrets:
```powershell
dotnet user-secrets list
```

---

## Common Issues

| Issue | Solution |
|-------|----------|
| "User secrets are not configured" | Run `dotnet user-secrets init` first |
| "`audit.db` not created" | Run the service once — EF Core creates it on startup; check write permissions on `./data/` directory |
| "API key rejected" | Verify key hasn't expired, contact IT Ops |
| "Certificate validation failed" | Run `dotnet dev-certs https --trust` for local HTTPS |
| Pre-commit hook not running | Run `.\setup-hooks.ps1` or `git config core.hooksPath .githooks` |
| Pre-commit hook "pwsh not found" | The hook falls back to `powershell.exe` automatically; no action needed |
| "PlaceholderPartyDataProvider" warnings in logs | Expected when `PartyDataSource=Placeholder`; switch to `MovexMaster` when ready |
| AR query returns no rows | Check `ArDivision`, `ArTransCode`, `ArCustomerStatus` in appsettings match your MOVEX data |

---

## Next Steps

Once local setup is complete:
1. Read [README.md](../README.md) for project overview
2. Review [03-myinvois-requirements.md](../ai/memory/03-myinvois-requirements.md) for validation rules & traceability
3. Start implementing Week 2 development tasks
4. Run unit tests: `dotnet test`

---

**Questions?** Contact the Development Team or refer to [TROUBLESHOOTING.md](TROUBLESHOOTING.md).
