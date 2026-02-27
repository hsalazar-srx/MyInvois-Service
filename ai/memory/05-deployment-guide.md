# MyInvois-Service - Deployment Guide

**Last Updated**: 2026-02-05  
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
- **Database**: SQL Server 2019+ (Developer Edition acceptable for dev)
- **Memory**: 2GB minimum (4GB recommended)
- **Disk**: 2GB minimum (10GB recommended for SQL logs)

### Software Requirements
- **Visual Studio 2022** or **Visual Studio Code**
- **SQL Server Management Studio** (SSMS)
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

### 2.2 Create SQL Server Database & Schema

```sql
-- Create database
CREATE DATABASE [SRX_AuditLog];

USE [SRX_AuditLog];

-- Create dbo.AuditLog table
CREATE TABLE [dbo].[AuditLog] (
    [AuditLogID] [bigint] IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Timestamp] [datetime2] NOT NULL DEFAULT GETUTCDATE(),
    [InvoiceNumber] [nvarchar](50) NOT NULL,
    [InvoiceType] [nvarchar](10),
    [Action] [nvarchar](50) NOT NULL,  -- 'Fetched', 'Transformed', 'Submitted', 'Failed'
    [Status] [nvarchar](50) NOT NULL,  -- 'Success', 'Pending', 'Failed'
    [HttpStatusCode] [int],
    [ErrorCode] [nvarchar](50),
    [ErrorMessage] [nvarchar](max),
    [MyInvoisUUID] [nvarchar](36),
    [MyInvoisStatus] [nvarchar](50),
    [SubmittedBy] [nvarchar](255),
    [BatchID] [nvarchar](50),
    [DurationMs] [int],
    [RawRequest] [nvarchar](max),
    [RawResponse] [nvarchar](max),
    [AdditionalData] [nvarchar](max),
    CONSTRAINT [CK_AuditLog_Status] CHECK ([Status] IN ('Success', 'Pending', 'Failed')),
    CONSTRAINT [CK_AuditLog_Action] CHECK ([Action] IN ('Fetched', 'Transformed', 'Submitted', 'Failed'))
);

-- Create indexes for performance
CREATE NONCLUSTERED INDEX [IX_AuditLog_InvoiceNumber] 
    ON [dbo].[AuditLog]([InvoiceNumber]);

CREATE NONCLUSTERED INDEX [IX_AuditLog_Timestamp] 
    ON [dbo].[AuditLog]([Timestamp]);

CREATE NONCLUSTERED INDEX [IX_AuditLog_Status] 
    ON [dbo].[AuditLog]([Status]);

CREATE NONCLUSTERED INDEX [IX_AuditLog_MyInvoisUUID] 
    ON [dbo].[AuditLog]([MyInvoisUUID]);

-- Create views for reporting
CREATE VIEW [dbo].[vw_FailedSubmissions] AS
SELECT 
    [AuditLogID], [Timestamp], [InvoiceNumber], [ErrorCode], 
    [ErrorMessage], [Status], [HttpStatusCode], [DurationMs]
FROM [dbo].[AuditLog]
WHERE [Action] = 'Submitted' AND [Status] = 'Failed';

CREATE VIEW [dbo].[vw_MonthlySummary] AS
SELECT 
    YEAR([Timestamp]) AS [Year],
    MONTH([Timestamp]) AS [Month],
    COUNT(*) AS [TotalInvoices],
    SUM(CASE WHEN [Status] = 'Success' THEN 1 ELSE 0 END) AS [SuccessCount],
    SUM(CASE WHEN [Status] = 'Failed' THEN 1 ELSE 0 END) AS [FailedCount],
    SUM(CASE WHEN [Status] = 'Pending' THEN 1 ELSE 0 END) AS [PendingCount],
    AVG(CAST([DurationMs] AS FLOAT)) AS [AvgDurationMs]
FROM [dbo].[AuditLog]
WHERE [Action] = 'Submitted'
GROUP BY YEAR([Timestamp]), MONTH([Timestamp]);

CREATE VIEW [dbo].[vw_DuplicateSubmissions] AS
SELECT 
    [InvoiceNumber], COUNT(*) AS [SubmissionCount],
    MIN([Timestamp]) AS [FirstSubmission],
    MAX([Timestamp]) AS [LastSubmission]
FROM [dbo].[AuditLog]
WHERE [Action] = 'Submitted' AND [Status] = 'Success'
GROUP BY [InvoiceNumber]
HAVING COUNT(*) > 1;

-- Retention policy (7 years = 2555 days)
CREATE PROCEDURE [dbo].[sp_PurgeOldAuditLogs]
AS
BEGIN
    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -2555, GETUTCDATE());
    
    DELETE FROM [dbo].[AuditLog]
    WHERE [Timestamp] < @CutoffDate;
    
    DBCC SHRINKFILE (SRX_AuditLog_log, 0);
END;
```

### 2.3 Verify SQL Setup

```powershell
# Open SQL Server Management Studio and connect
# Execute test query
SELECT COUNT(*) FROM [dbo].[AuditLog];
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
    "AuditLogDatabase": "Server=localhost;Database=SRX_AuditLog;Trusted_Connection=true;"
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

### 6.1 Enable Audit Log Retention Policy

```sql
-- Schedule weekly cleanup
EXEC msdb.dbo.sp_add_job 
    @job_name = 'MyInvois_AuditLog_Cleanup';

EXEC msdb.dbo.sp_add_jobstep 
    @job_name = 'MyInvois_AuditLog_Cleanup',
    @step_name = 'Purge_Old_Records',
    @command = 'EXEC [dbo].[sp_PurgeOldAuditLogs]';

EXEC msdb.dbo.sp_add_schedule 
    @schedule_name = 'Weekly_Sunday_Midnight',
    @freq_type = 8,
    @freq_interval = 1,
    @active_start_time = 000000;
```

### 6.2 Monitor Disk Usage

```sql
-- Check database size
SELECT 
    name,
    size / 1024 / 1024 AS [Size_MB]
FROM sys.database_files;

-- Check table growth
SELECT 
    OBJECT_NAME(ps.object_id) AS TableName,
    ps.row_count,
    (ps.reserved_page_count * 8) / 1024 AS [Reserved_MB]
FROM sys.dm_db_partition_stats ps;
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

```sql
-- Last 10 submissions
SELECT TOP 10 * FROM dbo.AuditLog 
WHERE Action = 'Submitted' 
ORDER BY Timestamp DESC;

-- Failed invoices this month
SELECT * FROM dbo.vw_FailedSubmissions
WHERE MONTH(Timestamp) = MONTH(GETDATE())
  AND YEAR(Timestamp) = YEAR(GETDATE());

-- Monthly summary
SELECT * FROM dbo.vw_MonthlySummary
ORDER BY Year DESC, Month DESC;

-- Duplicate submissions
SELECT * FROM dbo.vw_DuplicateSubmissions;
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
- [ ] SQL Server running? `sqlcmd -S localhost`
- [ ] User Secrets configured? `dotnet user-secrets list`
- [ ] appsettings.json valid JSON? Use VS Code to validate
- [ ] Check Windows Event Viewer for errors

### Connection String Issues
```sql
-- Test connection from application server
SQLCMD -S srxdatabase -U appuser -P password -d SRX_AuditLog -Q "SELECT 1"
```

### OAuth Token Fails
- [ ] Credentials correct? Check User Secrets
- [ ] Token endpoint accessible? `curl https://api.myinvois.hasil.gov.my/connect/token`
- [ ] Scope correct? Must be "InvoiceService"
- [ ] Sandbox vs Production? Check appsettings.json

### MyInvois Submission Fails

```sql
-- Check recent errors
SELECT TOP 20 
    Timestamp, InvoiceNumber, ErrorCode, ErrorMessage, RawResponse
FROM dbo.AuditLog
WHERE Status = 'Failed'
ORDER BY Timestamp DESC;
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

### If Database Corruption

```sql
-- Restore from backup
RESTORE DATABASE [SRX_AuditLog] 
FROM DISK = 'C:\Backups\SRX_AuditLog_2026-02-05.bak'
WITH REPLACE;
```

---

## Phase 10: Go-Live Checklist (Feb 28, 2026)

- [ ] Production database created & backed up
- [ ] OAuth credentials registered with MyInvois
- [ ] DB2 AS/400 connection verified
- [ ] Firewall rules configured (inbound/outbound)
- [ ] User Secrets configured in production server
- [ ] Scheduled job created (1st of month, 2:00 AM)
- [ ] Audit log retention policy enabled
- [ ] Monitoring alerts configured
- [ ] Documentation updated with production URLs
- [ ] Team trained on monitoring dashboards
- [ ] Rollback plan tested and documented

---

## 🔗 Related Documents

- [00-Product Vision](00-product-vision.md) - Project objectives
- [01-System Architecture](01-system-architecture.md) - System design
- [04-API Integration](04-api-integration.md) - API specifications

---

**Owner**: DevOps Team  
**Last Review**: 2026-02-05  
**Next Review**: 2026-02-28 (Post-Launch)

