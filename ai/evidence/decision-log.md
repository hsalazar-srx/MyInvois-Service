# Decision Log (Evidence) - MyInvois-Service

**Purpose:** Chronological log of decisions with evidence links  
**Owner:** Tech Lead  
**Maintained:** Ongoing

---

## Canonical ADRs

All Architecture Decision Records are maintained in:
- `ai/memory/09-implementation-decisions.md` (canonical source)

Use this file for **evidence** of decisions (approvals, meeting notes, impacts), not for duplicating full ADR text.

---

## ADR Index (Reference)

| ADR | Title | Canonical Record | Evidence/Notes |
|-----|-------|------------------|----------------|
| ADR-001 | Standalone Service vs Portal Integration | `ai/memory/09-implementation-decisions.md` | Approval captured in architecture review notes |
| ADR-002 | Monthly Batch Processing (Not Daily) | `ai/memory/09-implementation-decisions.md` | Finance approval recorded |
| ADR-003 | SQL Server for Audit Logs (Workspace Standard) | `ai/memory/09-implementation-decisions.md` | Workspace rule alignment |
| ADR-004 | Batch Size: Sales 100, Purchase 50 | `ai/memory/implementation-decisions.md` | Rate limit calculations validated |
| ADR-005 | XAdES Signature Using MyInvois SDK | `ai/memory/implementation-decisions.md` | Compliance confirmation |
| ADR-006 | Retry Strategy: Limited Retries, Manual Override | `ai/memory/implementation-decisions.md` | Finance + Ops alignment |
| ADR-007 | Token Caching (1 Hour TTL) | `ai/memory/implementation-decisions.md` | Performance considerations logged |
| ADR-008 | Error Classification (No-Retry vs Retriable) | `ai/memory/implementation-decisions.md` | Risk assessment attached |
| ADR-009 | Validators as Separate Classes (Not Inline) | `ai/memory/implementation-decisions.md` | Maintainability rationale |
| ADR-010 | MyInvois UUID as Primary Tracking ID | `ai/memory/implementation-decisions.md` | Audit traceability note |
| ADR-011 | Audit Log Retention: 7 Years | `ai/memory/implementation-decisions.md` | Compliance requirement |
| ADR-012 | Phase 1 Focus: Core Submission Only | `ai/memory/implementation-decisions.md` | Scope control agreement |

---

| ADR-013 | Replace MOVEX REST API with DB2 Direct Access | `ai/memory/09-implementation-decisions.md` | User-initiated architectural change; skills audit updated |
| ADR-014 | SQLite Audit Storage via EF Core | `ai/memory/09-implementation-decisions.md` | Architecture Review approved March 7, 2026; see `ai/evidence/decision-001-sqlite-audit-storage.md` |

---

## Operational / Process Decisions (Non-ADR)

| Date | Decision | Owner | Evidence | Impact |
|------|----------|-------|----------|--------|
| 2026-02-06 | Consolidated ADRs into `implementation-decisions.md`; decision-log now index only | Tech Lead | Updated governance docs | Reduced duplication, clearer navigation |
| 2026-02-06 | Governance moved to `ai/memory/08-governance-and-decisions.md` | Tech Lead | File rename + reference updates | Eliminated 04-04 numbering conflict |
| 2026-02-17 | Day 3 Complete: All 5 validators + MyInvoisMapper implemented | Dev Team | 112 total tests (190% of planned 59) | Configuration-based company settings, all validators non-fail-fast |
| 2026-02-17 | Day 4 Complete: MyInvoiceSubmitter with OAuth + Polly retry | Dev Team | 8 comprehensive tests | Token caching, rate limit retry, non-retriable error detection (DS301, DS302) |
| 2026-02-17 | .NET Solution initialized: MyInvois.Service.sln + 2 projects | Dev Team | Solution builds with 0 errors | Transitioned from design-only scaffold to compilable .NET 8.0 solution |
| 2026-02-17 | Day 5 Complete: AuditLogger with SQL Server persistence | Dev Team | 10 tests (184 total, 100% pass) | ISO 27001 compliant, IDbConnection injection, duplicate detection, failed query |
| 2026-02-17 | All 15 test failures resolved across 5 components | Dev Team | 174→184 tests, 0 failures | TIN signature fix, Date assertion fix, Moq delegate alignment, Polly retry count |
| 2026-02-18 | Week 3: ADR-013 Gap #2 closed — Invoice line items now flow through pipeline | Dev Team | 224 tests (100% pass) | RawInvoiceLineRecord DTO, MovexInvoiceReader maps lines, E2E pipeline validated end-to-end |
| 2026-02-18 | Week 3: InvoiceProcessor orchestrator + Integration/E2E tests | Dev Team | 5 integration + 5 E2E tests | Full pipeline: Reader→Mapper→Validators→Submitter→AuditLogger |
| 2026-02-18 | Fixed IMyInvoiceMapper → IMyInvoisMapper interface alignment | Dev Team | InvoiceProcessor uses real mapper | Removed old stub interface dependency |
| 2026-02-19 | DirectQueryDataSource fully implemented with Dapper+ODBC | Dev Team | 224 tests (100% pass) | Replaces NotImplementedException stubs; AP/AR header queries, batch line items, dictionary-based schema mapping |
| 2026-02-19 | ADR-013 Gap #3 closed — DB2 driver: System.Data.Odbc (not Net.IBM.Data.Db2) | Dev Team | NuGet: System.Data.Odbc 9.0.2 | AS/400 compatibility; ODBC driver more reliable for IBM i systems |
| 2026-02-19 | Company environment isolation: Dev uses CMP300, Prod uses CMP100 | Dev Team | appsettings.Development.json updated | ActiveCompanyCodes controls which companies are queried per environment |
| 2026-03-04 | Phase 2 kickoff: ADR-014 authored — SQLite replaces SQL Server for audit storage | architect-system-design | `ai/evidence/decision-001-sqlite-audit-storage.md` created | `IAuditLogger` interface unchanged; Sprint 6 implements code changes |
| 2026-03-07 | Architecture Review sign-off: ADR-014 approved | IT Manager | `decision-001-sqlite-audit-storage.md` reviewed; clean architecture confirmed, security (BitLocker + NTFS ACL) documented | Sprint 6 code changes unblocked; WORKSPACE_RULES.md updated |
| 2026-03-09 | Sprint 5 complete: all 5 Sprint 5 deliverables created/updated | Dev Team | decision-001, ADR-014, audit-logging.md, governance docs, decision-log all updated | Sprint 6 SQLite implementation starts March 10 |
| 2026-03-30 | **Go-live deferred to 2026-04-30** — Finance team capacity unavailable for data validation and UAT; e-invoicing not a current Finance priority | Project Lead | ADR-016 § Amendment 2026-03-30 (second extension: Feb 28 → Mar 31 → Apr 30) | Sprint 7 extended; UAT rescheduled to Sprint 8 (Apr 21-30) |

---

## How to Add New Entries

1. **Architecture decision** → Create or update ADR in `implementation-decisions.md`.
2. **Add evidence** → Log the decision here with link to meeting notes/approvals.
3. **If user-visible** → Update `ai/evidence/release-notes.md` and `ai/evidence/change-impact.md`.
