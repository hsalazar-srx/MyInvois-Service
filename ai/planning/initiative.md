# MyInvois Initiative: Scope & Planning

## Initiative Overview

**Name:** MVAI - MyInvois Integration for MOVEX ERP  
**Owner:** [Project Manager Name]  
**Duration:** 4 weeks (Feb 3-28, 2026)  
**Team Size:** 3 people (Dev, QA, Ops)  
**Budget:** [To be confirmed]  
**Priority:** High (Regulatory requirement)

---

## Business Context

### Problem Statement
MOVEX ERP currently has no integration with Malaysian government's MyInvois e-invoicing platform. This creates:
- **Risk:** Non-compliance with government e-invoicing mandate
- **Burden:** Manual submission of 100 sales + 500-1000 purchase invoices per month
- **Error Rate:** ~5-10% manual submission errors
- **Effort:** 40+ hours/month of finance team effort

### Business Objective
Enable automated e-invoicing for MOVEX ERP with ≥95% submission success rate, reducing finance team burden and ensuring regulatory compliance.

### Success Criteria
1. **Functional:** ≥95% of invoices submitted successfully each month
2. **Operational:** Process completes in <2 hours on 1st of month
3. **Quality:** Zero data loss, 100% audit trail
4. **Timeline:** Production go-live by Feb 28, 2026
5. **Team:** Dev team can maintain independently

---

## Scope Definition

### In Scope (Phase 1)

**Core Submission:**
- ✅ Fetch sales invoices from MOVEX (100/month)
- ✅ Fetch purchase invoices from MOVEX (500-1000/month)
- ✅ Transform to MyInvois UBL 2.1 format
- ✅ Validate mandatory fields (20+ fields)
- ✅ Submit to MyInvois API
- ✅ Log results to SQL Server audit table
- ✅ Basic error handling & retry (3 attempts)
- ✅ Monthly batch processing (1st of month)

**Documentation:**
- ✅ Architecture & design decisions
- ✅ Operational guides (setup, deployment, troubleshooting)
- ✅ Requirements traceability (spec → code mapping)
- ✅ Test strategy & test cases
- ✅ AI-assisted development workspace

**Testing:**
- ✅ 54+ unit tests (≥80% coverage)
- ✅ 20+ integration tests
- ✅ 5 E2E scenarios
- ✅ Finance UAT (50 test invoices)

### Out of Scope (Phase 2)

**User Interface:**
- ❌ Web UI for manual submission
- ❌ Dashboard for monitoring
- ❌ Retry interface for failed invoices

**Advanced Features:**
- ❌ Polly circuit breaker (advanced retry)
- ❌ Real-time monitoring & alerting
- ❌ Invoice history API
- ❌ Portal integration

**Enhancements:**
- ❌ Amendment handling (credit notes, adjustments)
- ❌ Multi-company support
- ❌ Custom field mapping

---

## Timeline & Milestones

### Week 1: Scaffold (Feb 3-7) ✅ COMPLETE

**Deliverables:**
- ✅ Architecture design (12 ADRs) — Decisions documented
- ✅ Requirements traceability (1000+ lines) — Spec captured
- ✅ Project structure (70+ files) — Scaffolding created
- ✅ Configuration framework (7 classes) — Empty stubs ready
- ✅ Data models (5 DTOs) — Empty stubs ready
- ✅ Service interfaces (5 services) — Interfaces designed, no code
- ✅ Validator stubs (5 validators) — Stubs created, no logic
- ✅ Database schema (2 SQL scripts) — Scripts created, not executed
- ✅ Operational documentation (4 guides) — README, SETUP, DEPLOYMENT, TROUBLESHOOTING
- ✅ AI workspace (12+ memory files) — Planning + governance
- ✅ SRX template compliance — Pre-commit hooks, skills audit, rules enforcement

**Status:** Design phase 100% complete. Implementation ready to start.

### Week 2: Implementation (Feb 10-14) ⏳ IN PROGRESS
- requirements-traceability.md (spec → code mapping)
- ai/rules.md (critical operating instructions)
- QUICK_REFERENCE.md (developer cheat sheet)

---

### Week 2: Implementation (Feb 10-14)

**Deliverables:**
- [ ] MovexInvoiceReader implementation
- [ ] MyInvoiceMapper implementation
- [ ] All 5 Validators implementation
- [ ] MyInvoiceSubmitter implementation
- [ ] AuditLogger implementation
- [ ] 54+ unit tests implemented
- [ ] Unit test coverage: ≥80%

**Key Milestones:**
- Day 1: MovexInvoiceReader complete
- Day 3: Validators complete
- Day 5: All services complete + unit tests
- End of week: All tests passing

**Success Criteria:**
- [ ] All unit tests passing
- [ ] Code coverage ≥80%
- [ ] Zero build warnings
- [ ] Peer review approved

---

### Week 3: Testing & Integration (Feb 17-21)

**Deliverables:**
- [ ] 20+ integration tests implemented
- [ ] 5 E2E test scenarios implemented
- [ ] Performance baseline established
- [ ] UAT test cases prepared
- [ ] Operations runbooks updated

**Key Milestones:**
- Day 1: Integration tests complete
- Day 3: E2E tests complete
- Day 4: Performance testing done
- Day 5: UAT test cases ready

**Success Criteria:**
- [ ] All integration tests passing
- [ ] E2E tests passing
- [ ] Response time <5s per invoice
- [ ] ≥95% success rate in test environment

---

### Week 4: Go-Live Preparation (Feb 24-28)

**Deliverables:**
- [ ] Finance UAT completion
- [ ] Production environment setup
- [ ] MVAI dry run (sandbox batch)
- [ ] Go-live execution
- [ ] Post-go-live monitoring

**Key Milestones:**
- Day 1: Finance UAT execution
- Day 2: UAT sign-off
- Day 4: Production dry run
- Day 5: Go-live (10 AM UTC)

**Success Criteria:**
- [ ] Finance UAT passed (≥95% success)
- [ ] Sandbox dry run successful
- [ ] Production ready
- [ ] Go-live executed successfully

---

## Resource Planning

### Team Composition

| Role | Name | Hours/Week | Responsibility |
|------|------|-----------|-----------------|
| Developer | [Name] | 40 | Code implementation (all services) |
| QA Engineer | [Name] | 40 | Testing (unit, integration, E2E, UAT) |
| Operations | [Name] | 20 | Deployment, monitoring, runbooks |
| Tech Lead | [Name] | 20 | Architecture guidance, code review |
| Project Manager | [Name] | 15 | Coordination, status reporting |

**Total Effort:** ~1400 hours over 4 weeks

### Dependencies

**External:**
- ✅ MOVEX database (DB2 on AS/400) access (available)
- ✅ MyInvois Sandbox environment (available)
- ✅ MyInvois Production access (pending approval)
- ✅ SQL Server 2019+ (available)
- ✅ .NET 8.0 SDK (available)

**Internal:**
- ✅ Finance team for UAT (scheduled week 4)
- ✅ Ops team for deployment (scheduled week 4)
- ✅ Security team for SSL certificate (ordered)

### Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|-----------|
| MOVEX database downtime | Missed batch | Low (5%) | Manual submission workaround |
| MyInvois API changes | Implementation delay | Low (10%) | Monitor API changelog weekly |
| Certificate delivery | Go-live delay | Medium (20%) | Backup vendor identified |
| Data validation issues | Failed submissions | Medium (15%) | 5 comprehensive validators |
| Rate limit exceeds | Throttling/errors | Low (5%) | 600ms batch delay = safe margin |
| Finance UAT fails | Go-live delay | Medium (20%) | Early test environment access |

---

## Communication & Governance

### Stakeholders

| Stakeholder | Role | Interest | Update Frequency |
|------------|------|----------|-----------------|
| CFO | Sponsor | Compliance, risk | Weekly status |
| Finance Manager | User | Timely submission | Weekly status |
| IT Director | Governance | Security, compliance | Bi-weekly |
| DevOps Lead | Operations | Deployment, monitoring | Weekly |
| Security Officer | Compliance | Encryption, audit trail | As needed |

### Governance

**Decision Making:**
- Architecture decisions: Tech Lead approval required
- Scope changes: Project Manager + Tech Lead approval
- Go-live readiness: Project Manager + QA + Ops approval

**Review Points:**
- End of Week 1: Scaffold review
- End of Week 2: Implementation review
- End of Week 3: Testing review
- Day before go-live: Final readiness review

**Escalation:**
- Blockers: To Project Manager immediately
- Critical bugs: To Tech Lead + Project Manager
- Go-live issues: To IT Director + CFO

### Reporting

**Weekly Status Report:**
- Deliverables completed
- Risks & mitigations
- Blockers & dependencies
- Forecast for next week

**Go-Live Report:**
- Final checklist status
- Success rate achieved
- Issues & resolutions
- Recommendations for Phase 2

---

## Budget & Resource Allocation

### Development Costs

| Item | Cost | Notes |
|------|------|-------|
| Developer time | [40h × rate] | 4 weeks implementation |
| QA time | [40h × rate] | 4 weeks testing |
| Ops time | [20h × rate] | Deployment & monitoring |
| Tech lead review | [20h × rate] | Architecture & code review |
| Project management | [15h × rate] | Coordination & reporting |
| **Total Labor** | **~1400 hours** | **4 weeks, 3 people** |

### Infrastructure Costs

| Item | Cost | Notes |
|------|------|-------|
| SQL Server license | [Existing] | Already available |
| .NET 8.0 | [Free] | Open source |
| Development tools | [Existing] | VS, SSMS, etc. |
| MyInvois cert | [~RM500] | Digital signature certificate |
| **Total Infrastructure** | **~RM500** | **One-time cost** |

### Total Initiative Cost

**~[Calculate based on hourly rates]** for complete delivery

---

## Success Definition & Handoff

### Phase 1 Success Criteria

1. **Functionality:**
   - [ ] All services implemented
   - [ ] All validators working
   - [ ] All 54+ tests passing
   - [ ] ≥95% success rate in test environment

2. **Quality:**
   - [ ] Code coverage ≥80%
   - [ ] Zero critical defects
   - [ ] Architecture review approved
   - [ ] Security review passed

3. **Documentation:**
   - [ ] Setup guide complete
   - [ ] Deployment guide complete
   - [ ] Troubleshooting guide complete
   - [ ] Operations runbooks ready

4. **Deployment:**
   - [ ] Production environment ready
   - [ ] Dry run successful
   - [ ] Go-live executed
   - [ ] Service running in production

### Handoff Checklist

- [ ] Code deployed to production
- [ ] Audit tables created & verified
- [ ] Finance team trained
- [ ] Ops team trained
- [ ] 24/7 support plan established
- [ ] Phase 2 planning initiated

### Phase 2 Planning

**Planned initiatives:**
- Portal UI for manual submission & retry
- Advanced retry (Polly circuit breaker)
- Real-time monitoring & alerting
- Multi-company support
- Amendment handling

**Success criteria for Phase 2:**
- User-friendly submission interface
- <1 minute submission response time
- Self-service retry capability
- ≥99% long-term success rate

---

## Key Decisions Made

### 1. Hybrid Architecture
**Decision:** Standalone service + Phase 2 portal  
**Why:** Rate limits don't support real-time, batch is simpler

### 2. Monthly Batch Schedule
**Decision:** 1st of month, 2-hour window  
**Why:** Aligns with business practice, safe under rate limits

### 3. SQLite Audit (revised from SQL Server — see ADR-014)
**Decision:** SQLite via EF Core 8, embedded on IIS server, per-project file
**Why:** Remove SQL Server dependency; <500 invoices/day fits SQLite comfortably; OS-level encryption (BitLocker) satisfies ISO 27001 in lieu of TDE
**Supersedes:** Original decision to use SQL Server (workspace standard for shared/enterprise deployments still applies; SQLite approved for self-hosted IIS only)

### 4. Phase 1 Scope
**Decision:** Core submission only  
**Why:** Faster delivery, MVP by Feb 28, Phase 2 for UI

### 5. XAdES via SDK
**Decision:** Use MyInvois SDK for signing  
**Why:** Proven implementation, reduce security risk

---

## Initiative Constraints

**Hard Constraints:**
- Go-live deadline: Feb 28, 2026 (regulatory mandate)
- Rate limit: 100 requests/minute from MyInvois
- Batch size: ≤1000 invoices per run
- Retention: 7 years minimum

**Soft Constraints:**
- Budget: ~[TBD] (cost optimization encouraged)
- Team size: 3 people (no additional hiring)
- Infrastructure: Use existing SQL Server & .NET

**Flexibility:**
- Scope can defer to Phase 2 if needed
- Timeline can extend to Mar 7 if critical issues found
- Team can scale up for deployment week if needed

---

## Phase 2: Audit Storage Migration (SQLite)

**Start:** March 4, 2026
**Sprints:** 5 (ADR), 6 (Implementation), 7 (Compliance)

### Phase 2 Objective
Replace SQL Server audit logging with SQLite embedded on the IIS server, eliminating the external SQL Server dependency while maintaining ISO 27001 compliance via OS-level encryption (BitLocker).

### Phase 2 Scope

**In Scope:**
- Replace `System.Data.SqlClient` + ADO.NET with `Microsoft.Data.Sqlite` + EF Core 8
- Preserve `IAuditLogger` interface (zero consumer impact)
- WAL mode, NTFS ACL, BitLocker documentation
- Backup runbook and security review

**Out of Scope:**
- Centralized shared audit DB with SM-Portal (see decision-001 — separate DBs chosen; revisit if SM-Portal becomes API gateway)
- Data migration of existing SQL Server audit rows (historical data stays in SQL Server)
- Changes to `IAuditLogger` interface or any service that calls it

### Phase 2 Success Criteria
- [ ] `audit.db` replacing SQL Server for new submissions
- [ ] All 225+ tests passing with SQLite backend
- [ ] `System.Data.SqlClient` removed from project
- [ ] Architecture Review and Security Review sign-offs obtained
- [ ] Deployment docs updated (BitLocker + backup runbook)

### Phase 2 Evidence
- `ai/evidence/decision-001-sqlite-audit-storage.md` (to be created in Sprint 5)
- `ai/evidence/decision-log.md` (Architecture Review entry)

---

## Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | Feb 5, 2026 | [Name] | Initial planning document |
| 2.0 | Mar 4, 2026 | architect-system-design | Phase 2 added — SQLite audit migration; ADR-002 (SQL Server) superseded by ADR-014 |

---

**Owner:** Project Manager
**Status:** Phase 1 Complete | Phase 2 In Progress (Sprint 5)
**Next Review:** Sprint 5 close — Mar 7, 2026

