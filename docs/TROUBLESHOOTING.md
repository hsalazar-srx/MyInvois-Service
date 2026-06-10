# MyInvois-Service — Troubleshooting Guide

**Purpose:** Diagnose and resolve common issues  
**Audience:** Operations, Support Team, Developers

---

## Scheduler / Daily Batch Not Running

### Overnight batch never fires — app shuts down before 02:00

**Symptom:** Manual batch via `POST /api/v1/batch/process-range` works fine. Log shows `[DailyBatch] Next run in XXX minutes` at startup, then `Application is shutting down` and `[DailyBatch] Scheduler stopped` ~20 minutes after the last HTTP request.

**Cause:** IIS default idle timeout is **20 minutes**. If no HTTP requests arrive, IIS shuts down the worker process — killing the `BackgroundService` scheduler before it reaches 02:00.

**Diagnosis:**
```powershell
# Check idle timeout setting
& "$env:windir\system32\inetsrv\appcmd.exe" list apppool "MyInvoisAPI" /processModel.idleTimeout
# If output shows 00:20:00 — this is the problem
```

**Fix:**
```powershell
# Set idle timeout to 0 (never shut down due to inactivity)
& "$env:windir\system32\inetsrv\appcmd.exe" set apppool "MyInvoisAPI" /processModel.idleTimeout:"00:00:00"

# Disable periodic recycling (default 1740 min / 29 hours — can also kill scheduler mid-run)
& "$env:windir\system32\inetsrv\appcmd.exe" set apppool "MyInvoisAPI" /recycling.periodicRestart.time:"00:00:00"

# Recycle to apply
& "$env:windir\system32\inetsrv\appcmd.exe" recycle apppool /apppool.name:"MyInvoisAPI"

# Verify scheduler is armed
Get-Content "C:\inetpub\wwwroot\MyInvois-Api\logs\stdout*.log" |
    Select-String "DailyBatch" | Select-Object -Last 5
# Expected: [DailyBatch] Next run in XXX minutes (02:00 local)
```

**Also set in IIS Manager:** Application Pools → MyInvoisAPI → Advanced Settings → **Idle Time-out = 0**, **Regular Time Interval = 0**, **Start Mode = AlwaysRunning**.

---

## Startup & DI Errors

### App returns 500 on first request but health endpoint works

**Symptom:** `GET /api/v1/health` returns 200. `POST /api/v1/batch/process-range` returns 500 with empty body.

**Cause:** A DI registration is missing. The health endpoint is a `MapGet` lambda (no DI), so it works. Controllers fail when the DI container tries to resolve a missing dependency at first use.

**Fix:** Check the stdout log or Serilog log for `Unable to resolve service for type`:
```powershell
Get-ChildItem "C:\inetpub\wwwroot\MyInvois-Api\logs\stdout*.log" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 |
    Get-Content | Select-Object -Last 100
```

The `ValidateOnBuild = true` setting (added 2026-05-27) means the app will now **crash at startup** instead of at first request if a registration is missing. If the app pool starts but the health endpoint also 404s, check the stdout log for `AggregateException: Some services are not able to be constructed`.

---

### App returns 404 for all routes except /health (IIS)

**Symptom:** Health endpoint works. All controller routes return 404. IIS logs show 404 (not 401/500).

**Causes and fixes:**

| Cause | Fix |
|-------|-----|
| Wrong build deployed (old version without controllers) | Re-publish from latest source; verify `MyInvois.Api.dll` date |
| App crashed on startup; IIS holds the port but .NET is dead | Check stdout log; fix startup error; recycle app pool |
| Wrong port in request (e.g. 5000 instead of 5051) | Use port **5051** — `http://localhost:5051/api/v1/...` |

**UAT IIS site:** port `5051`, root site (no path prefix), physical path `C:\inetpub\wwwroot\MyInvois-Api`.

---

### 404 on batch endpoint but 401 appeared once without API key

This confirms the endpoint exists and auth works. The 404s are from a **stale build** on the server
(old DLL without `BatchController`). Re-publish from the latest source.

---

## 🔐 Certificate Issues

### Certificate loading under IIS — definitive approach (2026-06-01)

The UAT deployment revealed a cascade of certificate loading failures. The **correct and permanent solution** is to load the certificate from the **Windows Certificate Store by thumbprint**, not from the `.p12` file. This avoids all password delivery problems.

**Setup (one-time, per server):**

1. Import the `.p12` into the LocalMachine store:
   ```powershell
   certlm.msc  # Certificate Manager — Personal → Import
   ```

2. Grant the app pool read access to the private key:
   - In `certlm.msc`: Personal → Certificates → right-click SRX GLOBAL cert
   - All Tasks → Manage Private Keys → Add → `IIS AppPool\MyInvoisAPI` → Read → OK

3. Get the thumbprint:
   ```powershell
   $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("My","LocalMachine")
   $store.Open("ReadOnly")
   $store.Certificates | Where-Object { $_.Subject -like "*SRX*" } |
       Select-Object Thumbprint, HasPrivateKey, NotAfter
   $store.Close()
   ```

4. Set in `appsettings.json` on the server:
   ```json
   "MyInvoisApi": {
     "CertificateThumbprint": "A0E772A9F4EC1D26B732515A3430728E82D78FD7"
   }
   ```

No password needed. The code (`MyInvoiceSubmitter.LoadCertificate()`) tries thumbprint first, falls back to file path for local dev.

---

### CryptographicException: Bad Data / The system cannot find the file specified

**Root cause:** The certificate password contains `{` or `}` characters. ASP.NET Core config token substitution corrupts any value containing `{...}` when delivered via `appsettings.json`, `web.config` `<environmentVariables>`, or machine-level environment variables. The password arrives as empty or truncated → `Bad Data`.

**Symptom progression:**
- `The system cannot find the file specified` → `CngKey.Open` failed → IIS app pool has no user profile; private key stored in per-user CNG key store
- `Bad Data` → password is empty or corrupted
- `The specified network password is not correct` → password arrived but wrong value

**Do not attempt:**
- Storing password in `appsettings.json` (corrupted by token substitution if contains `{`)
- Storing password in `web.config` `<environmentVariables>` (same issue)
- Machine-level environment variables (IIS worker process may not inherit them correctly)
- User secrets when `ASPNETCORE_ENVIRONMENT != Development` (not loaded by default)

**Solution:** Use `CertificateThumbprint` (Windows Store) as above. Password not needed at all.

**If file-path loading is required** (dev only), the code supports `CertificatePasswordFile`:
```json
"MyInvoisApi": {
  "CertificatePasswordFile": "C:\\Certs\\MyInvois\\cert-password.txt"
}
```
Create the file with the raw password — no quotes, no newline issues:
```powershell
[System.IO.File]::WriteAllText("C:\Certs\MyInvois\cert-password.txt", 'your-password')
icacls "C:\Certs\MyInvois\cert-password.txt" /inheritance:r /grant "Administrators:R" /grant "IIS AppPool\MyInvoisAPI:R"
```

---

### "Certificate File Not Found"

**Diagnosis:**
```powershell
Test-Path "C:\Certs\MyInvois\SRX_GLOBAL_(MALAYSIA)_SDN._BHD..p12"
Get-ChildItem "C:\Certs\MyInvois\"
icacls "C:\Certs\MyInvois"
```

**Solutions:**
1. Verify the `.p12` file is at the configured `CertificatePath`
2. If using thumbprint (recommended), file path is irrelevant — confirm cert is in LocalMachine store:
   ```powershell
   certlm.msc  # Personal → Certificates
   ```

---

### "Certificate Expired"

```
Error: Certificate validity period has ended
OR
Error: Certificate_InValid_For_Usage
```

**Diagnosis:**
```powershell
# Check if certificate expiry alert was triggered
$db = "E:\data\audit.db"
& sqlite3 $db "SELECT AuditId, Action, Timestamp FROM AuditLogs WHERE Action LIKE '%Certificate%Expired%' ORDER BY Timestamp DESC LIMIT 10;"

# Check monitoring logs
Get-Content "C:\Logs\CertificateMonitoring.log" -Tail 5
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

2. **Check SQLite audit log is accessible:**
   ```powershell
   $db = "E:\data\audit.db"
   Test-Path $db
   & sqlite3 $db "SELECT COUNT(*) FROM AuditLogs;"
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
| `audit.db` not found | Check `./data/` directory exists and service account has write access; run service once to create |
| `audit.db` locked | Another process holds the file; check for hung service instances |
| "Database disk image is malformed" | Run `PRAGMA integrity_check;` — restore from backup if corrupt |
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
4. Confirm IBM DB2 iSeries Access ODBC driver is installed (check ODBC Data Sources in Windows)

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
```powershell
$db = "E:\data\audit.db"
# Check submission rate per minute (last 5 minutes)
& sqlite3 $db "SELECT strftime('%H:%M', Timestamp) AS Minute, COUNT(*) AS SubmissionCount FROM AuditLogs WHERE Category='MyInvois' AND datetime(Timestamp) > datetime('now', '-5 minutes') GROUP BY Minute ORDER BY Minute DESC;"
```

**Solutions:**
1. The Polly retry policy in `MyInvoiceSubmitter` automatically handles 429 responses
   with exponential backoff (3 attempts: 5s, 10s, 20s) — no config change needed
   for transient rate-limit hits.
2. If sustained rate limiting occurs, reduce the batch date range via the
   `POST /api/v1/batch/process-range` endpoint to spread submissions across multiple runs.
3. Check submission volume: LHDN limits are 300 req/min (submission), 600 req/min (status).
   At ~100 AR + 500-1000 AP/month the service operates well within these limits.

---

### "Duplicate Submission" (HTTP 400, code DS302)

```
Invoice already submitted successfully
```

**Diagnosis:**
```powershell
$db = "E:\data\audit.db"
# Query duplicate submissions
& sqlite3 $db "SELECT InvoiceNumber, COUNT(*) AS SubmissionCount FROM AuditLogs WHERE Category='MyInvois' AND Status='Success' GROUP BY InvoiceNumber HAVING COUNT(*) > 1;"
```

**Solutions:**
1. **Expected**: MyInvois API correctly rejects true duplicates (DS302)
2. The service has built-in duplicate detection via audit log check before submission  
   - This replaces the legacy SQL-based “skip list” mechanism; no manual SQL `INSERT` is required.
3. After manual review, if the original submission was valid, treat the invoice as **skipped** by not requeuing or retrying it  
   - Any subsequent attempt with the same `InvoiceNumber` will be rejected with DS302 by design.
4. Only retry after correcting the invoice (e.g., new invoice number or corrected data), and document the action in your incident/ticket.

---

## Validation Errors

### "Mandatory Field Missing"

```
Supplier TIN, Invoice Date, or other required field not found
```

**Diagnosis:**
```powershell
$db = "E:\data\audit.db"
# Find invoices with validation errors
& sqlite3 $db "SELECT InvoiceNumber, ValidationErrors, Timestamp FROM AuditLogs WHERE Status='Failed' AND ValidationErrors IS NOT NULL ORDER BY Timestamp DESC LIMIT 10;"
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
```powershell
$db = "E:\data\audit.db"
# Find invalid TINs from recent entries
& sqlite3 $db "SELECT DISTINCT InvoiceNumber, ValidationErrors, Timestamp FROM AuditLogs WHERE ValidationErrors LIKE '%TIN%' AND datetime(Timestamp) > datetime('now', '-7 days');"
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
```powershell
$db = "E:\data\audit.db"
# Find missing exchange rates
& sqlite3 $db "SELECT InvoiceNumber, CurrencyCode, ExchangeRate, Timestamp FROM AuditLogs WHERE CurrencyCode != 'MYR' AND (ExchangeRate IS NULL OR ExchangeRate = 1.0) AND datetime(Timestamp) > datetime('now', '-7 days');"
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

### "Cannot open SQLite audit database"

```
SQLite Error: unable to open database file
OR: Microsoft.Data.Sqlite.SqliteException: unable to open database file
```

**Diagnosis:**
```powershell
$db = "E:\data\audit.db"

# Check file exists
Test-Path $db

# Check directory write permissions for app pool identity
icacls (Split-Path $db) | Select-String "MyInvoisApi"

# Try opening manually
& sqlite3 $db "SELECT COUNT(*) FROM AuditLogs;"
```

**Solutions:**
1. Create `data\` directory and grant write access to the app pool identity:
   ```powershell
   New-Item -ItemType Directory -Force "C:\inetpub\apps\MyInvois.Api\data"
   icacls "C:\inetpub\apps\MyInvois.Api\data" /grant "IIS AppPool\MyInvoisApi:(OI)(CI)F"
   ```
2. Restart the application — EF Core will recreate `audit.db` on startup
3. Verify connection string in `appsettings.Production.json` uses `Data Source=` format

---

### "Audit Log Database Corrupt"

```
SQLite Error: database disk image is malformed
OR: Microsoft.Data.Sqlite.SqliteException: database disk image is malformed
```

**Diagnosis:**
```powershell
$db = "E:\data\audit.db"
& sqlite3 $db "PRAGMA integrity_check;"  # Expected: ok
```

**Solutions:**
1. **Restore from backup:**
   ```powershell
   $backup = "\\backup-server\MyInvois\SQLiteAudit\{yyMMdd}\audit_{yyMMdd}.db"
   Stop-WebAppPool "MyInvoisApi"
   Copy-Item $backup $db -Force
   Start-WebAppPool "MyInvoisApi"
   ```
2. **If no backup available:** Delete `audit.db` — EF Core will recreate an empty database on next startup. Historical audit entries will be lost; escalate to IT Manager for compliance assessment.
3. **Escalate to IT Manager** if 7-year retention compliance is at risk

---

## Submission Failures

### View Failed Submissions

```powershell
$db = "E:\data\audit.db"

# Query failed submissions (most recent first)
& sqlite3 $db "SELECT AuditId, InvoiceNumber, ErrorMessage, StatusCode, RetryCount, Timestamp FROM AuditLogs WHERE Status='Failed' ORDER BY Timestamp DESC LIMIT 20;"

# Failed in last 24 hours
& sqlite3 $db "SELECT AuditId, InvoiceNumber, ErrorMessage, Timestamp FROM AuditLogs WHERE Status='Failed' AND datetime(Timestamp) > datetime('now', '-24 hours') ORDER BY Timestamp DESC;"
```

### Retry Failed Invoice

**Manual retry via audit log:**

```powershell
$db = "E:\data\audit.db"

# Step 1: Identify failed submission
& sqlite3 $db "SELECT AuditId, InvoiceNumber, ErrorMessage FROM AuditLogs WHERE Status='Failed' AND InvoiceNumber='INV-2026-00001';"

# Step 2: Reset to Pending so service reprocesses on next batch
& sqlite3 $db "UPDATE AuditLogs SET RetryCount = RetryCount + 1, Status = 'Pending' WHERE AuditId = 'YOUR_AUDIT_ID';"

# Step 3: Service reprocesses on next batch run
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

$db = "E:\data\audit.db"

# 1. Certificate still valid
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
  "C:\Certs\MyInvois\myinvois-cert.pfx",
  "Password"
)
Write-Host "Days until expiry: $(($cert.NotAfter - (Get-Date)).Days)"

# 2. Service running
Get-Service -Name "MyInvois-Service" -ErrorAction SilentlyContinue

# 3. Audit log accessible and WAL mode active
& sqlite3 $db "PRAGMA journal_mode;"          # Expected: wal
& sqlite3 $db "SELECT COUNT(*) FROM AuditLogs;"

# 4. Recent failures (last 24 hours)
& sqlite3 $db "SELECT COUNT(*) FROM AuditLogs WHERE Status='Failed' AND datetime(Timestamp) > datetime('now', '-24 hours');"
```

### Monthly Metrics

```powershell
$db = "E:\data\audit.db"
# Monthly summary
& sqlite3 $db "SELECT strftime('%Y-%m', Timestamp) AS Month, COUNT(*) AS TotalInvoices, SUM(CASE WHEN Status='Success' THEN 1 ELSE 0 END) AS SuccessCount, SUM(CASE WHEN Status='Failed' THEN 1 ELSE 0 END) AS FailedCount, ROUND(SUM(CASE WHEN Status='Success' THEN 1.0 ELSE 0 END) * 100 / COUNT(*), 1) AS SuccessRate FROM AuditLogs WHERE Action='MyInvois_Submit' GROUP BY Month ORDER BY Month DESC;"
```

---

## Getting Help

### Escalation Path

1. **Check this guide** (Troubleshooting.md)
2. **Query audit logs** (see SQLite examples above using `sqlite3`)
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

**Last Updated:** 2026-06-01
**Owned By:** Operations Team
**Review Cycle:** Monthly or as issues arise

---

**🚨 Critical Issue?** Contact IT Manager on-call immediately.
