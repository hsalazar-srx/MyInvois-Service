# Pre-Deployment Checklist

**Project:** MyInvois-Service
**Purpose:** Workspace compliance & deployment readiness verification
**Last Updated:** 2026-02-17

---

## 📋 Workspace Compliance Checklist

Per [../../.github/WORKSPACE_RULES.md](../../.github/WORKSPACE_RULES.md)

### 1. Database - Audit Schema

- [ ] **Standard audit schema** implemented
  - Database: `SRX_AuditLog`
  - Table: `[dbo].[AuditLog]`
  - Schema follows workspace standard template
  - Project-specific extensions documented in `ai/memory/02-data-model.md`

- [ ] **Required indexes** created
  - `[IX_AuditLog_Timestamp]` on Timestamp DESC
  - `[IX_AuditLog_User]` on UserId, Timestamp DESC
  - `[IX_AuditLog_Resource]` on ResourceType, ResourceId, Timestamp DESC
  - `[IX_AuditLog_Status]` on Status, Timestamp DESC (filtered WHERE Status <> 'Success')

- [ ] **TDE enabled** on SQL Server database
  ```sql
  -- Verify TDE:
  SELECT name, is_encrypted
  FROM sys.databases
  WHERE name = 'SRX_AuditLog';
  -- Expected: is_encrypted = 1
  ```

---

### 2. Security - Credentials & Encryption

- [ ] **Connection strings** in User Secrets (not appsettings.json)
  ```bash
  # Verify:
  dotnet user-secrets list
  # Should show connection strings
  ```

- [ ] **No credentials** in appsettings.json or appsettings.Development.json
  ```bash
  # Check:
  grep -i "password\|secret\|connectionstring.*Data Source" appsettings*.json
  # Should return nothing
  ```

- [ ] **TLS 1.2+** for all external connections
  - MOVEX DB2/AS400: Connection string includes `SSL=true` or equivalent
  - MyInvois API: HttpClient configured with TLS 1.2 minimum
  - SQL Server: `Encrypt=true;TrustServerCertificate=false`

---

### 3. Compliance - Data Retention & Privacy

- [ ] **7-year retention policy** documented
  - Documented in: `README.md` (Compliance section)
  - Policy references: ISO 27001, Malaysian tax law
  - Archival strategy documented (Phase 2 feature)

- [ ] **No PII in logs** verified
  - Code reviewed for logging statements
  - No Tax IDs, customer names, emails, phones
  - Correlation IDs and invoice numbers are OK

- [ ] **Audit log immutability** enforced
  - Write access: Service account only
  - Read access: Controlled via RBAC
  - No DELETE permissions on audit table

---

### 4. Testing - Coverage & Quality

- [ ] **Test coverage ≥80%**
  ```bash
  # Verify:
  dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
  # Check line coverage percentage
  ```

- [ ] **All tests passing**
  ```bash
  # Run:
  dotnet test
  # Expected: Passed! - Total: [N], Passed: [N], Failed: 0
  ```

- [ ] **Integration tests** against sandbox
  - MOVEX DB2/AS400 connection successful
  - MyInvois API authentication successful
  - End-to-end batch processing (10+ invoices)

---

### 5. Operations - Backup & Monitoring

- [ ] **Backup schedule** verified
  - SQL Server backup configured: Daily minimum
  - Backup location: Network-attached storage (redundant)
  - Recovery SLA: RTO 4 hours, RPO 24 hours
  - Quarterly restoration tests scheduled

- [ ] **Monitoring** configured
  - Application Insights or equivalent
  - Alerts for:
    - Failed submissions (>5% failure rate)
    - Database connection errors
    - MyInvois API errors (429, 500)
    - Audit log write failures

---

### 6. Documentation - Required Files

- [ ] **README.md** complete
  - Project purpose and scope
  - Technology stack
  - Prerequisites
  - Setup instructions (User Secrets)
  - How to run locally
  - Deployment instructions
  - Known issues
  - Links to docs

- [ ] **ai/memory/** files up to date
  - 00-product-vision.md
  - 01-system-architecture.md
  - 02-data-model.md (includes audit schema extensions)
  - 03-myinvois-requirements.md
  - 04-api-integration.md
  - 05-deployment-guide.md
  - 09-implementation-decisions.md (ADRs)
  - 00-skills-audit.md

- [ ] **Runbooks** created in `docs/`
  - SETUP.md - Local setup guide
  - DEPLOYMENT.md - Production deployment
  - TROUBLESHOOTING.md - Common issues and solutions

---

## 🚀 Deployment Readiness

### Pre-Production Checklist

- [ ] **Environment variables** configured
  - CI/CD secrets set for connection strings
  - Azure Key Vault references configured
  - Service account credentials rotated

- [ ] **Database migration** tested
  - DDL scripts run successfully in staging
  - Indexes created and verified
  - No conflicts with existing tables

- [ ] **UAT completed**
  - Finance team sign-off
  - Test submission batch (100+ invoices)
  - Success rate ≥95%
  - No data quality issues

- [ ] **Rollback plan** ready
  - Previous build accessible (1 week minimum)
  - Database rollback scripts prepared
  - Communication plan for stakeholders

---

## 📞 Approval Sign-Offs

**Before deploying to production:**

- [ ] **Dev Lead** - Code review approved
- [ ] **QA Lead** - Testing complete, no blockers
- [ ] **IT Ops** - Infrastructure ready, monitoring configured
- [ ] **Finance Sponsor** - Business requirements met
- [ ] **Compliance Officer** - Audit trail and retention verified

---

## 🔍 Post-Deployment Verification

**After deployment, verify:**

```bash
# 1. Service is running
curl https://[production-url]/health

# 2. Database connection
sqlcmd -S [server] -d SRX_AuditLog -Q "SELECT COUNT(*) FROM [dbo].[AuditLog]"

# 3. First submission successful
# Monitor logs for first batch run

# 4. Audit logs being written
# Check [dbo].[AuditLog] for new entries
```

---

**Related:**
- [WORKSPACE_RULES.md](../../.github/WORKSPACE_RULES.md) - Full workspace standards
- [Pre-Commit Checklist](pre-commit.md)
- [Development Workflow](../workflows/development.md)
