---
topic: MyInvois.Api Operational Runbook
category: runbook
system: MyInvois.Api
port: 5051
caller: SM-Portal
effective_date: 2026-03-11
last_reviewed: 2026-03-11
related_decisions: [ADR-001, ADR-015]
---

# MyInvois.Api Operational Runbook

**Version**: 1.0
**Last Updated**: 2026-03-11
**Purpose**: Day-to-day operations, troubleshooting, incident response, and deployment procedures for the MyInvois.Api IIS host
**Target Audience**: DevOps engineers, system administrators

---

## Table of Contents

1. [Overview](#1-overview)
2. [Health Check](#2-health-check)
3. [Normal Operations](#3-normal-operations)
4. [Configuration Reference](#4-configuration-reference)
5. [Troubleshooting](#5-troubleshooting)
6. [Incident Response](#6-incident-response)
7. [Key Rotation](#7-key-rotation)
8. [Deployment Checklist](#8-deployment-checklist)

---

## 1. Overview

`MyInvois.Api` is an ASP.NET Core (.NET 8) REST host running on IIS that exposes invoice data
extracted from DB2/AS400 (MOVEX M3) to SM-Portal over HTTP.

```
SM-Portal (IIS, port 5050, Windows AD auth)
  → HTTP GET with X-API-Key header
    → MyInvois.Api (IIS, port 5051, localhost-only)
      → IBM DB2 iSeries Access ODBC
        → AS/400 (MOVEX M3 database)
```

**Key facts:**

| Property | Value |
|---|---|
| Process host | IIS with ASP.NET Core Module v2 |
| Binding | `http://127.0.0.1:5051` — localhost only, not externally reachable |
| Auth method | API Key — two-tier (`X-API-Key` primary, `X-Admin-Key` admin) |
| Auth bypass | `GET /api/v1/health` is exempt from API key enforcement |
| Primary caller | SM-Portal |
| Data source | DB2/AS400 via ODBC DSN `AS400PROD` |
| IIS App Pool | `MyInvoisApi` (No Managed Code, ApplicationPoolIdentity) |
| Publish path | `C:\inetpub\apps\MyInvois.Api\` |
| Log output | Serilog console → captured by IIS stdout; IIS access logs in standard location |

---

## 2. Health Check

The health endpoint requires no authentication and is suitable for monitoring probes.

**Request:**
```
GET http://localhost:5051/api/v1/health
```

**Expected response — 200 OK:**
```json
{
  "status": "healthy",
  "timestamp": "2026-03-11T08:00:00.000Z"
}
```

**Quick check from the server:**
```powershell
Invoke-RestMethod -Uri "http://localhost:5051/api/v1/health"
```

If this returns anything other than `200 OK` with `status: healthy`, the service requires
investigation — proceed to [Section 5: Troubleshooting](#5-troubleshooting).

---

## 3. Normal Operations

### 3.1 Verify Service Is Running

**IIS Manager (GUI):**
1. Open IIS Manager → Sites → `MyInvois.Api`
2. Confirm site state is **Started** (green arrow)
3. Open Application Pools → `MyInvoisApi` → confirm state is **Started**

**PowerShell:**
```powershell
# Check site state
Get-WebSite -Name "MyInvois.Api" | Select-Object Name, State, PhysicalPath

# Check app pool state
Get-WebConfiguration system.applicationHost/applicationPools/add |
    Where-Object { $_.name -eq "MyInvoisApi" } |
    Select-Object name, state
```

**Windows Event Log:**
```powershell
# View Application log entries for ASP.NET Core
Get-EventLog -LogName Application -Source "IIS AspNetCore Module" -Newest 20
```

### 3.2 Log Locations

| Log type | Location |
|---|---|
| Serilog structured output | Captured by IIS stdout — see `stdoutLog` path in web.config, typically `C:\inetpub\apps\MyInvois.Api\logs\` |
| IIS access logs | `C:\inetpub\logs\LogFiles\W3SVC<site-id>\` |
| Windows Event Log | Event Viewer → Windows Logs → Application (source: IIS AspNetCore Module) |
| ASP.NET Core Module log | `C:\inetpub\apps\MyInvois.Api\logs\aspnetcore-*.log` (only when `stdoutLogEnabled=true`) |

> Tip: Each request includes a `CorrelationId` header and log property. Use the CorrelationId from
> SM-Portal error reports to trace the full request through Serilog output.

---

## 4. Configuration Reference

All secrets are held in Windows user-secrets (development) or should be migrated to Azure Key Vault
for production. The `appsettings.json` file contains only non-secret defaults.

| Config key | Source | Required | Description |
|---|---|---|---|
| `ApiKeys:Primary` | User secrets | Yes | Primary API key — used by SM-Portal for invoice requests |
| `ApiKeys:Admin` | User secrets | Yes | Admin API key — reserved for admin/diagnostic endpoints |
| `MovexDb:ConnectionString` | User secrets | Yes | ODBC connection string for DB2/AS400 (e.g., `DSN=AS400PROD;UID=...;PWD=...;`) |
| `MovexDb:CommandTimeoutSeconds` | appsettings.json | No | DB2 query timeout in seconds; default 30 |
| `MovexDb:RetryCount` | appsettings.json | No | Polly retry count for transient DB2 failures; default 3 |
| `MovexDb:RetryDelaySeconds` | appsettings.json | No | Delay between Polly retries; default 2 |
| `Logging:LogLevel:Default` | appsettings.json | No | Serilog minimum level; default `Information` |
| `ASPNETCORE_ENVIRONMENT` | web.config env var | Yes | Must be `Production` in IIS deployment |
| `ASPNETCORE_CONTENTROOT` | web.config env var | Yes | Absolute path to publish folder; workspace standard requirement |

> Never place `MovexDb:ConnectionString` in SM-Portal configuration. DB2 credentials must only be
> accessible to the `MyInvois.Api` process.

---

## 5. Troubleshooting

### Symptom / Cause / Fix Table

| Symptom | Likely Cause | Fix |
|---|---|---|
| `401 Unauthorized` on any endpoint except `/health` | `X-API-Key` header is missing, wrong, or the key does not match `ApiKeys:Primary` or `ApiKeys:Admin` | Confirm SM-Portal is sending `X-API-Key: <correct-value>`. Verify the secret with `dotnet user-secrets list` in `src/MyInvois.Api/`. |
| SM-Portal receives `502 Bad Gateway` | MyInvois.Api is unreachable — IIS site stopped, app pool crashed, or nothing is listening on port 5051 | Check IIS Manager: start the `MyInvois.Api` site and `MyInvoisApi` app pool. Run `netstat -ano \| findstr 5051` to confirm the port is bound. Review Event Log for crash details. |
| DB2 connection error in logs (e.g., `[IBM][CLI Driver] SQL30081N`) | ODBC DSN not configured, wrong credentials, or AS/400 TCP unreachable | Verify the ODBC DSN exists: open ODBC Data Sources (64-bit) → System DSN → `AS400PROD`. Test connectivity: `ping <AS400-hostname>`. Check that the app pool identity has network access. Confirm credentials in user-secrets are correct. |
| Slow responses (>30 s) or timeout errors from SM-Portal | DB2 query taking longer than `CommandTimeoutSeconds` | Increase `MovexDb:CommandTimeoutSeconds` in `appsettings.json`. Check AS/400 server load and index availability on FGINHE/FGLINE tables. Use M3 Query tool to run the underlying query interactively to baseline performance. |
| `CorrelationId` absent from Serilog log entries | Serilog CorrelationId enricher not wired in `Program.cs` | Review `Program.cs` for `UseSerilog` configuration — `CorrelationIdEnricher` must be added. Redeploy after fix. |
| `500 Internal Server Error` with no log output | App pool crashing before Serilog initialises | Temporarily set `stdoutLogEnabled="true"` in web.config, reproduce the error, check the stdout log file, then set it back to `false`. Review Event Viewer Application log. |
| `403 Forbidden` on all requests | Request came from outside `localhost` — `ApiKeyMiddleware` or IIS binding blocked it | Confirm the IIS site binding is `127.0.0.1:5051` only. SM-Portal must call `http://localhost:5051` — not the server hostname or IP. |

---

## 6. Incident Response

### 6.1 Restart IIS Site and App Pool (Safe Procedure)

```powershell
# 1. Recycle the app pool (graceful — drains in-flight requests first)
Restart-WebAppPool -Name "MyInvoisApi"

# 2. If the site itself is stopped, start it
Start-WebSite -Name "MyInvois.Api"

# 3. Confirm
Get-WebSite -Name "MyInvois.Api" | Select-Object Name, State
```

> Prefer recycling the app pool over stopping/starting the site — it avoids a brief port-binding
> gap that could confuse SM-Portal.

### 6.2 Verify Recovery

```powershell
# Health check confirms the service is accepting requests
Invoke-RestMethod -Uri "http://localhost:5051/api/v1/health"
```

### 6.3 Check Windows Event Log

```powershell
# Last 30 Application log entries from ASP.NET Core Module
Get-EventLog -LogName Application -Newest 30 |
    Where-Object { $_.Source -like "*AspNetCore*" -or $_.Source -like "*IIS*" } |
    Format-List TimeGenerated, EntryType, Message
```

### 6.4 Escalation

If the service does not recover after a graceful app pool recycle and a site restart:

1. Check whether the DB2 ODBC DSN is still reachable — AS/400 outages cascade as 500 errors.
2. Check disk space on the host — a full disk causes IIS to fail stdout log writes, which can hang the process.
3. If all else fails, coordinate with infrastructure team to reboot the IIS host during a maintenance window (SM-Portal invoice extract will be unavailable during the window).

---

## 7. Key Rotation

API keys can be rotated without downtime by staging the new key alongside the old key before
cutting SM-Portal over.

### Step-by-step (zero-downtime rotation)

1. **Generate new key:** Create a new GUID (e.g., `[System.Guid]::NewGuid()` in PowerShell).

2. **Stage new key as secondary:** MyInvois.Api's `ApiKeyMiddleware` accepts both `ApiKeys:Primary`
   and `ApiKeys:Admin`. If a secondary slot is available in the config, set the new key there.
   Otherwise, brief downtime (< 30 s app pool recycle) is acceptable during an off-peak window.

3. **Update SM-Portal:** Change the `MyInvoisApi:ApiKey` (or equivalent) secret in SM-Portal
   user-secrets to the new key value. Recycle SM-Portal's app pool.

4. **Verify with new key:**
   ```powershell
   curl -H "X-API-Key: <new-key>" "http://localhost:5051/api/v1/health"
   ```

5. **Remove old key:** Update `ApiKeys:Primary` in MyInvois.Api user-secrets to the new key,
   remove the old value from any secondary slot, and recycle `MyInvoisApi` app pool.

6. **Confirm old key is rejected:**
   ```powershell
   curl -H "X-API-Key: <old-key>" "http://localhost:5051/api/v1/health"
   # Expected: 401 Unauthorized
   ```

---

## 8. Deployment Checklist

### Pre-Deployment

- [ ] Backup current publish folder: `Copy-Item -Recurse C:\inetpub\apps\MyInvois.Api C:\inetpub\apps\MyInvois.Api.bak`
- [ ] Confirm user-secrets are present on target server: `dotnet user-secrets list` (run from `src/MyInvois.Api/`)
- [ ] Run tests locally: `dotnet test` — all tests must pass before deploying
- [ ] Build publish artifact: `dotnet publish src/MyInvois.Api/MyInvois.Api.csproj -c Release -o ./publish/MyInvois.Api`
- [ ] Inspect `web.config` in publish output — confirm `ASPNETCORE_CONTENTROOT` and `ASPNETCORE_ENVIRONMENT` are set correctly
- [ ] Notify SM-Portal team of planned deployment window (MyInvois.Api invoice extract will be briefly unavailable)

### Deployment

- [ ] Recycle `MyInvoisApi` app pool to drain in-flight requests: `Restart-WebAppPool -Name "MyInvoisApi"`
- [ ] Copy publish output to `C:\inetpub\apps\MyInvois.Api\`
- [ ] Confirm folder permissions — `IIS AppPool\MyInvoisApi` has `ReadAndExecute`
- [ ] Start `MyInvoisApi` app pool: `Start-WebAppPool -Name "MyInvoisApi"`
- [ ] Start `MyInvois.Api` site if stopped: `Start-WebSite -Name "MyInvois.Api"`

### Post-Deployment Verification

- [ ] Health check returns 200: `Invoke-RestMethod "http://localhost:5051/api/v1/health"`
- [ ] Invoice endpoint returns data: `curl -H "X-API-Key: <primary>" "http://localhost:5051/api/v1/invoices?fromDate=2026-01-01&toDate=2026-01-31&type=ALL"`
- [ ] Confirm port binding is localhost-only: `netstat -ano | findstr 5051` — must show `127.0.0.1:5051`
- [ ] Confirm SM-Portal can retrieve invoices end-to-end (ask a user to run the Invoice Extract from the portal)
- [ ] Review Serilog output / Event Log for any errors in the first 5 minutes
- [ ] Remove backup folder once stability is confirmed: `Remove-Item -Recurse C:\inetpub\apps\MyInvois.Api.bak`

---

## Related Documents

- [05-deployment-guide.md](../../ai/memory/05-deployment-guide.md) — Full deployment guide including MyInvois.Api host section (Phase 11)
- [iis-deployment.md](../runbooks/iis-deployment.md) — General IIS deployment runbook
- [ADR-001](../decisions/adr-001.md) — Architecture decision: API key authentication for internal services
- [ADR-015](../decisions/adr-015.md) — Architecture decision: localhost-only binding for internal APIs

---

**Owner**: DevOps Team
**Last Review**: 2026-03-11
**Next Review**: 2026-06-11
