# Product Roadmap - MyInvois-Service

**Last Updated:** February 16, 2026  
**Status:** Active  
**Owner:** Product Owner + Finance Manager

---

## Vision

**Enable seamless, compliant e-invoicing to Malaysian Inland Revenue Board (LHDNM) MyInvois system with automated batch processing, comprehensive validation, and audit-trail integrity—transforming manual invoice submission into a secure, scalable integration.**

---

## Current State (Now - February 2026)

### Version: 0.1-DESIGN

#### Status: Phase 0 (Architecture & Design Complete, Implementation Starting)
- ✅ Architecture designed (12 ADRs)
- ✅ Requirements fully specified (1000+ lines)
- ✅ Project scaffolded (70+ files)
- ✅ Services & validators designed (interface stubs created)
- ⏳ No code implementation yet (starts Week 2, Feb 10)
- ⏳ No unit tests written (54+ planned)
- ⏳ Database schema not executed
- ⏳ No deployment packages created

#### Features Planned (Not Yet Delivered)
- 📝 Automated monthly batch processing (design complete)
- 📝 MOVEX invoice extraction via DB2 direct access (design complete, ADR-013)
- 📝 UBL 2.1 format transformation (design complete)
- 📝 20+ mandatory field validation (design complete)
- 📝 OAuth 2.0 authentication (design complete)
- 📝 XAdES v1.1 e-signature integration (design complete)
- 📝 SQL Server audit logging (design complete)
- 📝 Error reporting to finance team (design complete)

#### Current Limitations
- No implementation yet (Phase 0 design-only)
- Database schema not executed
- No deployment pipeline
- No monitoring/alerting
- No UI dashboard
- Manual resubmission required
- Single-plant only (by design for MVP)

#### Active Users
- Finance Operations: 4-6 staff (assigned for UAT in Week 4)
- IT Operations: 2 staff (implementation + operations)
- Auditors: Will have access post-launch

#### Stability Level
- **Design Phase** - Architecture stable, implementation not started

---

## Near Term (Next 3-6 months) - Phase 2

### Initiative [I001]: Self-Service Portal UI
- **Business Value**: Finance team can view submission status without IT help
- **Manufacturing Impact**: Reduces support overhead; finance team more independent
- **Dependencies**: 
  - ASP.NET Core controller layer
  - Angular/React frontend (TBD)
  - Role-based access control (RBAC)
- **Risk Level**: Medium (new UI framework dependency)
- **Target Completion**: May 2026
- **User Stories**:
  - View monthly batch status (submitted, pending, failed)
  - View invoice details (source from M3, destination to MyInvois)
  - Download error report (CSV, Excel)
  - View submission history (last 12 months)
  - Role-based views (Finance Officer vs. Accounts Manager)

### Initiative [I002]: Enhanced Error Recovery
- **Business Value**: Finance team can fix validation errors and resubmit without IT involvement
- **Manufacturing Impact**: Faster resolution of data issues; reduces batch rework
- **Dependencies**:
  - Portal UI (see Initiative I001)
  - Data fix workflow
  - Resubmission logic
- **Risk Level**: Medium (complex validation state management)
- **Target Completion**: June 2026
- **User Stories**:
  - View validation errors per invoice
  - Update invoice data fields (fix typos, missing data)
  - Resubmit failed invoices
  - Bulk fix for common errors

### Initiative [I003]: Real-Time Audit Dashboard
- **Business Value**: IT team can monitor batch execution in real-time (not after completion)
- **Manufacturing Impact**: Faster issue detection; proactive alerting
- **Dependencies**:
  - Dashboard UI
  - Real-time logging to SQL Server
  - Alert system (email/SMS)
- **Risk Level**: Low (uses existing data)
- **Target Completion**: April 2026
- **Metrics to Display**:
  - Batch progress (X of 1000 submitted)
  - Success rate (%)
  - Error rate (%)
  - Average submission time (ms)
  - Retry queue depth

### Initiative [I004]: Multi-Plant Support
- **Business Value**: Extend service to other plants (future acquisition/expansion)
- **Manufacturing Impact**: Centralized e-invoicing for all plants; scale operations
- **Dependencies**:
  - Configuration per plant
  - Separate audit trails per plant
  - Tenant management
- **Risk Level**: High (architectural change)
- **Target Completion**: Q3 2026 (after Phase 2 stabilization)

### Initiative [I005]: MyInvois API Rate Optimization
- **Business Value**: Reduce submission time from ~30 min to ~10 min (3x faster)
- **Manufacturing Impact**: Faster compliance; more time for other tasks
- **Dependencies**:
  - Batch request API (if MyInvois provides)
  - Parallel submission strategy
- **Risk Level**: Medium (requires MyInvois API changes)
- **Target Completion**: May 2026

---

## Mid Term (6-12 months) - Phase 3

### Initiative [I006]: Intelligent Error Prediction
- **Business Value**: Catch validation errors before submission; reduce rework
- **Manufacturing Impact**: Higher first-time success rate; fewer manual fixes
- **Dependencies**:
  - AI/ML model (optional, vendor library)
  - Historical error data (learn from Phase 1-2)
- **Risk Level**: High (ML model training, data science)
- **Target Completion**: Q4 2026
- **Approach**:
  - Analyze historical errors (Phase 1-2)
  - Identify patterns (common missing fields, typos)
  - Train simple classifier (Random Forest, etc.)
  - Pre-submission scoring
  - Alert finance team on high-risk invoices

### Initiative [I007]: Automated Data Quality Remediation
- **Business Value**: Auto-fix common errors (trim spaces, fix currencies, etc.)
- **Manufacturing Impact**: Reduces manual data cleanup work
- **Dependencies**:
  - Error pattern library
  - Fix rules engine
  - Finance team sign-off on rules
- **Risk Level**: Medium (must not corrupt data)
- **Target Completion**: Q3 2026
- **Examples**:
  - Trim whitespace from fields
  - Convert currency codes (USD → MYR)
  - Fill default values (country, tax code)
  - Validate & reformat dates

### Initiative [I008]: Cloud Deployment (Azure)
- **Business Value**: Reduce on-premises infrastructure; improve availability
- **Manufacturing Impact**: Better uptime; easier scaling
- **Dependencies**:
  - Azure SQL Database setup
  - Azure Key Vault for credentials
  - Deployment automation
- **Risk Level**: Medium (Azure learning curve)
- **Target Completion**: Q4 2026
- **Deliverables**:
  - Run service in Azure App Service
  - Migrate audit database to Azure SQL
  - Use Azure Key Vault for secrets
  - Implement Azure Application Insights monitoring

### Initiative [I009]: Integration with Finance Systems
- **Business Value**: Sync submission status back to M3 (mark invoice as "submitted")
- **Manufacturing Impact**: Finance team knows status in M3 (no need for separate system)
- **Dependencies**:
  - M3 API write access
  - Change control approval from MOVEX team
- **Risk Level**: High (modifying M3 data)
- **Target Completion**: Q1 2027

---

## Long Term (12+ months) - Phase 4+

### Strategic Direction [S001]: Real-Time Invoice Processing
- **Vision**: Accept invoices throughout the month; submit in real-time (not batch)
- **Prerequisites**:
  - Phase 2 UI/UX stable
  - Multi-plant support complete
  - Performance optimizations done
- **Evaluation Criteria**:
  - Business need validated (finance wants real-time?)
  - Architecture redesign (async/event-driven)
  - Complexity vs. benefit analysis
- **Estimated Timeline**: 2027+ (low priority; monthly batch adequate)

### Strategic Direction [S002]: E-Invoice Archival & Retrieval
- **Vision**: Long-term archive of e-invoices (compliance + retrieval)
- **Prerequisites**:
  - 7-year audit trail stable
  - Cold storage strategy defined
  - Retrieval API for auditors
- **Evaluation Criteria**:
  - Audit requirements validated
  - Cost-benefit of archival strategy
  - Auditor feedback
- **Estimated Timeline**: 2027+ (when audit trail reaches 1+ year)

### Strategic Direction [S003]: Analytics & Reporting
- **Vision**: Submission metrics, trend analysis, anomaly detection
- **Prerequisites**:
  - 12 months of data collected
  - Audit dashboard working (Phase 2)
  - Business questions identified
- **Evaluation Criteria**:
  - Business value of insights
  - Effort to build analytics
  - Tool selection (Power BI, Tableau, custom)
- **Estimated Timeline**: 2027+ (after Phase 3)

---

## Phase 2 Architecture & Integration Details

### Phase 2 System Architecture

```
┌─────────────────────┐
│  MOVEX-Portal       │
│  (ASP.NET Core)     │
│  - Finance UI       │
│  - RBAC (AD groups) │
│  - User sessions    │
└─────────┬───────────┘
          │ HTTP REST API
          │ (Bearer token)
          ▼
┌─────────────────────┐
│ MyInvois-Service    │
│ (.NET 8.0 Worker)   │
│ - Batch processing  │
│ - OAuth 2.0 mgmt    │
│ - Validation        │
│ - Submission        │
└─────────┬───────────┘
          │ HTTPS OAuth 2.0
          │ + XAdES signature
          ▼
┌─────────────────────┐
│  MyInvois API       │
│  (Malaysia LHDNM)   │
│  - UBL 2.1 submit   │
│  - UUID generation  │
│  - Status polling   │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  SQL Server         │
│  [dbo].[AuditLog]   │
│  - All submissions  │
│  - Errors logged    │
│  - Retries tracked  │
└─────────────────────┘
```

### Phase 2 Error Handling & Retry

**Error Classification:**

| HTTP Status | Error Code | Category | Retry? | Manual Review? | Action |
|-------------|-----------|----------|--------|----------------|--------|
| 400 | DS101 | Validation | ❌ No | ✅ Yes | Check mandatory fields |
| 400 | DS302 | Duplicate | ❌ No | ✅ Yes | Verify against audit log |
| 401 | InvalidToken | Auth | ✅ Yes (1x) | ❌ No | Refresh OAuth token |
| 429 | RateLimit | Rate Limit | ✅ Yes (exp backoff) | ❌ No | Wait & retry (batch logic handles) |
| 500 | ServerError | Server | ✅ Yes (3x) | ⚠️ Maybe | Transient, will recover |

**Resilience (Polly circuit breaker pattern):**
- Exponential backoff: 5s → 10s → 20s
- Circuit breaker: Break after 5 failures, reset after 1 minute
- Timeout policy: 30-second connection timeout

### Phase 2 Audit Trail & Reporting

**Audit Log Schema:**

| Column | Type | Purpose | Example |
|--------|------|---------|---------|
| AuditId | GUID | Unique identifier | 3fa85f64-5717-4562-b3fc-2c963f66afa6 |
| Timestamp | DateTime | When logged (UTC) | 2026-02-01 10:15:30.123 |
| Action | varchar(50) | What happened | MyInvois_Submit |
| Status | varchar(20) | Success/Failed/Pending | Success |
| MyInvoisUUID | GUID | Returned by API | 550e8400-e29b-41d4-a716-446655440000 |
| InvoiceNumber | varchar(50) | MOVEX invoice ID | INV-2026-00042 |
| ErrorCode | varchar(20) | MyInvois error | DS302 |
| RetryCount | int | Number of retries | 2 |
| DurationMs | int | Processing time | 2345 |

**Operational Views:**
- vw_MyInvois_FailedSubmissions: For retry operations
- vw_MyInvois_MonthlySummary: For reporting
- Compliance reports: 7-year retention, searchable archive

### Phase 2 Performance Requirements

| Metric | Target | Measurement |
|--------|--------|-------------|
| Per-invoice submission | < 5 seconds | Including network roundtrip |
| Batch of 100 invoices | < 8 minutes | Sequential, with OAuth overhead |
| Overall success rate | ≥ 95% | First attempt + retries |
| No manual retry needed | ≥ 90% | Retries catch most failures |
| Database audit query | < 100ms | View with filters |

### Phase 2 Monitoring & Support

**Service Health Endpoints** (Phase 2):
```http
GET /api/v1/health
Authorization: Bearer <token>

Response:
{
  "status": "Healthy",
  "version": "1.0.0",
  "lastBatchRun": "2026-02-01T10:15:30Z",
  "pendingInvoices": 23,
  "failedSubmissions": 2,
  "database": "Connected"
}
```

**Portal Responsibilities** (Phase 2):
- Display service health (polling /health every 5 min)
- View audit logs with filtering
- Manual retry of failed submissions
- Download error reports (CSV, Excel)
- View submission history (last 12 months)

**Support Levels (Phase 2):**
- L1 Support: Finance Team (troubleshooting guide)
- L2 Support: IT Ops / Portal Team (infrastructure/config)
- L3 Support: Development Team (code/design)
- L4 Support: MyInvois Support (external API)

**Monitoring Tools** (Phase 2):
- Application Insights for metrics
- Grafana dashboard: success rate, latency, error rate
- PagerDuty alerts for critical failures
- Weekly PowerShell health check scripts

---

## Technical Debt & Maintenance

### High Priority (Next 2 months)
- [ ] Add comprehensive unit tests (target: 85% coverage)
- [ ] Document all validation rules in code
- [ ] Create runbooks for common issues
- [ ] Implement health checks for dependencies (M3, MyInvois, DB)

### Medium Priority (Next 3-6 months)
- [ ] Refactor validation logic into separate service (testability)
- [ ] Add performance benchmarks (measure batch time)
- [ ] Create CI/CD pipeline for automated testing
- [ ] Document architectural decisions (ADR format)

### Scheduled Maintenance
- **Dependency Updates**: Monthly (NuGet packages)
  - Security patches: Within 48 hours
  - Minor updates: Monthly
  - Major updates: Quarterly + testing
- **Platform Upgrades**: 
  - .NET LTS updates: Quarterly (test 1 month before)
  - SQL Server patches: Per Microsoft schedule
  - OS patches: Monthly (security Tuesday)
- **Security Patches**: 
  - Critical: Within 24 hours
  - High: Within 1 week
  - Medium: Within 1 month

---

## Sunset/Deprecation Plans

### Manual MyInvois Portal Submission (Current Workaround)
- **When This Gets Deprecated**: When automated batch is stable (Phase 1)
- **Reason**: Automated batch replaces manual submission entirely
- **Timeline**: Deprecate in June 2026; remove in December 2026
- **Migration Path**: 
  - Transition all submissions to automated batch
  - Keep portal access as emergency fallback
  - Finance team trained on automated process
  - Support for manual submission ends Dec 2026

### File-Based Audit Logging (If Implemented)
- **When This Gets Deprecated**: When SQL Server audit logging is validated
- **Reason**: SQL Server better for compliance + analytics
- **Timeline**: Deprecate in March 2026; remove in June 2026
- **Migration Path**:
  - Migrate all files to SQL Server database
  - Validate data integrity
  - Archive old files to cold storage (7-year retention)
  - Update documentation

---

## Success Metrics

### Phase 1 (Current - Launch)
- ✅ 100% of monthly invoices processed automatically
- ✅ 95%+ first-time submission success rate
- ✅ Zero compliance violations (all invoices auditable)
- ✅ < 5% data validation error rate
- ✅ Finance team happy with batch timing

### Phase 2 (UI + Portal)
- Target: 100% of finance team using self-service portal
- Target: 90%+ of errors fixed by finance team without IT
- Target: Batch completion time < 30 minutes
- Target: Email alert delivery within 5 minutes of error
- Target: Zero manual resubmissions (all handled by system)

### Phase 3 (Analytics + Cloud)
- Target: 99%+ system availability
- Target: Batch time < 10 minutes (3x improvement)
- Target: < 2% error rate (vs. 5% baseline)
- Target: Cloud cost < RM 1,000/month
- Target: Auditor satisfaction 8/10

### Phase 4+ (Long-term)
- Target: Real-time invoice processing (if adopted)
- Target: < 1% invoice rejects after AI improvements
- Target: 7-year audit trail fully searchable
- Target: 100% user adoption across all plants (if multi-plant)

---

## Budget & Resource Planning

### Phase 2 (6 months, 2-3 developers)
- Development: 360 hours (UI, portal, error recovery)
- Testing & QA: 180 hours
- Deployment & training: 80 hours
- **Total**: ~620 hours

### Phase 3 (6 months, 2 developers + optional ML specialist)
- Development: 300 hours (dashboard, cloud migration)
- ML/AI (optional): 200 hours (if proceeding with prediction)
- Testing & QA: 150 hours
- Deployment & training: 80 hours
- **Total**: ~730 hours (or ~500 without ML)

---

## Related Documentation
- [00-product-vision.md](00-product-vision.md) - Detailed vision & business outcomes
- [01-manufacturing-context.md](01-manufacturing-context.md) - Operational constraints
- [01-system-architecture.md](01-system-architecture.md) - Technical approach
- [03-myinvois-requirements.md](03-myinvois-requirements.md) - Requirements & traceability
- [08-governance-and-decisions.md](08-governance-and-decisions.md) - Approval process
- [09-implementation-decisions.md](09-implementation-decisions.md) - Architecture decisions (ADRs)
- [10-testing-strategy.md](10-testing-strategy.md) - Testing strategy & test cases
- [ai/planning/initiative.md](../planning/initiative.md) - Initiative planning template
- [ai/planning/execution-plan.md](../planning/execution-plan.md) - Execution guidance
