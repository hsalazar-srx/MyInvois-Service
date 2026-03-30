# MyInvois-Service - Deployment Guide

**Last Updated**: 2026-03-18
**Status**: MVAI Iteration 1 (Pre-Production)  
**Version**: 1.0

---

## 🚀 Overview

Complete setup instructions for deploying MyInvois-Service in development, staging, and production environments.

---

## Phase 1: Prerequisites

### Hardware Requirements
- **OS**: Windows Server 2019+ or Windows 11
- **Runtime**: .NET 8.0 Runtime (or SDK if developing)
- **Disk**: 2GB minimum (100MB/year estimated for SQLite audit log growth)
- **Memory**: 2GB minimum (4GB recommended)

### Software Requirements
- **Visual Studio 2022** or **Visual Studio Code**
- **IBM DB2 iSeries Access ODBC driver** (for MOVEX AS400 access)
- **sqlite3.exe** (optional — for manual audit log inspection; download from sqlite.org or `winget install SQLite.SQLite`)
- **Git** (for version control)

### Network Access
- Inbound: None (service runs locally)
- Outbound:
  - DB2 AS/400 server (MOVEX database)
  - api.myinvois.hasil.gov.my (MyInvois API)
  - User Secrets Manager (Windows Credential Store)

---

## Phase 2: Environment Setup

### 2.1 Clone Repository

```powershell
# From Git
git clone https://github.com/YourOrg/MyInvois-Service.git
cd MyInvois-Service
```

### 2.2 Audit Database Setup (Auto-Created)

The audit database is SQLite (`audit.db`) managed by EF Core 8 (ADR-014). The schema is **created automatically** on first startup via `EnsureCreated` + `PRAGMA journal_mode=WAL`. No manual SQL setup is required.

### 2.3 Verify After First Run

```powershell
$db = "C:\inetpub\apps\MyInvois.Api\data\audit.db"  # adjust path to your deploy location

# Verify WAL mode
& sqlite3 $db "PRAGMA journal_mode;"  # Expected: wal

# Verify table exists and is empty
& sqlite3 $db "SELECT COUNT(*) FROM AuditLogs;"  # Expected: 0
```

---

## Phase 3: Application Configuration

### 3.1 Clone Configuration Template

**File**: `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "ConnectionStrings": {
    "AuditLog": "Data Source=./data/audit.db"
  },
  "MovexDb": {
    "ConnectionString": "DataSource=AS400SERVER;UserID=MOVEXUSER;Password=***;DefaultCollection=MOVEXDB;",
    "TimeoutSeconds": 30,
    "RetryCount": 3,
    "RetryDelaySeconds": 2
  },
  "MyInvoisApi": {
    "BaseUrl": "https://api.myinvois.hasil.gov.my",
    "Environment": "sandbox",
    "TokenEndpoint": "/connect/token",
    "SubmissionEndpoint": "/api/v1.0/documentsubmissions",
    "TimeoutSeconds": 30,
    "MaxRetries": 3,
    "RetryDelaySeconds": 2
  },
  "BatchProcessing": {
    "SalesInvoiceBatchSize": 100,
    "PurchaseInvoiceBatchSize": 50,
    "BatchDelayMs": 600,
    "MaxConcurrentBatches": 1
  },
  "Processing": {
    "EnableMonthlyBatch": true,
    "MonthlyBatchSchedule": "0 2 1 * *",
    "MaxInvoicesPerMonth": 10000
  },
  "Validation": {
    "TinCacheDurationMinutes": 60,
    "RequireExchangeRateForNonMYR": true,
    "DecimalPrecision": 2,
    "TaxRatePrecision": 4
  }
}
```

### 3.2 Configure User Secrets (OAuth Credentials)

```powershell
# Open PowerShell in project directory
# Initialize User Secrets
dotnet user-secrets init

# Set OAuth credentials (from MyInvois registration)
dotnet user-secrets set "MyInvoisApi:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "MyInvoisApi:ClientSecret" "YOUR_CLIENT_SECRET"

# Verify secrets were saved
dotnet user-secrets list
```

**Expected Output**:
```
MyInvoisApi:ClientId = YOUR_CLIENT_ID
MyInvoisApi:ClientSecret = ***
```

### 3.3 Environment-Specific Configurations

**Development** (`appsettings.Development.json`):
```json
{
  "MyInvoisApi": {
    "Environment": "sandbox",
    "BaseUrl": "https://sandbox.myinvois.hasil.gov.my"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

**Staging** (`appsettings.Staging.json`):
```json
{
  "MyInvoisApi": {
    "Environment": "sandbox",
    "BaseUrl": "https://sandbox.myinvois.hasil.gov.my"
  },
  "Processing": {
    "MaxInvoicesPerMonth": 5000
  }
}
```

**Production** (`appsettings.Production.json`):
```json
{
  "MyInvoisApi": {
    "Environment": "production",
    "BaseUrl": "https://api.myinvois.hasil.gov.my"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

---

## Phase 4: Build & Test Locally

### 4.1 Build Solution

```powershell
# Restore NuGet packages
dotnet restore

# Build solution
dotnet build --configuration Release

# Expected output
# Build succeeded. 0 Warning(s)
```

### 4.2 Run Unit Tests

```powershell
# Run all tests
dotnet test

# Run specific test project
dotnet test --project .\MyInvois.Service.Tests\

# Expected output
# Test Run Successful.
# Total tests: 54
# Passed: 54
# Failed: 0
```

### 4.3 Local Integration Test

```powershell
# Start application (Worker Service - background)
dotnet run --configuration Debug --project .\MyInvois.Service\

# Check logs for startup messages
# Expected: "Worker started successfully"

# Test API endpoints
Invoke-RestMethod -Uri "http://localhost:8080/health"
```

---

## Phase 5: Scheduled Job Setup

### Windows Task Scheduler (Recommended for Phase 1)

```powershell
# Run as Administrator

# Create batch file: C:\Scripts\run-myinvois-batch.bat
@echo off
cd C:\Projects\MyInvois-Service
set ASPNETCORE_ENVIRONMENT=Production
dotnet MyInvois.Service.dll --batch-mode
pause

# Create scheduled task (1st of month at 2:00 AM UTC)
$action = New-ScheduledTaskAction `
    -Execute 'C:\Scripts\run-myinvois-batch.bat'

$trigger = New-ScheduledTaskTrigger `
    -Monthly `
    -At '02:00:00' `
    -DaysOfMonth 1

Register-ScheduledTask `
    -TaskName 'MyInvois-Monthly-Batch' `
    -Action $action `
    -Trigger $trigger `
    -RunLevel Highest `
    -User 'DOMAIN\MyInvoisService'
```

### Azure Functions (Phase 2 Alternative)

```csharp
[FunctionName("MyInvoisBatchProcessor")]
public async Task Run(
    [TimerTrigger("0 2 1 * * *")] TimerInfo timer,
    ILogger log)
{
    var processor = new InvoiceProcessor(_invoiceReader, _mapper, _submitter);
    await processor.ProcessMonthlyBatch(DateTime.UtcNow.AddMonths(-1));
}
```

---

## Phase 6: Database Maintenance

### 6.1 Audit Log Retention (7-Year Policy)

SQLite does not have a scheduled job agent. Schedule a weekly Task Scheduler job to purge records older than 7 years (2555 days):

```powershell
# Weekly cleanup — run as Task Scheduler action
$db = "C:\inetpub\apps\MyInvois.Api\data\audit.db"
& sqlite3 $db "DELETE FROM AuditLogs WHERE datetime(Timestamp) < datetime('now', '-2555 days');"
& sqlite3 $db "VACUUM;"  # Reclaim freed space
Write-Host "Audit log retention cleanup complete: $(Get-Date)"
```

### 6.2 Monitor Disk Usage

```powershell
$db = "C:\inetpub\apps\MyInvois.Api\data\audit.db"

# Check file size
(Get-Item $db).Length / 1MB | ForEach-Object { "{0:N2} MB" -f $_ }

# Check row count and approximate growth
& sqlite3 $db "SELECT COUNT(*) AS TotalRows, MIN(Timestamp) AS OldestRecord, MAX(Timestamp) AS NewestRecord FROM AuditLogs;"
```

---

## Phase 7: Monitoring & Logging

### 7.1 Application Logs

**Location**: `C:\Projects\MyInvois-Service\logs\`

**Log Levels**:
- **Debug**: Development troubleshooting
- **Information**: Normal operations (submissions, errors)
- **Warning**: Retryable errors, auth issues
- **Error**: Non-retriable failures
- **Critical**: System failures

### 7.2 Audit Log Queries

```powershell
$db = "C:\inetpub\apps\MyInvois.Api\data\audit.db"

# Last 10 submissions
& sqlite3 $db "SELECT AuditId, InvoiceNumber, Status, Action, Timestamp FROM AuditLogs ORDER BY Timestamp DESC LIMIT 10;"

# Failed invoices this month
& sqlite3 $db "SELECT InvoiceNumber, ErrorMessage, Timestamp FROM AuditLogs WHERE Status='Failed' AND strftime('%Y-%m', Timestamp) = strftime('%Y-%m', 'now') ORDER BY Timestamp DESC;"

# Monthly summary
& sqlite3 $db "SELECT strftime('%Y-%m', Timestamp) AS Month, COUNT(*) AS Total, SUM(CASE WHEN Status='Success' THEN 1 ELSE 0 END) AS Succeeded, SUM(CASE WHEN Status='Failed' THEN 1 ELSE 0 END) AS Failed FROM AuditLogs WHERE Action='MyInvois_Submit' GROUP BY Month ORDER BY Month DESC;"

# Duplicate submissions (same invoice submitted more than once successfully)
& sqlite3 $db "SELECT InvoiceNumber, COUNT(*) AS SubmissionCount, MIN(Timestamp) AS First, MAX(Timestamp) AS Last FROM AuditLogs WHERE Action='MyInvois_Submit' AND Status='Success' GROUP BY InvoiceNumber HAVING COUNT(*) > 1;"
```

### 7.3 Health Check Endpoint

```csharp
[HttpGet("health")]
public IActionResult Health()
{
    var checks = new Dictionary<string, object>
    {
        { "status", "healthy" },
        { "database", CheckDatabaseConnection() },
        { "movex_db", CheckMovexDb() },
        { "myinvois_api", CheckMyInvoisApi() },
        { "timestamp", DateTime.UtcNow }
    };
    
    return Ok(checks);
}
```

---

## Phase 8: Troubleshooting Checklist

### Service Won't Start
- [ ] .NET 8.0 Runtime installed? `dotnet --version`
- [ ] `./data/` directory exists and is writable by the service account?
- [ ] `audit.db` present? (Created on first run — check write permissions if missing)
- [ ] User Secrets configured? `dotnet user-secrets list`
- [ ] appsettings.json valid JSON? Use VS Code to validate
- [ ] Check Windows Event Viewer for errors

### Audit DB Issues
```powershell
# Verify SQLite file is accessible
$db = ".\data\audit.db"
Test-Path $db
& sqlite3 $db "PRAGMA integrity_check;"  # Expected: ok
```

### OAuth Token Fails
- [ ] Credentials correct? Check User Secrets
- [ ] Token endpoint accessible? `curl https://api.myinvois.hasil.gov.my/connect/token`
- [ ] Scope correct? Must be "InvoiceService"
- [ ] Sandbox vs Production? Check appsettings.json

### MyInvois Submission Fails

```powershell
# Check recent errors
$db = ".\data\audit.db"
& sqlite3 $db "SELECT Timestamp, InvoiceNumber, ErrorMessage, ResponsePayload FROM AuditLogs WHERE Status='Failed' ORDER BY Timestamp DESC LIMIT 20;"
```

### MOVEX DB2 Connection Timeout
- [ ] Network connectivity: `ping AS400SERVER`
- [ ] Firewall rules allow DB2 port (typically 446 or 8471)?
- [ ] Increase timeout? Update appsettings.json `TimeoutSeconds`
- [ ] Check DB2 job logs on AS/400 server

---

## Phase 9: Rollback Procedure

### If Deployment Fails

```powershell
# 1. Stop service
Stop-Service MyInvoisService

# 2. Restore previous version from Git
git checkout <previous-commit-hash>

# 3. Rebuild
dotnet build --configuration Release

# 4. Start service
Start-Service MyInvoisService

# 5. Verify
dotnet user-secrets list
```

### If Audit Database Corrupt

```powershell
# Check integrity
$db = ".\data\audit.db"
& sqlite3 $db "PRAGMA integrity_check;"

# If corrupt — restore from daily backup
$backup = "\\backup-server\MyInvois\SQLiteAudit\{yyMMdd}\audit_{yyMMdd}.db"
Copy-Item $backup $db -Force
Write-Host "Restored audit.db from backup"
```

---

## Phase 10: Go-Live Checklist (Apr 30, 2026)

- [ ] `./data/` directory created on production server with correct NTFS ACL (service account write access)
- [ ] `audit.db` created on first test run; WAL mode confirmed (`PRAGMA journal_mode;` = `wal`)
- [ ] SQLite daily backup script scheduled (see DEPLOYMENT.md — 7-year retention per ADR-014)
- [ ] OAuth credentials registered with MyInvois
- [ ] DB2 AS/400 ODBC connection verified
- [ ] Firewall rules configured (inbound/outbound)
- [ ] User Secrets configured on production server (MovexDb, API keys, certificate)
- [ ] Scheduled job created (1st of month, 2:00 AM)
- [ ] Certificate expiry monitoring enabled (daily at 06:00 AM)
- [ ] Monitoring alerts configured
- [ ] Documentation updated with production URLs
- [ ] Team trained on SQLite audit log queries
- [ ] Rollback plan tested and documented

---

## Phase 11: MyInvois.Api Host (Invoice Extract)

This section covers the IIS deployment of `MyInvois.Api` — the ASP.NET Core REST host that wraps
`MyInvois.Service` and exposes a localhost-only HTTP endpoint consumed by SM-Portal.

**Architecture:**
```
SM-Portal (IIS, port 5050)
  → HTTP GET (X-API-Key)
    → MyInvois.Api (IIS, port 5051, localhost-only)
      → DB2/AS400 via ODBC
```

### 11.1 Prerequisites

| Requirement | Notes |
|---|---|
| .NET 8.0 Hosting Bundle | Install on the IIS host — separate from .NET SDK |
| IBM DB2 iSeries Access ODBC driver | Required on the IIS host machine; download from IBM Fix Central |
| IIS with ASP.NET Core Module v2 | Installed automatically by the .NET 8 Hosting Bundle |
| App pool identity — AS/400 network access | The `ApplicationPoolIdentity` or a service account must be able to reach the AS/400 TCP port |
| IIS WebSockets feature | WebSockets is **not** required; leave disabled (it is disabled by default) |

### 11.2 User Secrets Setup

Run the following commands **from `src/MyInvois.Api/`** (not from the SM-Portal directory or the
solution root — user secrets are scoped to the `.csproj` `UserSecretsId`).

```powershell
cd c:\Projects\MyInvois-Service\src\MyInvois.Api

dotnet user-secrets init

dotnet user-secrets set "ApiKeys:Primary" "<generate-random-guid>"
dotnet user-secrets set "ApiKeys:Admin"   "<generate-different-guid>"
dotnet user-secrets set "MovexDb:ConnectionString" "DSN=AS400PROD;UID=...;PWD=...;"

# Verify
dotnet user-secrets list
```

**Expected output:**
```
ApiKeys:Primary = <guid>
ApiKeys:Admin = <guid>
MovexDb:ConnectionString = DSN=AS400PROD;UID=***;PWD=***;
```

> Note: `MovexDb:ConnectionString` must **only** exist in MyInvois.Api user-secrets.
> Do **not** set this secret in SM-Portal — DB2 credentials must never be accessible to the portal process.

### 11.3 Build and Publish

```powershell
# From the solution root
cd c:\Projects\MyInvois-Service

dotnet restore
dotnet publish src/MyInvois.Api/MyInvois.Api.csproj -c Release -o ./publish/MyInvois.Api
```

**Expected output:**
```
Build succeeded.
MyInvois.Api -> .\publish\MyInvois.Api\
```

### 11.4 IIS Setup

#### 11.4.1 Create Application Pool

| Setting | Value |
|---|---|
| Name | `MyInvoisApi` |
| .NET CLR Version | No Managed Code |
| Managed Pipeline Mode | Integrated |
| Identity | ApplicationPoolIdentity (or a dedicated service account with AS/400 access) |

```powershell
# PowerShell (run as Administrator)
Import-Module WebAdministration

New-WebAppPool -Name "MyInvoisApi"
Set-ItemProperty IIS:\AppPools\MyInvoisApi managedRuntimeVersion ""
Set-ItemProperty IIS:\AppPools\MyInvoisApi processModel.identityType ApplicationPoolIdentity
```

#### 11.4.2 Create Website

| Setting | Value |
|---|---|
| Site name | `MyInvois.Api` |
| Physical path | `C:\inetpub\apps\MyInvois.Api` (copy publish output here) |
| Binding | `http://localhost:5051` — **localhost-only, never bind to 0.0.0.0 or the server IP** |
| Application pool | `MyInvoisApi` |

```powershell
# Copy publish output
Copy-Item -Recurse -Force .\publish\MyInvois.Api\* C:\inetpub\apps\MyInvois.Api\

# Create site
New-Website -Name "MyInvois.Api" `
            -PhysicalPath "C:\inetpub\apps\MyInvois.Api" `
            -ApplicationPool "MyInvoisApi" `
            -Port 5051 `
            -IPAddress "127.0.0.1"
```

#### 11.4.3 Set ASPNETCORE_CONTENTROOT in web.config

Per workspace standard, add the environment variable to the `<aspNetCore>` handler block in
`C:\inetpub\apps\MyInvois.Api\web.config`:

```xml
<aspNetCore processPath="dotnet" arguments=".\MyInvois.Api.dll" stdoutLogEnabled="false">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_CONTENTROOT"
                         value="C:\inetpub\apps\MyInvois.Api" />
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
  </environmentVariables>
</aspNetCore>
```

#### 11.4.4 Grant Folder Permissions

```powershell
# Grant read+execute to the app pool identity
$acl = Get-Acl "C:\inetpub\apps\MyInvois.Api"
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "IIS AppPool\MyInvoisApi", "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.AddAccessRule($rule)
Set-Acl "C:\inetpub\apps\MyInvois.Api" $acl
```

### 11.5 Smoke Test

```powershell
# Health check — no auth required
curl -H "X-API-Key: <primary>" "http://localhost:5051/api/v1/health"
# Expected: {"status":"healthy","timestamp":"2026-..."}

# Invoice extract — requires X-API-Key
curl -H "X-API-Key: <primary>" `
     "http://localhost:5051/api/v1/invoices?fromDate=2025-01-01&toDate=2025-03-31&type=ALL"
# Expected: {"invoices":[...],"totalCount":N}
```

If health returns 200 and invoices returns 200 (or 204 with an empty range), the service is
operating correctly.

### 11.6 Security Checklist

- [ ] Port 5051 is bound to `127.0.0.1` only — confirm with `netstat -ano | findstr 5051`
- [ ] Windows Firewall has no inbound rule for port 5051 (no external access needed)
- [ ] `MovexDb:ConnectionString` is **not** present in SM-Portal user-secrets — DB2 credentials must reside only in `MyInvois.Api` user-secrets
- [ ] `ApiKeys:Primary` and `ApiKeys:Admin` are random GUIDs — never reuse passwords from other systems
- [ ] IIS site binding does **not** include the server IP or hostname — localhost only
- [ ] `stdoutLogEnabled` is `false` in web.config (stdout can leak secrets to disk)
- [ ] App pool identity service account (if not ApplicationPoolIdentity) has minimum required AS/400 permissions

---

## 🔗 Related Documents

- [00-Product Vision](00-product-vision.md) - Project objectives
- [01-System Architecture](01-system-architecture.md) - System design
- [04-API Integration](04-api-integration.md) - API specifications
- [MyInvois.Api Runbook](../../docs/runbooks/myinvois-api-runbook.md) - Operational runbook for MyInvois.Api

---

**Owner**: DevOps Team
**Last Review**: 2026-03-11
**Next Review**: 2026-06-11

