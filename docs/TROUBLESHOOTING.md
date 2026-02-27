# MyInvois-Service — Troubleshooting Guide

**Purpose:** Diagnose and resolve common issues  
**Audience:** Operations, Support Team, Developers

---

## 🔐 Certificate Issues

### "Certificate File Not Found"

```
Error: File not found at C:\Certs\MyInvois\myinvois-cert.pfx
```

**Diagnosis:**
```powershell
# Check if file exists
Test-Path "C:\Certs\MyInvois\myinvois-cert.pfx"

# Check directory permissions
icacls "C:\Certs\MyInvois"

# Verify service account can read
whoami /priv
```

**Solutions:**
1. Verify certificate file was copied from Finance (should be ~3-5 KB)
2. Check directory path matches appsettings.json `CertificateSettings:StoragePath`
3. Verify directory encryption is not preventing access:
   ```powershell
   cipher /s:"C:\Certs\MyInvois"
   ```
4. Restart service after confirming file exists

---

### "Certificate Password Incorrect"

```
Error: The supplied password is incorrect when loading certificate
```

**Diagnosis:**
```powershell
# Test certificate password retrieval
$credManager = Get-StoredCredential -Target 'MyInvoisCert'
Write-Host "Password stored: $($credManager -ne $null)"

# Test certificate load
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
  "C:\Certs\MyInvois\myinvois-cert.pfx",
  "TestPassword"
)
```

**Solutions:**
1. Request correct password from Finance (via secure channel, not email)
2. Update Windows Credential Manager:
   ```powershell
   cmdkey /delete:MyInvoisCert
   cmdkey /add:MyInvoisCert /user:admin /pass:*
   # System will prompt for password securely
   ```
3. Restart service
4. If still fails, request new certificate copy from Finance

---

### "Certificate Expired"

```
Error: Certificate validity period has ended
OR
Error: Certificate_InValid_For_Usage
```

**Diagnosis:**
```sql
-- Check if certificate expiry alert was triggered
SELECT TOP 10 * FROM [dbo].[AuditLog]
WHERE [Action] LIKE '%Certificate%Expired%'
ORDER BY [Timestamp] DESC;

-- Check monitoring logs
Get-Content "C:\Logs\CertificateMonitoring.log" | Tail -5
```

**Solutions:**
1. **Check expiry date:**
   ```powershell
   $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
     "C:\Certs\MyInvois\myinvois-cert.pfx",
     "Password"
   )
   Write-Host "Expires: $($cert.NotAfter)"
   ```

2. **If expired:**
   - [ ] Stop all invoice submissions (pause batch job)
   - [ ] Contact Finance to issue renewed certificate
   - [ ] Replace certificate file in C:\Certs\MyInvois\
   - [ ] Test with sandbox before resuming production
   - [ ] Resume batch processing

3. **If discovered late (within 24 hours of expiry):**
   - [ ] ESCALATE to Executive Sponsor immediately
   - [ ] Activate incident response
   - [ ] Plan emergency renewal or use backup certificate (if available)

**Prevention:**
- Ensure daily expiry monitoring is running
- Check alert emails are being sent (verify distribution list)
- Finance should have renewal budget approved 90 days before expiry

---

### "Certificate Password Stored Incorrectly"

```
Error: Cannot retrieve certificate password from credential storage
OR
Service crashes with: Unexpected error retrieving credentials
```

**Diagnosis:**
```powershell
# Check if credential exists in manager
cmdkey /list | Select-String "MyInvoisCert"

# Test credential retrieval
$cred = Get-StoredCredential -Target 'MyInvoisCert'
Write-Host "Credential found: $($cred -ne $null)"
```

**Solutions:**
1. **Re-store credential securely:**
   ```powershell
   # Delete old entry
   cmdkey /delete:MyInvoisCert
   
   # Create new entry (will prompt for password)
   cmdkey /add:MyInvoisCert /user:admin /pass:*
   ```

2. **Verify service account can access:**
   ```powershell
   # Run as service account (NT SERVICE\MyInvoisAppPool)
   runas /user:NT SERVICE\MyInvoisAppPool "cmdkey /list"
   ```

3. **If credential manager not available, use DPAPI:**
   ```powershell
   # Encrypt password using DPAPI
   $password = "CertificatePassword"
   $secureString = ConvertTo-SecureString $password -AsPlainText -Force
   $encrypted = ConvertFrom-SecureString $secureString
   
   # Store encrypted value in secure configuration file
   # Application decrypts at runtime
   ```

---

### "Unauthorized Access to Certificate"

```
Alert: Multiple failed attempts to access certificate file
OR
Warning: Unknown process accessing C:\Certs\MyInvois\
```

**Diagnosis & Escalation:**
This is a **SECURITY INCIDENT** - investigate immediately.

```powershell
# Check Windows Event Log for failed access attempts
Get-EventLog -LogName Security | Where-Object {
  $_.EventID -eq 4659 -and
  $_.Message -like "*Certs*MyInvois*"
} | Format-List TimeGenerated, Message, TargetUserName

# Check file access audit log
auditpol /set /subcategory:"File System" /success:enable /failure:enable
```

**Immediate Actions:**
1. **Isolate the server** if unauthorized access confirmed
2. **Revoke certificate access:**
   ```powershell
   icacls "C:\Certs\MyInvois" /grant:r "NULL:(N)"
   ```
3. **Restore from backup:**
   ```powershell
   # Extract from encrypted backup
   7z x "\\backup-server\Certificates\MyInvois\myinvois-cert.BACKUP.7z" ...
   ```
4. **Escalate to CISO / Incident Response Team** with:
   - Full Windows Event Log export
   - List of affected systems
   - Timeline of unauthorized access attempts

---

## Service Won't Start

### Symptom
```
Error: Connection refused or service crashes on startup
```

### Diagnosis

1. **Check configuration:**
   ```powershell
   # Verify appsettings.json syntax
   Get-Content .\appsettings.Production.json | ConvertFrom-Json
   ```

2. **Check SQL Server connection:**
   ```powershell
   sqlcmd -S YOUR_SQL_SERVER -d SRX_AuditLog -Q "SELECT 1;"
   ```

3. **Check certificate configuration (production):**
   ```powershell
   # Verify certificate file exists
   Test-Path "C:\Certs\MyInvois\myinvois-cert.pfx"
   
   # Verify password in credential manager
   cmdkey /list | Select-String "MyInvoisCert"
   
   # Test certificate load
   $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
     "C:\Certs\MyInvois\myinvois-cert.pfx",
     "Password"
   )
   ```

4. **Check file permissions:**
   - Service account must have read access to `appsettings.Production.json`
   - Service account must have read access to `C:\Certs\MyInvois\myinvois-cert.pfx`
   - Service account must have write access to log directory

### Solutions

| Issue | Fix |
|-------|-----|
| "Connection timeout" | Verify SQL Server is running; check firewall rules |
| "Login failed" | Verify Integrated Security enabled; check service account |
| "Database does not exist" | Run `create-audit-table.sql` first |
| "Certificate file not found" | Verify cert copied to `C:\Certs\MyInvois\`, check path in appsettings.json |
| "Certificate password incorrect" | Verify password in Credential Manager, update via `cmdkey /add:MyInvoisCert` |
| "Cannot load certificate" | Verify certificate file not corrupted, restore from backup |
| "File not found" | Check working directory is correct (`C:\Services\MyInvois-Service\`) |
| "Access denied" | Check NTFS permissions on certificate directory and log directory |

---

## MOVEX DB2/AS400 Connection Errors

### "Connection refused" or "Communication link failure"

```
Unable to connect to IBM DB2/AS400 server
```

**Diagnosis:**
```powershell
# Test connectivity to AS400
Test-NetConnection YOUR_AS400_SERVER -Port 446

# Verify DB2 driver is installed
dotnet list package | Select-String "IBM.Data.Db2"
```

**Solutions:**
1. Verify AS400 server hostname and port in `MovexDb:ConnectionString`
2. Check network connectivity (ping, tracert to AS400)
3. Verify firewall allows DB2 ports (446 or 8471)
4. Confirm IBM DB2 driver (`Net.IBM.Data.Db2`) is installed

---

### "Authorization failure" (DB2 Login)

```
DB2 authentication failed - invalid credentials
```

**Diagnosis:**
```powershell
# Check DB2 credentials in User Secrets
dotnet user-secrets list | Select-String "MovexDb"
```

**Solutions:**
1. Update DB2 credentials in User Secrets:
   ```powershell
   dotnet user-secrets set "MovexDb:ConnectionString" "Server=YOUR_AS400;Database=YOUR_DB;UserID=your-user;Password=new-password;"
   ```
2. Restart service
3. Contact IT Ops if AS400 account is locked or expired

---

### "SQL0204N - Table not found"

```
Table or view not found in specified schema
```

**Diagnosis:**
- Verify schema names in `appsettings.json`: `MovexDb:SchemaData` (mvxcdta) and `MovexDb:SchemaProgram` (mvxc300)
- Confirm MOVEX tables exist: `fpledg` (AP), `fsledg` (AR), `fgledg` (GL)

**Solutions:**
1. Verify correct schema names for your MOVEX environment
2. Check table permissions for the DB2 service account
3. Increase `CommandTimeoutSeconds` if queries are slow on large tables

---

## MyInvois API Errors

### "Invalid Client" (OAuth 401)

```
ClientId or ClientSecret rejected
```

**Diagnosis:**
```powershell
# Verify credentials
dotnet user-secrets list | Select-String "MyInvoisApi"

# Check credential format (should not have spaces)
```

**Solutions:**
1. Re-generate credentials in MyInvois sandbox portal
2. Update User Secrets:
   ```powershell
   dotnet user-secrets set "MyInvoisApi:ClientId" "new_id"
   dotnet user-secrets set "MyInvoisApi:ClientSecret" "new_secret"
   ```
3. Restart service

---

### "Rate Limit Exceeded" (HTTP 429)

```
Too many requests (100 per minute limit)
```

**Diagnosis:**
```sql
-- Check submission rate
SELECT 
    DATEPART(MINUTE, [Timestamp]) AS [Minute],
    COUNT(*) AS [SubmissionCount]
FROM [dbo].[AuditLog]
WHERE [Category] = 'MyInvois'
  AND [Timestamp] > DATEADD(MINUTE, -5, GETUTCDATE())
GROUP BY DATEPART(MINUTE, [Timestamp])
ORDER BY [Minute] DESC;
```

**Solutions:**
1. Increase delay between batches in `appsettings.json`:
   ```json
   "BatchProcessing": {
     "DelayBetweenBatchesMs": 1000  // Increase from 600
   }
   ```
2. Reduce batch size:
   ```json
   "PurchaseBatchSize": 25  // Reduce from 50
   ```
3. Spread submission over longer period

---

### "Duplicate Submission" (HTTP 400, code DS302)

```
Invoice already submitted successfully
```

**Diagnosis:**
```sql
-- Query duplicate submissions
SELECT [InvoiceNumber], COUNT(*) AS [SubmissionCount]
FROM [dbo].[AuditLog]
WHERE [Category] = 'MyInvois'
  AND [Status] = 'Success'
GROUP BY [InvoiceNumber]
HAVING COUNT(*) > 1;
```

**Solutions:**
1. **Expected**: MyInvois API correctly prevents duplicates
2. Add invoice to skip list:
   ```sql
   INSERT INTO [dbo].[AuditLog] ([Action], [Status], [InvoiceNumber], ...)
   VALUES ('MyInvois_Skip', 'Success', 'INV-2026-00001', ...);
   ```
3. Manual review required before retry

---

## Validation Errors

### "Mandatory Field Missing"

```
Supplier TIN, Invoice Date, or other required field not found
```

**Diagnosis:**
```sql
-- Find invoices with validation errors
SELECT [InvoiceNumber], [ValidationErrors], [Timestamp]
FROM [dbo].[AuditLog]
WHERE [Status] = 'Failed'
  AND [ValidationErrors] IS NOT NULL
ORDER BY [Timestamp] DESC
LIMIT 10;
```

**Solutions:**
1. **Sales/Purchase field mapping**: Update `MyInvoiceMapper.cs` to populate missing field from MOVEX
2. **Default value**: Add null-coalescing in mapper:
   ```csharp
   document.BuyerTIN = invoice.Buyer?.TIN ?? "UNKNOWN";
   ```
3. **Data quality**: Contact Finance team to verify MOVEX data

---

### "Invalid TIN Format"

```
TIN does not match Malaysian format (12 alphanumeric)
```

**Diagnosis:**
```sql
-- Find invalid TINs
SELECT DISTINCT [SupplierTIN], [BuyerTIN]
FROM [dbo].[AuditLog]
WHERE [ValidationErrors] LIKE '%TIN%'
  AND [Timestamp] > DATEADD(DAY, -7, GETUTCDATE());
```

**Solutions:**
1. **Correct data source**: Update company master TIN in MOVEX
2. **Bypass validation** (NOT RECOMMENDED): Remove from validation in `MandatoryFieldsValidator.cs`
3. **Manual correction**: Approve TIN mapping in special cases

---

### "Exchange Rate Missing"

```
Non-MYR currency without exchange rate
```

**Diagnosis:**
```sql
-- Find missing exchange rates
SELECT [InvoiceNumber], [CurrencyCode], [ExchangeRate]
FROM [dbo].[AuditLog]
WHERE [CurrencyCode] != 'MYR'
  AND ([ExchangeRate] IS NULL OR [ExchangeRate] = 1.0)
  AND [Timestamp] > DATEADD(DAY, -7, GETUTCDATE());
```

**Solutions:**
1. **Get rate from MOVEX**: Ensure OINVOH.ARAT is populated in source invoice
2. **Default exchange rate** (temporary): Add default in mapper:
   ```csharp
   document.ExchangeRate = invoice.ExchangeRate > 0 ? invoice.ExchangeRate : 1.0m;
   ```
3. **Disable for MYR-only**: If company operates only in MYR, set in validation config

---

## Database Issues

### "Cannot connect to SQL Server"

```
Connection timeout or login failed
```

**Diagnosis:**
```powershell
# Test SQL Server port
Test-NetConnection YOUR_SQL_SERVER -Port 1433

# Test connection string
sqlcmd -S YOUR_SQL_SERVER -U sa -P password -Q "SELECT @@VERSION;"
```

**Solutions:**
1. Verify SQL Server is running:
   ```powershell
   Get-Service -Name "MSSQL*" | Start-Service
   ```
2. Check firewall (port 1433):
   ```powershell
   Get-NetFirewallRule -DisplayName "*SQL*"
   ```
3. Verify connection string in `appsettings.json`

---

### "Audit Log Table Corrupt"

```
Error reading/writing to [dbo].[AuditLog]
```

**Diagnosis:**
```sql
-- Check table integrity
DBCC CHECKTABLE ([dbo].[AuditLog]);
```

**Solutions:**
1. **Repair database:**
   ```sql
   ALTER DATABASE SRX_AuditLog SET SINGLE_USER;
   DBCC CHECKDB (SRX_AuditLog, REPAIR_REBUILD);
   ALTER DATABASE SRX_AuditLog SET MULTI_USER;
   ```
2. **Restore from backup:**
   ```sql
   RESTORE DATABASE SRX_AuditLog FROM DISK = 'C:\Backups\SRX_AuditLog.bak';
   ```
3. **Escalate to DBA team**

---

## Submission Failures

### View Failed Submissions

```sql
-- Query failed submissions (most recent first)
SELECT 
    [AuditId],
    [InvoiceNumber],
    [ErrorMessage],
    [ErrorCode],
    [RetryCount],
    [Timestamp]
FROM [dbo].vw_MyInvois_FailedSubmissions
ORDER BY [Timestamp] DESC
LIMIT 20;

-- Or use view
SELECT * FROM [dbo].vw_MyInvois_FailedSubmissions
WHERE [HoursSinceFailed] < 24;
```

### Retry Failed Invoice

**Manual retry via audit log:**

```sql
-- Step 1: Identify failed submission
SELECT [AuditId], [InvoiceNumber], [ErrorMessage]
FROM [dbo].[AuditLog]
WHERE [Status] = 'Failed'
  AND [InvoiceNumber] = 'INV-2026-00001';

-- Step 2: Update retry count
UPDATE [dbo].[AuditLog]
SET [RetryCount] = [RetryCount] + 1,
    [Status] = 'Pending'
WHERE [AuditId] = 'YOUR_AUDIT_ID';

-- Step 3: Service reprocesses on next batch
```

**Automated retry (Phase 2):**
```powershell
# Future: Use portal UI or API
POST /api/myinvois/retry/{submissionId}
```

---

## Monitoring & Health Checks

### Certificate Expiry Monitoring Not Running

```
Warning: Certificate expiry check script not executing
OR: No certificate monitoring logs created for past 24 hours
```

**Diagnosis:**
```powershell
# Check if scheduled task exists
Get-ScheduledTask -TaskName "*MyInvois*" -ErrorAction SilentlyContinue

# Check task history
Get-ScheduledTaskInfo -TaskName "MyInvois-CertificateMonitoring"

# Check monitoring log
Get-Content "C:\Logs\CertificateMonitoring.log" -Tail 5
```

**Solutions:**
1. **Create scheduled task if missing:**
   ```powershell
   # Register scheduled task for daily certificate check
   $action = New-ScheduledTaskAction `
     -Execute "PowerShell.exe" `
     -Argument "C:\MyInvois-Service\CertificateExpiryCheck.ps1"
   
   $trigger = New-ScheduledTaskTrigger `
     -Daily -At 06:00AM
   
   Register-ScheduledTask `
     -TaskName "MyInvois-CertificateMonitoring" `
     -Action $action `
     -Trigger $trigger `
     -RunLevel Highest
   ```

2. **Verify task runs successfully:**
   ```powershell
   Start-ScheduledTask -TaskName "MyInvois-CertificateMonitoring"
   Get-ScheduledTaskInfo -TaskName "MyInvois-CertificateMonitoring"
   ```

3. **Check log file permissions:**
   ```powershell
   icacls "C:\Logs" /grant "NT SERVICE\SYSTEM:(OI)(CI)F"
   ```

---

### Alerts Not Being Sent

```
Certificate expiry date approaching, but no email alert received
```

**Diagnosis:**
```powershell
# Check if alert function is working
Test-NetConnection smtp.company.com -Port 587

# Check SMTP credentials
$smtp = [System.Net.Mail.SmtpClient]::new("smtp.company.com")
$smtp.Credentials = New-Object System.Net.NetworkCredential("user", "pass")

# Verify email distribution list exists
Get-DistributionGroup -Identity "infrastructure@company.com"
```

**Solutions:**
1. **Verify SMTP server settings in script:**
   ```powershell
   # In CertificateExpiryCheck.ps1, check:
   Send-MailMessage -SmtpServer "smtp.company.com" -UseSsl -Port 587
   ```

2. **Test alert manually:**
   ```powershell
   Send-MailMessage `
     -To "infrastructure@company.com" `
     -Subject "Test Alert: MyInvois Certificate" `
     -Body "This is a test alert from certificate monitoring" `
     -SmtpServer "smtp.company.com" `
     -From "monitoring@company.com" `
     -UseSsl -Port 587
   ```

3. **Verify email distribution list:**
   ```powershell
   Get-DistributionGroupMember -Identity "infrastructure@company.com"
   ```

4. **Confirm no mail filtering rules blocking alerts:**
   - Check Exchange Transport Rules
   - Verify sender address is allowed
   - Check recipient inbox rules (not moved to spam)

---

## Daily Health Check

```powershell
# Run this every morning
./scripts/daily-health-check.ps1

# Or manually:
sqlcmd -S YOUR_SQL_SERVER -d SRX_AuditLog -i .\scripts\health-check.sql

# Specific checks:
# 1. Certificate still valid
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
  "C:\Certs\MyInvois\myinvois-cert.pfx",
  "Password"
)
Write-Host "Days until expiry: $(($cert.NotAfter - (Get-Date)).Days)"

# 2. Service running
Get-Service -Name "MyInvois-Service"

# 3. Audit log accessible
sqlcmd -S YOUR_SQL_SERVER -d SRX_AuditLog -Q "SELECT COUNT(*) FROM [dbo].[AuditLog];"
```

### Monthly Metrics

```sql
-- Monthly summary
SELECT [Year], [Month], [InvoiceType], [TotalInvoices], [SuccessCount], [FailedCount], [SuccessRate]
FROM [dbo].vw_MyInvois_MonthlySummary
ORDER BY [Year] DESC, [Month] DESC;
```

---

## Getting Help

### Escalation Path

1. **Check this guide** (Troubleshooting.md)
2. **Query audit logs** (see SQL examples above)
3. **Check MyInvois status** (https://myinvois.hasil.gov.my/status)
4. **Contact IT Ops** (if database/infrastructure issue)
5. **Contact Dev Team** (if code issue)

### Support Contacts

- **Finance**: Invoice-related issues
- **IT Ops**: Server, database, network
- **Dev Team**: Code, validation, mapping issues
- **IT Manager**: Escalation for critical issues

---

## Related Documentation

- [DEPLOYMENT.md](DEPLOYMENT.md) — Production deployment guide
- [README.md](../README.md) — Project overview
- [03-myinvois-requirements.md](../ai/memory/03-myinvois-requirements.md) — Validation rules & traceability

---

**Last Updated:** February 16, 2026
**Owned By:** Operations Team  
**Review Cycle:** Monthly or as issues arise

---

**🚨 Critical Issue?** Contact IT Manager on-call immediately.
