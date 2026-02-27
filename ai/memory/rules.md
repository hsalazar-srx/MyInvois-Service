# MyInvois-Service - AI Agent Rules

**Last Updated**: 2026-02-06  
**Version**: 1.2  
**Scope**: Core principles, constraints, and guidance for AI agents working on this project

---

## 🚨 CRITICAL: Read This First (AI Agents START HERE)

**⚠️ RULE #0: Skills-First Architecture (MANDATORY)**

**Before creating ANY implementation files (`src/`, `Services/`, `Models/`, etc.):**

1. ✅ **Check centralized skills**: `C:\Projects\.github\skills\manifest.json`
   - Review existing skills in your domain (integration, architecture, etc.)
   - Identify which skills can be reused
   
2. ✅ **Consult available agents**: `C:\Projects\.github\agents\manifest.json`
   - Check if `@expert-movex-dotnet` or other agents have relevant expertise
   - Ask agents for guidance BEFORE coding
   
3. ✅ **Document in skills audit**: `ai/memory/00-skills-audit.md`
   - List skills found and how they're used
   - Identify skills gaps and propose new skills
   - This file MUST exist before creating implementation code
   
4. ✅ **Reference skills in code**: Add skill references in code comments
   ```csharp
   // Uses skill: integration/m3-transaction-builder v1.0+
   public class MovexInvoiceReader { }
   ```

**Why This Matters:**
- ✅ Reuse proven patterns from centralized registry
- ✅ Maintain consistency across all workspace projects  
- ✅ Leverage agent expertise for domain-specific guidance
- ✅ Prevent duplicate implementations (saves 10-14 hours refactoring)

**Enforcement:** Pre-commit hook validates `00-skills-audit.md` exists before allowing commits with `src/` files.

**See:** [00-Skills Audit](00-skills-audit.md) for this project's skills inventory

---

## 🎯 Core Principles

### 1. Validation-First Architecture
All invoices are validated **before** API submission. Never attempt to submit an invalid invoice.

```
Validation Sequence:
1. MandatoryFieldsValidator (20+ required fields)
2. TINValidator (12-digit format, optional API lookup)
3. DateValidator (real dates, ISO 8601, UTC)
4. CurrencyValidator (ISO 4217, exchange rate if non-MYR)
5. TotalsValidator (mathematical consistency ±1 cent)

If ANY validator fails → Log as "Failed" in audit, DO NOT SUBMIT
```

**Evidence**: [03-MyInvois Requirements](03-myinvois-requirements.md#validation-rules)

### 2. Audit Everything (7-Year Retention)
Every action that touches invoice data must be logged to SQL Server `dbo.AuditLog`.

**Mandatory Log Fields**:
- `Timestamp` (UTC)
- `InvoiceNumber`
- `Action` (Fetched, Transformed, Submitted, Failed)
- `Status` (Success, Pending, Failed)
- `RawRequest` & `RawResponse` (full JSON for forensics)
- `MyInvoisUUID` (after successful submission)

**Retention**: 7 years minimum (automated cleanup via stored procedure)

**Rationale**: Regulatory compliance (LHDNM), dispute resolution, audit trails

**Evidence**: [02-Data Model - Audit Log Design](02-data-model.md#audit-log-schema)

### 3. Configuration Over Code
Business logic is driven by `appsettings.json`, not hardcoded values.

**Never Hardcode**:
- ❌ Batch sizes (move to `BatchProcessing.SalesInvoiceBatchSize`)
- ❌ Retry counts (move to `MyInvoisApi.MaxRetries`)
- ❌ Timeouts (move to `MovexApi.TimeoutSeconds`)
- ❌ API endpoints (move to `MovexApi.BaseUrl`)

**Always Use**:
- ✅ `IOptions<T>` dependency injection
- ✅ Environment-specific config (Development/Staging/Production)
- ✅ User Secrets for credentials (OAuth ClientId/ClientSecret)

**Evidence**: [05-Deployment Guide - Configuration Section](05-deployment-guide.md#phase-3-application-configuration)

### 4. Security First
Authentication, encryption, and credential management must be correct from day one.

**Requirements**:
- ✅ OAuth 2.0 for MyInvois (Client Credentials flow)
- ✅ Windows Integrated Auth for MOVEX (AD service account)
- ✅ TLS 1.2+ for all external API calls
- ✅ User Secrets for OAuth credentials (never in appsettings.json)
- ✅ XAdES v1.1 signature generation (MyInvois SDK)
- ✅ No logging of sensitive data (credentials, PII)

**Red Flags**:
- 🚩 Credentials in code
- 🚩 HTTP instead of HTTPS
- 🚩 Null token handling (will cause 401 errors)
- 🚩 Secrets in git commits

**Evidence**: [04-API Integration - Authentication](04-api-integration.md#authentication-oauth-20-client-credentials)

---

## 📋 MyInvois-Specific Rules

### Rule 1: Never Submit Invalid Invoices
A single validation error blocks the entire invoice.

```csharp
// Example: Missing TIN
var validationErrors = await _validator.Validate(invoice);
if (validationErrors.Any())
{
    // Log as Failed, do NOT submit
    await _auditLogger.LogSubmissionAttempt(invoice, "Failed", 
        validationErrors.First().Code);
    return; // Early exit
}

// Only if we reach here, submit
await _submitter.Submit(invoice);
```

### Rule 2: Prevent Duplicate Submissions
Check audit log before submitting the same invoice twice.

```sql
-- ALWAYS query before submission
SELECT TOP 1 MyInvoisUUID, Status 
FROM dbo.AuditLog 
WHERE InvoiceNumber = @invoiceNumber 
  AND Action = 'Submitted' 
  AND Status = 'Success'
ORDER BY Timestamp DESC;

-- If found: Don't submit, log as "Skipped - Already Submitted"
```

### Rule 3: Do NOT Retry DS-Series Errors
MyInvois error codes starting with "DS" are validation errors that won't resolve by retrying.

| Error Code | Meaning | Action |
|-----------|---------|--------|
| DS101 | Missing mandatory field | Manual review required |
| DS102 | Invalid format | Check data transformation |
| DS301 | Signature validation failed | Check XAdES implementation |
| DS302 | Duplicate submission | Check audit log |
| DS401 | Invalid TIN | Verify with finance team |

```csharp
// Example: Classify errors
private bool IsRetriableError(string errorCode)
{
    if (errorCode.StartsWith("DS")) return false;  // Never retry DS errors
    if (errorCode == "429") return true;           // Rate limited - retry
    if (errorCode == "500") return true;           // Server error - retry
    return false;
}
```

### Rule 4: Preserve MyInvois UUID
Once an invoice is successfully submitted, save its MyInvois UUID in the audit log for reconciliation.

```
MyInvoisUUID: 550e8400-e29b-41d4-a716-446655440000
Status: VALID
UIN: S1PR230100004534 (assigned after acceptance)
QR Code: (provided in /documents/{uuid}/details endpoint)
```

**Phase 2**: Query `/api/v1.0/documents/{uuid}/details` to get final UIN and QR code for sending to customer.

### Rule 5: Respect Batch Rate Limiting
MyInvois allows 100 requests/minute. Enforce 600ms delay between batch submissions.

```csharp
// Batch size: 50-100 invoices per request
// Delay between batches: 600ms (= 100 req/min limit)

for (int i = 0; i < batches.Count; i++)
{
    await _submitter.Submit(batches[i]);
    
    if (i < batches.Count - 1)
    {
        await Task.Delay(600);  // 600ms between batches
    }
}
```

---

## 🏗️ Project Constraints

### Scope
- **Only**: Sales invoices (SI) from MOVEX
- **Only**: MyInvois API submission (UBL 2.1 format)
- **Not Included**: Purchase invoices (PI), credit notes, debit notes
- **Timeline**: Monthly batch processing (1st of month, 2-hour window)

### Technology Stack
- **.NET Version**: 8.0 (LTS)
- **Database**: SQL Server 2019+
- **Auth**: OAuth 2.0, Windows Integrated Auth
- **Signature**: XAdES v1.1 (MyInvois SDK)
- **Format**: UBL 2.1 (e-invoice standard)

### Business Rules
- **Mandatory Fields**: 20+ per MyInvois spec (TIN, names, amounts, line items, etc.)
- **Decimal Precision**: 2 decimal places for amounts
- **Currency**: MYR primary; non-MYR requires exchange rate
- **Tax Rate**: 6% (SST) is default; others supported
- **Audit Retention**: 7 years minimum

---

## 📂 Folder Structure (SRX Template)

```
ai/
├── memory/
│   ├── 00-product-vision.md           ← Vision, objectives, timeline
│   ├── 00-skills-audit.md             ← Skills-first architecture audit
│   ├── 01-system-architecture.md      ← Components, data flow, decisions
│   ├── 02-data-model.md               ← SQL schema, DTOs, configurations
│   ├── 03-myinvois-requirements.md    ← Validation rules, field mapping
│   ├── 04-api-integration.md          ← API specs, endpoints, examples
│   ├── 05-deployment-guide.md         ← Setup, scheduling, monitoring
│   ├── 06-known-risks-and-pitfalls.md ← Risks, mitigations
│   ├── 07-product-roadmap.md          ← Roadmap & phase planning
│   ├── 08-governance-and-decisions.md ← Approvals & change control
│   └── implementation-decisions.md    ← ADRs (canonical)
├── evidence/
│   ├── decision-log.md                ← Decision log (evidence index)
│   ├── change-impact.md               ← Deployment change logs
│   ├── release-notes.md               ← Version release history
├── planning/
│   ├── sprint-plan.md                 ← Current sprint backlog
│   ├── execution-plan.md              ← Detailed task breakdown
│   └── initiative.md                  ← Strategic initiatives (Phase 2, etc.)
├── tasks/
│   ├── sprint-backlog.md              ← Current sprint todos
│   └── task-template.md               ← Template for new tasks
├── rules.md                            ← THIS FILE (AI agent rules)
└── README.md                           ← Quick reference guide
```

---

## 🤖 AI Agent Guidance

### When Implementing Features
1. **Read first**: [00-Product Vision](00-product-vision.md) for context
2. **Understand architecture**: [01-System Architecture](01-system-architecture.md)
3. **Check data model**: [02-Data Model](02-data-model.md) for schemas
4. **Validate requirements**: [03-MyInvois Requirements](03-myinvois-requirements.md)
5. **Review API specs**: [04-API Integration](04-api-integration.md)
6. **Follow deployment steps**: [05-Deployment Guide](05-deployment-guide.md)
7. **Check governance**: [08-Governance and Decisions](08-governance-and-decisions.md)

### When Debugging Issues
1. **Check audit log**: `SELECT * FROM dbo.AuditLog WHERE InvoiceNumber = 'XXX'`
2. **Review error code**: Match against [03-MyInvois Requirements](03-myinvois-requirements.md#error-codes)
3. **Validate data**: Ensure all 20+ mandatory fields are present
4. **Check configuration**: Verify appsettings.json matches environment
5. **Trace execution**: Use RawRequest/RawResponse from audit log for forensics

### When Adding Code
- ✅ Always maintain audit trail
- ✅ Use configuration, not hardcoded values
- ✅ Validate before submitting
- ✅ Handle retriable vs. non-retriable errors differently
- ✅ Document decisions in evidence/decision-log.md

### When Something Breaks
1. **Preserve evidence**: Don't delete error logs or audit records
2. **Document issue**: Add to evidence/change-impact.md
3. **Implement fix**: Update code and add test cases
4. **Verify fix**: Run unit + integration tests
5. **Update memory**: If architectural decision changed, update 01-system-architecture.md

---

## 🚨 Critical Skills Registry

### Required Skills (from `.github/skills/`)
- **dotnet-build**: Build .NET 8.0 solution
- **sql-server-setup**: Create/manage SQL Server databases
- **oauth-configuration**: Configure OAuth 2.0 flows
- **api-integration**: Consume RESTful APIs
- **xades-signing**: Generate XAdES v1.1 signatures (MyInvois SDK)

### Centralized Skills Location
```
.github/
├── skills/
│   ├── dotnet-build.md
│   ├── sql-server-setup.md
│   ├── oauth-configuration.md
│   ├── api-integration.md
│   └── xades-signing.md
```

---

## 📞 Escalation Path

| Issue | Owner | Escalation |
|-------|-------|-----------|
| Validation logic question | Integration Team | Product Owner if ambiguous |
| API integration issue | API Team | MyInvois support if persistent |
| Database/audit issues | Database Team | Infrastructure if connection fails |
| OAuth/security issues | Security Team | InfoSec for credential rotation |
| Deployment/scheduling | DevOps Team | IT for server access |
| MyInvois registration | Business/Legal | LHDNM (MyInvois regulator) |

---

## ✅ Quality Checklist

Before marking a task as DONE:
- [ ] Code builds without errors
- [ ] All unit tests pass (54+ tests)
- [ ] Integration tests pass with test data
- [ ] Audit log captures the action
- [ ] Configuration is external (appsettings.json)
- [ ] No credentials hardcoded
- [ ] Error handling covers retriable cases
- [ ] Documentation updated (memory files or evidence/)
- [ ] Change logged in evidence/change-impact.md

---

## 🔗 Quick Reference

| Need | File |
|------|------|
| Project vision? | [00-product-vision.md](00-product-vision.md) |
| How does it work? | [01-system-architecture.md](01-system-architecture.md) |
| Database schema? | [02-data-model.md](02-data-model.md) |
| Validation rules? | [03-myinvois-requirements.md](03-myinvois-requirements.md) |
| API endpoints? | [04-api-integration.md](04-api-integration.md) |
| How to deploy? | [05-deployment-guide.md](05-deployment-guide.md) |
| Architecture decisions? | [evidence/decision-log.md](../evidence/decision-log.md) |
| Test strategy? | [planning/testing-strategy.md](../planning/testing-strategy.md) |

---

**Owner**: Integration Architects  
**Last Review**: 2026-02-05  
**Next Review**: 2026-03-05 (Post-Phase 1)

