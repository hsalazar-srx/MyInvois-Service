# MyInvois-Service — Deployment Runbook

**Target Audience:** IT Operations  
**Version:** 1.0-MVAI  
**Status:** Pre-Production (Feb 28, 2026)

---

## Pre-Deployment Checklist

**72 Hours Before Go-Live**

### Infrastructure & Certificate
- [ ] **Certificate**: Digital certificate received from Finance, stored in encrypted directory (`C:\Certs\MyInvois`)
- [ ] **Certificate Storage**: Windows EFS encryption enabled, NTFS permissions restricted to service account
- [ ] **Certificate Password**: Stored securely (Windows Credential Manager or DPAPI)
- [ ] **Certificate Access**: Service account can read certificate (test script passed)
- [ ] **Certificate Backup**: Encrypted backup created and verified in offsite location
- [ ] **Monitoring Script**: Certificate expiry monitoring job deployed (daily check at 06:00 AM)
- [ ] **Disaster Recovery**: Documented recovery procedure, test restore completed

### Database & Configuration
- [ ] **Database**: SRX_AuditLog exists, TDE enabled
- [ ] **Schema**: All tables and indexes created
- [ ] **Backup**: Full backup scheduled (daily)
- [ ] **Network**: Firewall rules allowing MOVEX DB2/AS400 (port 446/8471) & MyInvois API access
- [ ] **Configuration**: All settings validated in production appsettings.json (CertificateSettings section populated)

**24 Hours Before**

- [ ] **Certificate Test**: Signature generation test passed in MyInvois sandbox
- [ ] **Dry run**: Process 100 test invoices successfully
- [ ] **Monitoring**: Certificate expiry monitoring verified (logs show daily checks)
- [ ] **Alerting**: Email alerts configured for Finance + Infrastructure teams
- [ ] **Rollback plan**: Documented procedure to revert to pre-MVAI state, backup verified
- [ ] **Support**: On-call team briefed on common issues (especially certificate-related)

---

## Deployment Steps

### 1. Build Release Package

```powershell
# From project root
dotnet publish -c Release -o .\publish\
```

**Expected:** Executable in `.\publish\MyInvois.Service.exe`

### 2. Transfer to Target Server

Copy build artifacts to production server:
```
C:\Services\MyInvois-Service\
```

**Folder structure:**
```
C:\Services\MyInvois-Service\
├── MyInvois.Service.exe
├── MyInvois.Service.dll
├── appsettings.json
├── appsettings.Production.json
├── ...
└── database\
    └── backups\
```

### 3. Configure Production Secrets

On production server, set via **Encrypted Server Storage** (certificate) and **environment variables**:

```powershell
# Set MOVEX DB2/AS400 connection
$env:MOVEX_DB_CONNECTION = "Server=PROD_AS400;Database=PROD_DB;UserID=svc_myinvois;Password=prod_password;"

# Set MyInvois credentials
$env:MYINVOIS_CLIENT_ID = "prod_client_id"
$env:MYINVOIS_CLIENT_SECRET = "prod_client_secret"

# Set SQL Server connection string
$env:SQL_CONNECTION_STRING = "Server=PROD_SQL;Database=SRX_AuditLog;Integrated Security=true;TrustServerCertificate=true;"

# Certificate password is retrieved from Windows Credential Manager (not environment variable)
# It was stored during infrastructure setup: cmdkey /add:MyInvoisCert /user:admin /pass:*
```

Or update `appsettings.Production.json`:
```json
{
  "ConnectionStrings": {
    "AuditLog": "Server=PROD_SQL;Database=SRX_AuditLog;Integrated Security=true;TrustServerCertificate=true;"
  },
  "CertificateSettings": {
    "StoragePath": "C:\\Certs\\MyInvois\\myinvois-cert.pfx",
    "PasswordReference": "Credential:MyInvoisCert",
    "Thumbprint": "FROM_FINANCE_DOCUMENTATION",
    "ValidityCheckIntervalDays": 14
  }
}
```

**Certificate verification:**
```powershell
# Verify certificate file exists and is readable
Test-Path "C:\Certs\MyInvois\myinvois-cert.pfx"

# Verify directory is encrypted
cipher /s:"C:\Certs\MyInvois"

# Verify NTFS permissions are restricted
icacls "C:\Certs\MyInvois" /T
```

### 4. Start Service

```powershell
# Option A: As Console App (for initial testing)
C:\Services\MyInvois-Service\MyInvois.Service.exe

# Option B: As Windows Service (Phase 2)
# New-Service -Name "MyInvois-Service" -BinaryPathName "C:\Services\MyInvois-Service\MyInvois.Service.exe" -Credential (Get-Credential)
# Start-Service -Name "MyInvois-Service"
```

**Expected:** Service logs startup messages (check console or event log)

### 5. Test Certificate & Signature Generation

Before production submission, validate the certificate works correctly:

```powershell
# Test 1: Verify certificate is accessible
$certPath = "C:\Certs\MyInvois\myinvois-cert.pfx"
$password = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
  [System.Runtime.InteropServices.Marshal]::SecureStringToCoTaskMemUnicode(
    (Get-StoredCredential -Target 'MyInvoisCert').Password
  )
)

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
  $certPath,
  $password
)

Write-Host "✓ Certificate Details:"
Write-Host "  Subject: $($cert.Subject)"
Write-Host "  Thumbprint: $($cert.Thumbprint)"
Write-Host "  Valid From: $($cert.NotBefore)"
Write-Host "  Expires: $($cert.NotAfter)"
Write-Host "  Has Private Key: $($cert.HasPrivateKey)"
```

**Expected output:**
```
✓ Certificate Details:
  Subject: CN=MyCompany, O=Organization, C=MY
  Thumbprint: [40-character hex string]
  Valid From: 2/18/2026
  Expires: 2/18/2027 (or later)
  Has Private Key: True
```

### 5.5: Test Signature Generation in MyInvois Sandbox

```powershell
# Generate test invoice XML (sample UBL 2.1)
$testInvoiceXml = @'
<?xml version="1.0" encoding="UTF-8"?>
<Invoice>
  <InvoiceNumber>TEST-2024-001</InvoiceNumber>
  <InvoiceDate>2024-02-23</InvoiceDate>
  <SupplierTIN>123456789012</SupplierTIN>
  <Amount>100.00</Amount>
</Invoice>
'@

# Sign the invoice
$signature = [System.Convert]::ToBase64String(
  $cert.PrivateKey.SignData(
    [System.Text.Encoding]::UTF8.GetBytes($testInvoiceXml),
    "SHA256"
  )
)

Write-Host "✓ Signature generated successfully"
Write-Host "  Length: $($signature.Length) characters"

# Submit to MyInvois SANDBOX (not production)
$body = @{
  invoiceXml = $testInvoiceXml
  signature = $signature
  signatureMethod = "xmldsig"
} | ConvertTo-Json

$response = Invoke-WebRequest `
  -Uri "https://sandbox.myinvois.hasil.gov.my/api/v1/submission" `
  -Method POST `
  -Body $body `
  -ContentType "application/json" `
  -Headers @{
    Authorization = "Bearer [OAuth-Token-From-Finance]"
  }

if ($response.StatusCode -eq 200) {
  Write-Host "✓ Sandbox submission successful"
  Write-Host "  Submission UID: $(($response.Content | ConvertFrom-Json).uid)"
}
else {
  Write-Host "✗ Sandbox submission failed: $($response.StatusCode)"
  Write-Host "  Response: $($response.Content)"
}
```

**Expected:**
```
HTTP 200 OK
{
  "uid": "2024-02-23-001-12345",
  "status": "ACCEPTED",
  "timestamp": "2024-02-23T14:30:00Z",
  "message": "Invoice submission successful"
}
```

### 6. Verify Service Health

```powershell
# Check audit table
sqlcmd -S PROD_SQL -d SRX_AuditLog -Q "SELECT COUNT(*) FROM [dbo].[AuditLog];" -h -1 -W
```

**Expected:** Returns 0 (empty table ready for submissions)

---

## MVAI Go-Live Procedure (Feb 28, 2026)

### Morning Briefing (8:00 AM)

- [ ] Confirm all systems online (MOVEX DB2/AS400, MyInvois sandbox, SQL Server)
- [ ] Verify no pending issues from UAT
- [ ] Distribute escalation contacts

### Dry Run (9:00 AM)

```powershell
# Process 10 test invoices (no actual MyInvois submission)
dotnet MyInvois.Service.exe --process-batch --dry-run --max-invoices 10
```

**Expected:** All invoices validate successfully, audit log populated

### Production Submission (10:00 AM)

```powershell
# Process February invoices (submit to MyInvois production)
dotnet MyInvois.Service.exe --process-monthly-batch --environment production
```

**Monitor in real-time:**
```sql
-- Check successful submissions
SELECT TOP 20 [InvoiceNumber], [Status], [MyInvoisUUID], [Timestamp]
FROM [dbo].[AuditLog]
WHERE [Category] = 'MyInvois' AND [Action] = 'MyInvois_Submit'
ORDER BY [Timestamp] DESC;

-- Check failed submissions
SELECT [InvoiceNumber], [ErrorMessage], [RetryCount], [Timestamp]
FROM [dbo].[AuditLog]
WHERE [Category] = 'MyInvois' AND [Status] = 'Failed'
ORDER BY [Timestamp] DESC;

-- View summary
SELECT [Status], COUNT(*) AS [Count]
FROM [dbo].[AuditLog]
WHERE [Category] = 'MyInvois' AND [Action] = 'MyInvois_Submit'
GROUP BY [Status];
```

**Success Criteria:**
- ✅ ≥95% submissions successful
- ✅ All submissions logged in audit table
- ✅ No unexpected errors
- ✅ MyInvois UUIDs returned for successful submissions

### Post-Live Monitoring (2-4 PM)

Check every 30 minutes:
```sql
SELECT 
    [Status], 
    COUNT(*) AS [Count],
    CAST(COUNT(*) * 100.0 / SUM(COUNT(*)) OVER () AS DECIMAL(5,2)) AS [Percentage]
FROM [dbo].[AuditLog]
WHERE [Category] = 'MyInvois' 
  AND [Timestamp] > DATEADD(HOUR, -4, GETUTCDATE())
GROUP BY [Status];
```

### Sign-Off (5:00 PM)

- [ ] All invoices submitted
- [ ] Success rate ≥95%
- [ ] No critical errors
- [ ] **Notify Finance team** of completion

---

## Rollback Procedure (If Needed)

If critical issues arise during go-live:

### Step 1: Stop Service

```powershell
Stop-Service -Name "MyInvois-Service" -Force
```

### Step 2: Revert to Pre-MVAI State

```powershell
# Delete audit log (preserve backup)
sqlcmd -S PROD_SQL -d SRX_AuditLog -Q "TRUNCATE TABLE [dbo].[AuditLog];"

# Restore previous version (if applicable)
# Copy previous build to C:\Services\MyInvois-Service\
```

### Step 3: Notify Stakeholders

- Contact: Finance Manager, IT Manager, Architecture Team
- Document: What went wrong, what actions taken, next steps

### Step 4: Post-Incident Review

Schedule within 48 hours to analyze root cause and implement preventive measures.

---

## Monitoring & Alerting (Phase 1 + Production)

### Certificate Expiry Monitoring (CRITICAL)

Daily automated check for certificate validity:

```powershell
# File: C:\MyInvois-Service\CertificateExpiryCheck.ps1
# Scheduled: Daily at 06:00 AM via Task Scheduler

$certPath = "C:\Certs\MyInvois\myinvois-cert.pfx"
$password = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(...)
$logFile = "C:\Logs\CertificateMonitoring.log"

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
  $certPath,
  $password
)

$expiryDate = $cert.NotAfter
$daysUntilExpiry = ($expiryDate - (Get-Date)).Days

Add-Content $logFile "$(Get-Date): Certificate expires in $daysUntilExpiry days"

# ALERT THRESHOLDS
if ($daysUntilExpiry -lt 0) {
  # CRITICAL: Certificate expired
  Send-EmailAlert -Subject "CRITICAL: MyInvois Certificate EXPIRED" -Severity "Critical"
  Stop-Service -Name "MyInvois-Service" -Force
}
elseif ($daysUntilExpiry -lt 7) {
  Send-EmailAlert -Subject "EMERGENCY: Certificate expires in $daysUntilExpiry days" -Severity "Critical"
}
elseif ($daysUntilExpiry -lt 14) {
  Send-EmailAlert -Subject "CRITICAL: Certificate renewal needed" -Severity "High"
}
elseif ($daysUntilExpiry -lt 30) {
  Send-EmailAlert -Subject "WARNING: Certificate renewal due" -Severity "Medium"
}
elseif ($daysUntilExpiry -lt 60) {
  Write-Host "🟡 NOTICE: Certificate expires in $daysUntilExpiry days"
}
elseif ($daysUntilExpiry -lt 90) {
  Write-Host "ℹ️ INFO: Certificate valid for $daysUntilExpiry days"
}
```

**Alert Recipients:** infrastructure@company.com, secops@company.com, finance@company.com

**Alert Schedule:**
| Threshold | Action |
|-----------|--------|
| 120 days before | Planning reminder to Finance (budget review) |
| 90 days before | Procurement initiation reminder |
| 60 days before | Budget approval deadline |
| 30 days before | Renewal escalation to Finance |
| 14 days before | ALERT: High priority renewal (Infrastructure lead) |
| 7 days before | ALERT: CRITICAL - Escalate to executive |
| 0 days | EMERGENCY - Stop all submissions, activate incident |

### Application Insights (Phase 1: Manual, Phase 2: Automated)

```csharp
// Add to Program.cs (Phase 2)
builder.Services.AddApplicationInsightsTelemetry();
```

### Manual Monitoring (Current)

Set up recurring SQL Agent jobs:

```sql
-- Job: Check failed submissions (hourly)
EXEC sp_add_job @job_name = 'MyInvois_FailureCheck';
EXEC sp_add_jobstep @job_name = 'MyInvois_FailureCheck',
    @command = 'sqlcmd -S ? -d SRX_AuditLog -Q "SELECT COUNT(*) FROM [dbo].[AuditLog] WHERE [Status] = ''Failed'' AND [Timestamp] > DATEADD(HOUR, -1, GETUTCDATE());"';
EXEC sp_add_schedule @schedule_name = 'Hourly', @freq_type = 4, @freq_interval = 1;
EXEC sp_attach_schedule @job_name = 'MyInvois_FailureCheck', @schedule_name = 'Hourly';
```

### Manual Monitoring (Current)

Set up recurring SQL Agent jobs:

```sql
-- Job: Check failed submissions (hourly)
EXEC sp_add_job @job_name = 'MyInvois_FailureCheck';
EXEC sp_add_jobstep @job_name = 'MyInvois_FailureCheck',
    @command = 'sqlcmd -S ? -d SRX_AuditLog -Q "SELECT COUNT(*) FROM [dbo].[AuditLog] WHERE [Status] = ''Failed'' AND [Timestamp] > DATEADD(HOUR, -1, GETUTCDATE());"';
EXEC sp_add_schedule @schedule_name = 'Hourly', @freq_type = 4, @freq_interval = 1;
EXEC sp_attach_schedule @job_name = 'MyInvois_FailureCheck', @schedule_name = 'Hourly';
```

### Backup & Disaster Recovery

#### Daily Audit Log Backup

```powershell
# Automated scheduled backup (daily at 22:00)
Backup-SqlDatabase -ServerInstance "PROD_SQL" `
  -Database "SRX_AuditLog" `
  -BackupFile "\\backup-server\MyInvois\SRX_AuditLog_$(Get-Date -Format 'yyMMdd').bak" `
  -CompressionOption On

# Verify backup
Restore-SqlDatabase -ServerInstance "PROD_SQL" `
  -Database "SRX_AuditLog_Verify" `
  -BackupFile "\\backup-server\MyInvois\SRX_AuditLog_$(Get-Date -Format 'yyMMdd').bak" `
  -Replace
```

#### Certificate Backup (Encrypted)

```powershell
# Create encrypted backup (quarterly or after certificate renewal)
$source = "C:\Certs\MyInvois\myinvois-cert.pfx"
$backup = "\\backup-server\Certificates\MyInvois\myinvois-cert.BACKUP.7z"
$archivePassword = "BackupPassword"  # Different from certificate password

# Encrypt with 7-Zip AES-256
7z a -tzip -mem=AES256 -p$archivePassword "$backup" "$source"

# Verify
7z l "$backup"
```

**Backup Location Requirements:**
- [ ] Physically separate location (different building/server)
- [ ] Encrypted with AES-256 or stronger
- [ ] Different password than certificate itself
- [ ] Limited access (2-3 authorized people only)
- [ ] Off-site copy (geographic disaster recovery)
- [ ] Documented in Disaster Recovery Plan

#### Disaster Recovery Test

**Schedule:** Every 6 months (August 1 & February 1)

```powershell
# Step 1: Extract backup to temporary location
7z x "\\backup-server\Certificates\MyInvois\myinvois-cert.BACKUP.7z" `
  -o"C:\Temp\CertRecoveryTest\" `
  -p$archivePassword

# Step 2: Verify certificate loads
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
  "C:\Temp\CertRecoveryTest\myinvois-cert.pfx",
  "CertificatePassword"
)
Write-Host "✓ Certificate restored successfully"
Write-Host "  Expires: $($cert.NotAfter)"

# Step 3: Test signature generation
$signature = [System.Convert]::ToBase64String(
  $cert.PrivateKey.SignData(
    [System.Text.Encoding]::UTF8.GetBytes("test"),
    "SHA256"
  )
)
Write-Host "✓ Signature generated from restored certificate"

# Step 4: Cleanup
Remove-Item "C:\Temp\CertRecoveryTest\" -Recurse -Force

# Step 5: Document results
Add-Content "C:\Logs\DR_Test_Log.txt" "$(Get-Date): DR test passed, restored certificate functional"
```

---

## Operational Support

### On-Call Escalation

| Time | Contact | Role |
|------|---------|------|
| 9-17 (Weekday) | Dev Team | Troubleshoot service code |
| 17-22 (Weekday) | IT Ops | Server/database issues |
| 22-9 (Off-hours) | On-Call (rotating) | Incident response |

### Common Commands

```powershell
# Check service status
Get-Service -Name "MyInvois-Service"

# View recent logs (file-based)
Get-Content C:\Logs\MyInvois-Service\*.log -Tail 50

# Manually trigger batch (testing)
C:\Services\MyInvois-Service\MyInvois.Service.exe --process-batch --test

# Retry failed invoices
sqlcmd -S PROD_SQL -d SRX_AuditLog -i .\scripts\retry-failed-invoices.sql
```

---

## Documentation & Runbooks

- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) — Common issues and fixes
- [03-myinvois-requirements.md](../ai/memory/03-myinvois-requirements.md) — Validation rules & traceability
- [SQL query reference](./sql-queries.md) — Common audit log queries

---

## Post-Go-Live (Week of Mar 3)

### Phase 2 Planning

- Implement Portal UI dashboard
- Add automatic retry with circuit breaker
- Deploy to production from staging

### Performance Review

- Analyze submission success rates
- Identify bottlenecks
- Plan optimizations

---

**Deployment Owner:** IT Ops  
**Review Date:** February 21, 2026 (1 week before go-live)  
**Next Update:** Post-MVAI lessons learned

---

**Questions?** Contact the DevOps Team or Development Manager.
