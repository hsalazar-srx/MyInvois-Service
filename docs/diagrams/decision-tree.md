# Decision Tree - Troubleshooting & Operational Decisions

**Last Updated:** February 5, 2026  
**Status:** Production  
**Owner:** Operations Manager

## Purpose

This diagram provides a decision framework for:
- Troubleshooting common issues
- Determining whether to retry or escalate
- Deciding when manual intervention is needed
- Escalation paths for different failure types

## Troubleshooting Decision Tree

```mermaid
---
config:
  theme: light
  layout: elk
  look: classic
---
graph TD
  START["Batch Failed<br/>or Issue<br/>Detected"] --> Q1{What<br/>failed?}
  
  Q1 -->|MOVEX<br/>Connection| Q2["Can reach<br/>MOVEX API?"]
  Q2 -->|No| Q2A["Is MOVEX<br/>server down?"]
  Q2A -->|Likely yes| A1["✓ RETRY<br/>Wait 1 hour<br/>Auto-retry<br/>Escalate if<br/>still down"]
  Q2A -->|No - timeout| A2["✓ WAIT<br/>Check network<br/>connectivity<br/>Check firewall<br/>rules<br/>Verify DNS"]
  Q2 -->|Yes| Q2B["Do you get<br/>401/403<br/>error?"]
  Q2B -->|Yes| A3["✗ ESCALATE<br/>MOVEX API auth<br/>Check credentials<br/>Contact MOVEX<br/>team"]
  Q2B -->|No| A4["✗ ESCALATE<br/>MOVEX data issue<br/>Check data quality<br/>Contact MOVEX<br/>admin"]
  
  Q1 -->|Validation<br/>Error| Q3["What<br/>validation<br/>failed?"]
  Q3 -->|Missing<br/>field| A5["✓ REPORT<br/>Email error to<br/>finance team<br/>Provide field list<br/>Next: Manual fix<br/>in MOVEX"]
  Q3 -->|Invalid<br/>format| A6["✓ REPORT<br/>Email error to<br/>finance team<br/>Show field + issue<br/>Next: Manual fix<br/>or retry"]
  Q3 -->|Data<br/>mismatch| A7["✓ REPORT<br/>Email error to<br/>finance team<br/>Invoice total<br/>mismatch details<br/>Next: Manual fix"]
  
  Q1 -->|MyInvois<br/>Submission| Q4["What HTTP<br/>error?"]
  Q4 -->|401<br/>Unauthorized| A8["✓ AUTO RETRY<br/>Token refresh<br/>Automatic<br/>within 5 min"]
  Q4 -->|429<br/>Too Many| A9["✓ AUTO RETRY<br/>Exponential backoff<br/>Wait 60s<br/>Retry batch"]
  Q4 -->|400<br/>Bad Request| Q5["Error<br/>code?"]
  Q5 -->|DS301<br/>Hash error| A10["✗ SKIP<br/>Data integrity<br/>issue in MyInvois<br/>Contact MyInvois<br/>vendor"]
  Q5 -->|DS302<br/>Duplicate| A11["✓ SKIP<br/>Already submitted<br/>Mark resolved<br/>No retry needed"]
  Q5 -->|Other<br/>400 error| A12["✗ REPORT<br/>Email error to<br/>finance team<br/>Show error details<br/>Next: Manual retry<br/>or fix"]
  Q4 -->|500<br/>Server Error| A13["✓ AUTO RETRY<br/>Exponential backoff<br/>Max 3 attempts<br/>Queue for retry<br/>if fails"]
  Q4 -->|503<br/>Unavailable| A14["✓ AUTO RETRY<br/>Service down<br/>Exponential backoff<br/>Max 3 attempts<br/>Escalate if<br/>persistent"]
  Q4 -->|Network<br/>Timeout| A15["✓ AUTO RETRY<br/>Connection issue<br/>Exponential backoff<br/>Max 3 attempts"]
  
  Q1 -->|Database<br/>Connection| Q6["SQL Server<br/>responding?"]
  Q6 -->|No| A16["✗ ESCALATE<br/>Database down!<br/>Contact DBA<br/>Emergency: Log<br/>to file temp"]
  Q6 -->|Yes| Q7["Auth<br/>working?"]
  Q7 -->|No| A17["✗ ESCALATE<br/>Credentials wrong<br/>Check Key Vault<br/>Contact IT"]
  Q7 -->|Yes| A18["✗ ESCALATE<br/>Unknown DB<br/>error<br/>Check error log"]
  
  Q1 -->|Key Vault<br/>Access| A19["✗ ESCALATE<br/>Cannot get<br/>credentials<br/>Contact IT<br/>Check Azure<br/>access policies"]
  
  Q1 -->|Authentication<br/>Token| Q8["Token<br/>valid?"]
  Q8 -->|Expired| A20["✓ AUTO RETRY<br/>Refresh token<br/>automatic<br/>within 5 min"]
  Q8 -->|Invalid| A21["✗ ESCALATE<br/>Credentials<br/>incorrect<br/>Check Key Vault<br/>Rotate if needed"]
  Q8 -->|Not obtained| A22["✗ ESCALATE<br/>OAuth server<br/>unreachable<br/>Check network<br/>Contact vendor"]
  
  style A1 fill:#99ff99
  style A2 fill:#ffff99
  style A3 fill:#ff9999
  style A4 fill:#ff9999
  style A5 fill:#ffff99
  style A6 fill:#ffff99
  style A7 fill:#ffff99
  style A8 fill:#99ff99
  style A9 fill:#99ff99
  style A10 fill:#ff9999
  style A11 fill:#99ff99
  style A12 fill:#ffff99
  style A13 fill:#99ff99
  style A14 fill:#99ff99
  style A15 fill:#99ff99
  style A16 fill:#ff9999
  style A17 fill:#ff9999
  style A18 fill:#ff9999
  style A19 fill:#ff9999
  style A20 fill:#99ff99
  style A21 fill:#ff9999
  style A22 fill:#ff9999
```

## Action Guide by Decision Color

### 🟢 GREEN (AUTO RETRY / AUTO RECOVER)
**Action**: System handles automatically; no manual intervention needed
- **Auto-Retry**: Exponential backoff (3-5 attempts)
- **Monitoring**: Monitor logs; alert if not resolved
- **Escalation**: Only if still failing after auto-retries
- **Examples**: 
  - 401 Unauthorized (auto token refresh)
  - 429 Rate Limited (auto backoff)
  - 500 Server Error (auto retry)

**Next Steps**:
1. ✓ Let system retry automatically
2. Monitor logs for 10-30 minutes
3. If still failing → Check related YELLOW items
4. If critical → Escalate per RED path

---

### 🟡 YELLOW (REPORT / MANUAL ACTION REQUIRED)
**Action**: Issue requires manual intervention; not critical but blocking
- **Report**: Send detailed error report to responsible team
- **Investigation**: Root cause analysis needed
- **Timeline**: 1-24 hours to resolution
- **Examples**: 
  - Validation errors (missing fields)
  - Data quality issues
  - Format mismatches

**Next Steps**:
1. Email detailed error report to responsible party
2. Include: What failed, which invoices, actionable next steps
3. Specify required action (fix data, retry, escalate)
4. Set follow-up check: When is retry expected?
5. Track resolution status

---

### 🔴 RED (ESCALATE / CRITICAL)
**Action**: Critical issue; immediate escalation required
- **Escalate**: Alert senior staff (IT Manager, Finance Manager)
- **Timeline**: Immediate attention required
- **Impact**: Batch may be blocked
- **Examples**:
  - Database unavailable
  - Key Vault inaccessible
  - Authentication credentials invalid
  - MOVEX API inaccessible

**Next Steps**:
1. Send URGENT alert email
2. Include: What failed, when, potential impact
3. Call on-call manager (if outside business hours)
4. Conference with relevant teams (IT, Finance)
5. Determine: Can batch retry, or manual escalation?

---

## Escalation Paths

### Escalation Level 1: Finance Manager
**When**: Validation errors, data quality issues
**Contact**: finance-manager@company.com
**Actions**:
- Review error report
- Coordinate with MOVEX admin
- Request data fix
- Authorize manual retry
- Track resolution

**SLA**: Within 2 business hours

### Escalation Level 2: IT Manager
**When**: Service/Integration/Database issues
**Contact**: it-manager@company.com
**Actions**:
- Investigate system issue
- Check logs and monitoring
- Contact external vendors (MOVEX, MyInvois)
- Authorize retry or rollback
- Post-mortem after resolution

**SLA**: Immediate (during business hours)

### Escalation Level 3: Security Officer
**When**: Authentication, credential, or security issues
**Contact**: security-officer@company.com
**Actions**:
- Verify credential validity
- Check Azure Key Vault access
- Review security logs
- Authorize key rotation
- Investigate compliance impact

**SLA**: Immediate (critical)

### Escalation Level 4: Database Administrator
**When**: SQL Server issues
**Contact**: dba@company.com
**Actions**:
- Restore from backup
- Investigate corruption
- Verify encryption status
- Check backup integrity
- Coordinate with IT Manager

**SLA**: Within 30 minutes

---

## Common Issues & Quick Fixes

### Issue: Batch Fails at MOVEX Connection
**Symptom**: "Connection timeout" or "Service unavailable"
**Quick Fix**:
1. Check: Is MOVEX server down? (Check status page)
2. Check: Is network accessible? (ping MOVEX server)
3. Check: Firewall rules? (Verify port 443 allowed)
4. Action: Wait 1 hour; system will auto-retry
5. If still failing after 3 auto-retries → Escalate to IT Manager

**Prevention**: Monitor MOVEX status page; schedule batch after maintenance

---

### Issue: Validation Errors (Missing Fields)
**Symptom**: Email report: "15 invoices failed: Missing TaxId"
**Quick Fix**:
1. Review: Which invoices? (Listed in error report)
2. Contact: MOVEX admin team
3. Request: Add missing field values in M3
4. Wait: For data fix (usually 1-2 hours)
5. Action: Trigger batch retry on 2nd or 3rd of month
6. Verify: Check batch results again

**Prevention**: Finance team QA invoices before batch (27th-31st)

---

### Issue: MyInvois Returns DS302 (Duplicate)
**Symptom**: Batch report: "Invoice DS302 - Already submitted"
**Quick Fix**:
1. Check: View audit log for previous submission
2. Verify: Is duplicate legitimate or error?
3. Action: If duplicate → Mark as "Skip" (no retry)
4. Action: If error → Investigate (contact MyInvois)
5. Don't Retry: DS302 means already in system

**Prevention**: Check duplicate detection before retry

---

### Issue: Rate Limit (429 Too Many Requests)
**Symptom**: Batch slows down; takes 45+ minutes
**Quick Fix**:
1. System handles: Auto-backoff + retry
2. No action: Batch will complete (slower)
3. Monitor: Check batch logs
4. Don't: Manually retry (will hit limit again)
5. Prevention: Confirm 600ms delay in configuration

**Prevention**: Verify rate limiting in code (600ms per submission)

---

### Issue: Authentication Token Expires
**Symptom**: Error "401 Unauthorized" mid-batch
**Quick Fix**:
1. System handles: Auto-refresh token
2. System retries: Failed submission automatically
3. Monitor: Check batch logs
4. No action: Transparent to operator
5. If failing: Check Key Vault credentials

**Prevention**: Verify token TTL = 1 hour; auto-refresh on 401

---

## When to Stop & Escalate

### STOP and ESCALATE When:
1. ❌ **Same issue fails 3+ times** → System can't auto-recover
2. ❌ **Critical system unavailable** (Database, Key Vault) → Can't proceed
3. ❌ **Security issue detected** (Invalid credentials, access denied) → Stop immediately
4. ❌ **Compliance violation risk** (Data loss, audit trail broken) → Escalate immediately
5. ❌ **Unknown error** (Not in decision tree) → Need expert review

### Decision Framework:
```
Can system auto-retry? 
  YES → Let it retry (monitor logs)
  NO → Is this a data issue?
    YES → Report to Finance Manager
    NO → Is this a system issue?
      YES → Escalate to IT Manager
      NO → Is this a security issue?
        YES → Escalate to Security Officer
        NO → Escalate to Architecture Team
```

---

## Related Diagrams
- [integration-sequence.md](integration-sequence.md) - Batch execution flow
- [workflow-process.md](workflow-process.md) - Finance team workflow
- [architecture.md](architecture.md) - System components
