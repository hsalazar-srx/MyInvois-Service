# MyInvois-Service — Local Setup Guide

**Duration:** 5-10 minutes  
**Prerequisites:** .NET 8.0 SDK, SQL Server, IBM DB2 driver (Net.IBM.Data.Db2), Windows domain account

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

### Set SQL Server Connection String

```powershell
dotnet user-secrets set "ConnectionStrings:AuditLog" "Server=YOUR_SQL_SERVER;Database=SRX_AuditLog;Integrated Security=true;TrustServerCertificate=true;Connection Timeout=30;"
```

**Example:** 
```powershell
dotnet user-secrets set "ConnectionStrings:AuditLog" "Server=SQLSERVER01;Database=SRX_AuditLog;Integrated Security=true;TrustServerCertificate=true;"
```

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

**Expected:** 5 secrets configured (MovexDb:ConnectionString, MyInvoisApi:ClientId, MyInvoisApi:ClientSecret, ConnectionStrings:AuditLog, plus certificate password via Credential Manager)

---

## Step 3: Create SQL Server Database

### Create Database

```powershell
# Connect to your SQL Server instance
sqlcmd -S YOUR_SQL_SERVER

# In SQLCMD prompt:
CREATE DATABASE SRX_AuditLog;
GO
EXIT
```

### Create Schema & Tables

```powershell
# Run schema script (from MyInvois-Service root folder)
sqlcmd -S YOUR_SQL_SERVER -i .\src\Database\create-audit-table.sql -d SRX_AuditLog
sqlcmd -S YOUR_SQL_SERVER -i .\src\Database\create-audit-views.sql -d SRX_AuditLog
```

**Expected:** "Audit Log schema setup completed."

### Verify Database

```powershell
sqlcmd -S YOUR_SQL_SERVER -d SRX_AuditLog -Q "SELECT COUNT(*) FROM [dbo].[AuditLog];"
```

**Expected:** Returns 0 (empty table)

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

## Configuration Files

### appsettings.json (Production/Default)
- Defines all configuration keys with safe defaults
- Sensitive values reference `{{FROM_USER_SECRETS}}` — set via `dotnet user-secrets`
- Batch sizes, API timeouts, validation rules

### appsettings.Development.json (Local Overrides)
- Overrides for local development
- Uses local SQL Server instance (`(local)`)
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
| "Cannot connect to SQL Server" | Check server name, instance, and integrated auth enabled |
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
