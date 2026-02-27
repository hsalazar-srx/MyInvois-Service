# Workflow Process - Finance Team Operations

**Last Updated:** February 18, 2026 (Updated with implementation progress)
**Status:** Production  
**Owner:** Finance Manager

## Purpose

This diagram shows the complete finance team workflow for monthly e-invoice submission, including:
- Pre-batch preparation
- Batch execution and monitoring
- Error handling and recovery
- Compliance and audit trail
- Post-batch reporting

## Monthly Submission Workflow

```mermaid
---
config:
  theme: light
  layout: elk
  look: classic
---
flowchart TD
  A["Month-End Prep<br/>(27th - Last Day)"] --> B{All invoices<br/>in MOVEX?}
  B -->|No| C["Finance team<br/>creates missing<br/>invoices in M3"]
  B -->|Yes| D["Stand by for<br/>batch execution"]
  C --> D
  
  D --> E["1st of Month<br/>1:00 AM<br/>Batch Auto-Runs"]
  
  E --> F["Batch Processing<br/>(Service handles<br/>validation, transform,<br/>submission)"]
  
  F --> G{Batch<br/>outcome?}
  
  G -->|All Success<br/>100%| H["✓ SUCCESS<br/>Email: 815 submitted<br/>1000 total invoices"]
  G -->|Partial Failure<br/>90-99%| I["⚠️ PARTIAL<br/>Email: 15 failed<br/>800 submitted"]
  G -->|Major Failure<br/><90%| J["✗ CRITICAL<br/>Alert Operations<br/>Manual escalation"]
  
  H --> K["Finance Manager<br/>Reviews Email"]
  I --> K
  J --> L["Operations Manager<br/>Investigates<br/>Issue resolution"]
  
  K -->|All successful| M["Archive<br/>batch summary<br/>No action needed"]
  K -->|Some failures| N["Finance Manager<br/>Reviews error report"]
  
  L -->|Root cause| L1["Fix issue<br/>Retry batch<br/>on 2nd"]
  L1 --> N
  
  N -->|Action: Skip| O["These invoices<br/>were already<br/>submitted<br/>✓ Mark as done"]
  N -->|Action: Fix data| P["Contact MOVEX<br/>admin to fix<br/>invoice data"]
  N -->|Action: Retry| Q["Use Portal UI<br/>Submit failed<br/>invoices manually"]
  
  O --> R["Update tracking<br/>sheet"]
  P --> S["Wait for<br/>data fix"]
  Q --> T{Retry<br/>successful?}
  
  S --> U["Notify Finance<br/>Manager<br/>Data fixed"]
  U --> N
  
  T -->|Yes| R
  T -->|No| V["Escalate to<br/>Finance Lead<br/>Manual portal<br/>submission"]
  V --> R
  
  R --> W["Month-End Close<br/>(27th - 3rd)"]
  W --> X["Verify all invoices<br/>submitted to<br/>MyInvois"]
  X --> Y{All invoices<br/>accounted for?}
  Y -->|Yes| Z["✓ COMPLIANCE MET<br/>All invoices<br/>auditable"]
  Y -->|No| AA["Alert Compliance<br/>Officer<br/>Investigate"]
  
  Z --> AB["Archive audit<br/>logs for tax<br/>authority"]
  AB --> AC["Next month<br/>repeat"]
  
  style A fill:#ffe6e6
  style E fill:#fff4e6
  style H fill:#e6ffe6
  style I fill:#ffffcc
  style J fill:#ff9999
  style Z fill:#99ff99
  style AA fill:#ff9999
```

## Finance Team Approval Workflow

```mermaid
---
config:
  theme: light
---
sequenceDiagram
  autonumber
  participant Schedule as Windows<br/>Scheduler
  participant Service as Batch<br/>Service
  participant Finance as Finance<br/>Manager
  participant Portal as Portal UI<br/>Phase 2
  participant Audit as Auditor
  
  Note over Finance: PRE-BATCH (30th of month)
  
  Finance->>Finance: Verify all invoices<br/>in MOVEX system
  Finance->>Finance: Check for duplicates<br/>in last 12 months
  
  Note over Schedule,Finance: BATCH EXECUTION (1st of month)
  
  Schedule->>Service: Auto-trigger batch<br/>(1:00 AM)
  Service->>Service: Process 815 invoices
  Service->>Finance: Email batch results<br/>Subject: MyInvois Batch Complete
  
  Finance->>Portal: Login to view<br/>submission status
  Portal->>Finance: Display:<br/>- 800 successful<br/>- 15 failed<br/>- Error details
  
  alt Failures < 5%
    Finance->>Finance: Quick scan errors
    Finance->>Finance: Approve results
    Note over Finance: No action needed
  else Failures 5-20%
    Finance->>Portal: View error details
    Portal->>Finance: Error report
    Finance->>Finance: Determine root cause
    alt Data quality issue
      Finance->>Finance: Contact MOVEX admin<br/>Request data fix
    else Integration issue
      Finance->>Finance: Alert IT Manager<br/>Request retry
    else Duplicate/already submitted
      Finance->>Finance: Check audit log<br/>Mark as resolved
    end
  else Critical failure
    Finance->>Finance: Call IT Manager<br/>Emergency meeting
  end
  
  Note over Finance: POST-BATCH (2nd - 27th)
  
  loop Check audit logs weekly
    Finance->>Portal: View submission status
    Portal->>Finance: Updated status<br/>+ MyInvois UUIDs
  end
  
  Note over Finance,Audit: MONTH-END AUDIT
  
  Audit->>Portal: Access audit trail<br/>(read-only)
  Portal->>Audit: Show:<br/>- All submissions<br/>- Timestamps<br/>- MyInvois UUIDs<br/>- Any failures
  
  Audit->>Audit: Verify compliance<br/>- All invoices submitted?<br/>- No duplicates?<br/>- Proper error handling?
  Audit->>Audit: Prepare audit<br/>summary for<br/>tax authority
```

---

## Error Resolution Workflow

### Error Type 1: Duplicate Submission

```
Scenario: MyInvois says "Invoice DS302 - Already submitted"
  1. Finance manager sees error in report
  2. Opens Portal → Search invoice history
  3. Finds previous submission (Jan 15)
  4. Marks as "Duplicate - Skip"
  5. No action needed
  Resolution time: < 5 minutes
```

### Error Type 2: Missing Mandatory Field

```
Scenario: MyInvois rejects due to missing "Tax ID"
  1. Finance manager sees error (field: TaxId)
  2. Contacts MOVEX admin
  3. MOVEX admin updates invoice in M3
  4. Finance manager triggers retry on 2nd of month
  5. Batch re-processes and submits successfully
  Resolution time: 1-3 hours (depends on MOVEX admin)
```

### Error Type 3: Invalid Data Format

```
Scenario: MyInvois rejects "Invoice total doesn't match line items"
  1. Finance manager sees error (Detail: Total mismatch)
  2. Manually reviews invoice in MOVEX
  3. Finds data entry error
  4. Updates invoice in M3
  5. Finance manager retries via Portal UI
  6. Submission succeeds
  Resolution time: 30-60 minutes (manual review)
```

### Error Type 4: Service Error / Network Issue

```
Scenario: MyInvois returns "500 Server Error"
  1. Service automatically queues for retry (3 attempts)
  2. Finance manager sees "Pending" status
  3. System retries hourly for 3 times
  4. If still failed after 3 retries, escalate to IT
  5. IT investigates MyInvois API issue
  6. Once resolved, finance manager triggers batch retry
  Resolution time: 2-24 hours (depends on service availability)
```

---

## Compliance & Audit Trail

### Audit Trail Components
- ✅ **Who**: Service account executing batch
- ✅ **What**: 815 invoices submitted to MyInvois
- ✅ **When**: 2026-02-01 01:15 - 01:35 AM
- ✅ **Where**: MyInvois API endpoint
- ✅ **Why**: Monthly e-invoice filing requirement
- ✅ **How**: Automated batch with XAdES v1.1 signatures
- ✅ **Result**: 800 successful (UUID), 15 failed

### Audit Evidence Preserved
- Invoice source data (MOVEX export)
- Validation results (passed/failed)
- Transformation output (UBL 2.1 format)
- Submission requests & responses (MyInvois API)
- e-signature certificates (XAdES v1.1)
- Error reports & recovery actions
- All timestamps (UTC, ISO 8601)

### Retention Policy
- **Minimum 7 years** (Malaysian tax law requirement)
- Automatic archival after 1 year (cold storage, Phase 2)
- Immutable storage (SQL Server, append-only)
- Access logged for audit purposes

---

## Key Responsibilities

### Finance Manager
- Verify invoice completeness before batch (27th-31st)
- Review batch results (email notification)
- Approve/reject results
- Escalate critical issues
- Ensure compliance

### Finance Officer
- Monitor batch execution (if urgent)
- Fix data quality issues (contact MOVEX admin)
- Retry failed submissions (via Portal, Phase 2)
- Track submission status monthly

### MOVEX Administrator
- Provide invoice data to MOVEX system
- Respond to data quality issues
- Ensure timely data availability

### IT Manager
- Deploy and maintain MyInvois-Service
- Handle integration issues
- Manage credentials (Azure Key Vault)
- Escalate to MyInvois vendor if needed

### Auditor
- Review monthly submission compliance
- Verify audit trail integrity
- Prepare for tax authority audits
- Report on KPIs (success rate, timeliness)

---

## Success Criteria

### Per-Batch Success
- ✅ ≥95% of invoices submitted successfully
- ✅ All errors documented in audit trail
- ✅ Error report delivered within 1 hour
- ✅ Batch completes within 2-hour window
- ✅ Finance team can explain all failures

### Monthly Success
- ✅ 100% of invoices accounted for (submitted or escalated)
- ✅ Zero compliance violations
- ✅ All audit logs preserved
- ✅ No duplicate submissions to MyInvois
- ✅ Finance team satisfied with process

### Quarterly Success
- ✅ < 2% average failure rate
- ✅ < 30 minutes average batch time
- ✅ Zero unauthorized submissions
- ✅ Auditor approves audit trail

---

## Related Diagrams
- [architecture.md](architecture.md) - System components
- [integration-sequence.md](integration-sequence.md) - Batch processing details
- [data-flow.md](data-flow.md) - Invoice data movement
