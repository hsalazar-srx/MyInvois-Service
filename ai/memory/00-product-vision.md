
# MyInvois-Service - Product Vision & Objectives

**Last Updated**: 2026-02-16  
**Status**: MVAI Iteration 1 (Active)  
**Version**: 1.0

## 🎯 Vision Statement

Enable seamless, compliant e-invoicing to Malaysian Inland Revenue Board's (LHDNM) MyInvois system with automated batch processing, comprehensive validation, and audit-trail integrity—transforming manual invoice submission into a secure, scalable integration.

---

## 🧩 Problem Being Solved

### Current State (Pain Points)

1. **Manual Submission Process**
   - Finance team manually submits invoices to MyInvois portal
   - ~600-1100 invoices/month across sales & purchase
   - Time-consuming, error-prone, no audit trail
   - Compliance risk: No automated validation

2. **No Audit Trail**
   - Cannot prove MyInvois submissions in audits
   - No tracking of UIN/QR codes returned by MyInvois
   - Difficult to troubleshoot failed submissions
   - Tax authority may reject if no documentation

3. **Integration Gap**
   - MOVEX invoices created in M3 system
   - MyInvois requires different schema (UBL 2.1)
   - Manual transformation leads to errors
   - No validation against MyInvois constraints

4. **Scaling Challenges**
   - Manual process breaks if volume increases
   - Cannot handle real-time submission requirements
   - No recovery mechanism for failed submissions
   - High operational overhead

### Future State (Solution)

1. **Automated Batch Processing**
   - Scheduled monthly batch (1st of month)
   - Reads invoices from MOVEX database (DB2 on AS/400) automatically
   - Transforms to MyInvois schema
   - Submits ~600-1100 invoices with minimal human intervention

2. **Complete Audit Trail**
   - Every submission logged to SQL Server
   - MyInvois UUID, status, errors captured
   - Proof of compliance for audits
   - UIN/QR codes stored for reference

3. **Validation-First Pipeline**
   - Validates against MyInvois constraints before submission
   - Rejects invalid invoices (not wasted API calls)
   - Clear error messages for manual review
   - Traceability to business rules

4. **Scalable & Resilient**
   - Batch sizes tuned for rate limits (100 req/min)
   - Limited retries for transient failures
   - Manual intervention for persistent failures
   - Ready for future: daily/hourly submission

---

## 📊 Business Objectives

| Objective | Target | Timeline |
|-----------|--------|----------|
| **Reduce manual effort** | 95% less time on submission | Phase 1 (MVAI) |
| **Compliance ready** | 100% MyInvois constraints validated | Phase 1 (MVAI) |
| **Audit capability** | Complete trail for 7 years | Phase 1 (MVAI) |
| **Success rate** | ≥95% first-pass submission | Phase 1 (MVAI) |
| **Scalability** | Support daily submission if needed | Phase 2 |
| **User feedback** | Gather from finance team | Phase 2 |

---

## 🏗️ Solution Architecture (High-Level)

```
MOVEX (M3) Invoices
    ↓
MOVEX REST API (SRXWEBAPP1)
    ↓ (HTTP GET)
MyInvois-Service (Worker Service)
    ↓
    ├── Fetch invoices from MOVEX
    ├── Validate against MyInvois constraints
    ├── Transform to UBL 2.1 schema
    ├── Sign with digital certificate
    └── Submit to MyInvois API
    ↓
MyInvois Platform (LHDNM)
    ↓
Audit Log (SQL Server)
    └── Complete submission trail
```

---

## 📋 Scope & Constraints

### In Scope (Phase 1 - MVAI)

- ✅ Monthly batch processing (Sales + Purchase)
- ✅ MOVEX REST API integration (read invoices)
- ✅ MyInvois validation (5 validators)
- ✅ MyInvois submission (OAuth + REST API)
- ✅ Digital signature (XAdES v1.1)
- ✅ Audit logging (SQL Server)
- ✅ Error classification & handling
- ✅ Limited retries (3 attempts)
- ✅ Token caching (1-hour TTL)

### Out of Scope (Phase 1)

- ❌ Real-time submission (defer to Phase 2)
- ❌ Status polling/reconciliation (defer to Phase 2)
- ❌ Automatic retry with circuit breaker (defer to Phase 2)
- ❌ Portal UI dashboard (defer to Phase 2)
- ❌ Amendment/cancellation handling (defer to Phase 2)
- ❌ Self-billing (AR-only in Phase 1)
- ❌ EDI/PDF output (QR/UIN codes only)

---

## 🎯 Success Criteria

1. **Technical**
   - ✅ Service deployment successful
   - ✅ Monthly batch processes 600-1100 invoices
   - ✅ ≥95% first-pass submission success
   - ✅ Complete audit trail in SQL Server
   - ✅ All MyInvois constraints validated
   - ✅ ≥75% test coverage

2. **Operational**
   - ✅ Finance team confirms reduction in manual effort
   - ✅ Zero compliance violations
   - ✅ Audit department satisfied with trail
   - ✅ Support team confident in troubleshooting

3. **Business**
   - ✅ LHDNM accepts all submissions
   - ✅ No rejected invoices due to format
   - ✅ Cost savings from automation
   - ✅ Readiness for future scaling

---

## 📅 Timeline

| Phase | Duration | Deliverables |
|-------|----------|--------------|
| **MVAI (Phase 1)** | 4 weeks (Feb 5-28) | Core submission + validation + audit |
| **Phase 2** | 4 weeks (Mar 1-31) | UI dashboard + retry + status polling |
| **Phase 3** | TBD | Cloud migration + daily submission |

---

## 👥 Stakeholders

- **Finance Team**: Primary users (invoice submission)
- **IT Operations**: Deployment & support
- **Compliance/Audit**: Audit trail validation
- **Development Team**: Implementation & maintenance
- **LHDNM**: MyInvois platform (external)

---

## 🔑 Key Design Decisions

1. **Standalone Worker Service** (vs. integrate into Portal)
   - Separation of concerns
   - Independent scalability
   - Batch-centric processing

2. **Monthly Batch** (vs. daily/real-time)
   - Aligns with current volume & regulatory requirements
   - Simpler operational model
   - Easier testing & validation

3. **SQL Server for Audit** (vs. Db2)
   - Centralized compliance reporting
   - Better analytics capabilities
   - Future cloud migration path

4. **Validators as Separate Classes** (vs. inline)
   - Single responsibility
   - Testability
   - Reusability

5. **Limited Retries + Manual Override** (vs. automatic retry)
   - Operational control in Phase 1
   - Upgrade to automatic in Phase 2

---

## 📚 Related Documents

- [01-System Architecture](01-system-architecture.md) - Detailed architecture & component design
- [02-Data Model](02-data-model.md) - Database schema & data structures
- [03-MyInvois Requirements](03-myinvois-requirements.md) - Validation rules & field mapping
- [04-API Integration](04-api-integration.md) - MOVEX & MyInvois API specifications
- [05-Deployment Guide](05-deployment-guide.md) - Installation & operations
- [Implementation Decisions](09-implementation-decisions.md) - ADRs & technical choices
- [Testing Strategy](10-testing-strategy.md) - Unit, integration, E2E test plans

---

**Owner**: Development Team  
**Last Review**: 2026-02-05  
**Next Review**: 2026-03-05 (post-MVAI)

