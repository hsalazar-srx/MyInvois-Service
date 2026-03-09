# Sprint Backlog: Weekly Work Items & Progress Tracking

## Sprint 2: Implementation (Feb 10-14, 2026)

### Work Items Summary

| ID | Task | Status | Assignee | Effort | Priority |
|----|------|--------|----------|--------|----------|
| 2.1 | Implement MovexInvoiceReader | ⏳ Ready | Developer | 12h | P0 |
| 2.2 | Implement MyInvoiceMapper | ⏳ Ready | Developer | 12h | P0 |
| 2.3 | Implement Validators (5 classes) | ⏳ Ready | Developer | 12h | P0 |
| 2.4 | Implement MyInvoiceSubmitter | ⏳ Ready | Developer | 12h | P0 |
| 2.5 | Implement AuditLogger | ⏳ Ready | Developer | 8h | P0 |
| 2.6 | Create audit table in test DB | ⏳ Ready | Developer | 2h | P1 |
| 2.7 | Write unit tests (54+) | ⏳ Ready | Developer | 20h | P0 |
| 2.8 | Code review & approval | ⏳ Ready | Tech Lead | 10h | P0 |
| 2.9 | Fix defects & coverage gaps | ⏳ Ready | Developer | 8h | P0 |
| **Total** | | | | **96h** | |

---

### Daily Standup Notes

#### Monday, Feb 10

**Standup (10 AM UTC):**

| Team Member | Yesterday | Today | Blockers |
|--------------|-----------|-------|----------|
| Developer | Started MovexInvoiceReader | Continue Reader + start Mapper | None |
| QA | Reviewed test plan | Prepare test environment | None |
| Ops | Set up test DB | Monitor infrastructure | None |

**Progress:**
- [ ] MovexInvoiceReader: 30% complete
- [ ] Unit tests written: 5/54

---

#### Tuesday, Feb 11

**Standup (10 AM UTC):**

| Team Member | Yesterday | Today | Blockers |
|--------------|-----------|-------|----------|
| Developer | Completed Reader | Complete Mapper + start Validators | None |
| QA | Test environment ready | Set up test harness | None |
| Ops | Infrastructure monitoring | Continue monitoring | None |

**Progress:**
- [ ] MovexInvoiceReader: 100% complete (5/12 tests passing)
- [ ] MyInvoiceMapper: 50% complete
- [ ] Unit tests written: 17/54

---

#### Wednesday, Feb 12

**Standup (10 AM UTC):**

| Team Member | Yesterday | Today | Blockers |
|--------------|-----------|-------|----------|
| Developer | Completed Mapper | Validators 50%, start Submitter | Test DB schema issue? |
| QA | Test harness working | Start integration test writing | None |
| Ops | Infrastructure stable | Monitor deployments | None |

**Progress:**
- [ ] MyInvoiceMapper: 100% complete (8/8 tests passing)
- [ ] DateValidator: 50% complete
- [ ] Unit tests written: 32/54

---

#### Thursday, Feb 13

**Standup (10 AM UTC):**

| Team Member | Yesterday | Today | Blockers |
|--------------|-----------|-------|----------|
| Developer | Validators done, Submitter done | AuditLogger + orchestrator | None |
| QA | Integration tests drafted | Run full test suite | Coverage gaps? |
| Ops | All systems operational | Prepare for Week 3 | None |

**Progress:**
- [ ] All 5 validators: 100% complete (15/15 tests passing)
- [ ] MyInvoiceSubmitter: 100% complete (5/5 tests passing)
- [ ] Unit tests written: 47/54

---

#### Friday, Feb 14

**Standup (10 AM UTC):**

| Team Member | Yesterday | Today | Blockers |
|--------------|-----------|-------|----------|
| Developer | Completed Submitter | Final tests + code review | None |
| QA | Full test run | Coverage check | Coverage <80%? |
| Ops | Infrastructure ready | Documentation updates | None |

**Standup Notes:**
- Target: 54+ tests passing, ≥80% coverage
- Status: On track
- Next: Week 3 integration testing

**Progress:**
- [ ] All services implemented
- [ ] All 54 unit tests: 100% passing
- [ ] Code coverage: 82% (target ≥80%)
- [ ] Code review: Approved
- [ ] Ready for Week 3 integration testing

---

### Task Breakdown by Component

#### 2.1: MovexInvoiceReader Implementation

**Sub-tasks:**
- [ ] Set up IInvoiceDataSource with DB2 connection
- [ ] Implement GetPendingInvoices method
- [ ] Implement GetInvoiceById method
- [ ] Implement GetInvoicesByDateRange method
- [ ] Add DB2 connection retry logic
- [ ] Add exponential backoff for rate limits
- [ ] Write 12 unit tests
- [ ] Code review & approval

**Timeline:**
- Start: Monday 10 AM
- Target completion: Tuesday 2 PM
- Effort: 12 hours

**Test Cases (12):**
1. [ ] GetPendingInvoices - valid month - returns sales and purchase
2. [ ] GetPendingInvoices - no invoices - returns empty list
3. [ ] GetPendingInvoices - DB2 transient error - retries and succeeds
4. [ ] GetPendingInvoices - DB2 connection timeout - backoff and succeeds
5. [ ] GetPendingInvoices - DB2 fails all retries - throws exception
6. [ ] GetInvoiceById - valid ID - returns invoice
7. [ ] GetInvoiceById - not found 404 - throws exception
8. [ ] GetInvoicesByDateRange - valid range - returns filtered list
9. [ ] DataSource - first call - opens DB2 connection
10. [ ] DataSource - connection valid - reuses connection
11. [ ] DataSource - connection dropped - reconnects
12. [ ] DataSource - reconnect fails - throws exception

---

#### 2.2: MyInvoiceMapper Implementation

**Sub-tasks:**
- [ ] Implement Transform method
- [ ] Implement field mapping (MOVEX → UBL 2.1)
- [ ] Implement validator coordination
- [ ] Add error collection
- [ ] Write 8 unit tests
- [ ] Code review & approval

**Timeline:**
- Start: Tuesday 10 AM
- Target completion: Wednesday 12 PM
- Effort: 12 hours

**Test Cases (8):**
1. [ ] Transform - valid MOVEX invoice - returns document
2. [ ] Transform - missing mandatory field - returns errors
3. [ ] Transform - invalid TIN - returns validation errors
4. [ ] Transform - invalid date - returns validation errors
5. [ ] Transform - mismatched totals - returns validation errors
6. [ ] Transform - all validators coordinated - runs all checks
7. [ ] ValidateDocument - valid document - returns true
8. [ ] ValidateDocument - invalid document - returns false

---

#### 2.3: Validators Implementation (5 Classes)

**Sub-tasks per validator:**
- [ ] Implement validation logic
- [ ] Handle edge cases
- [ ] Add error messages
- [ ] Write unit tests (3-4 each)
- [ ] Code review & approval

**MandatoryFieldsValidator (4 tests):**
1. [ ] Validate - all fields present - returns no errors
2. [ ] Validate - missing InvoiceNumber - returns error
3. [ ] Validate - missing SupplierTIN - returns error
4. [ ] Validate - empty string field - treats as missing

**TINValidator (4 tests):**
1. [ ] Validate - valid TIN - returns no errors
2. [ ] Validate - invalid format - returns error
3. [ ] Validate - unregistered TIN - returns error
4. [ ] Validate - cached TIN - uses cache

**DateValidator (3 tests):**
1. [ ] Validate - valid dates - returns no errors
2. [ ] Validate - placeholder date - returns error
3. [ ] Validate - date range invalid - returns error

**CurrencyValidator (3 tests):**
1. [ ] Validate - valid currency - returns no errors
2. [ ] Validate - invalid currency code - returns error
3. [ ] Validate - two decimals - accepts format

**TotalsValidator (2 tests):**
1. [ ] Validate - matching totals - returns no errors
2. [ ] Validate - mismatched totals - returns error

**Timeline:**
- Start: Wednesday 10 AM
- Target completion: Thursday 12 PM
- Effort: 12 hours

---

#### 2.4: MyInvoiceSubmitter Implementation

**Sub-tasks:**
- [ ] Set up OAuth for MyInvois API
- [ ] Implement Submit method
- [ ] Add XAdES signing (via SDK)
- [ ] Add error handling (retriable vs non-retriable)
- [ ] Add token refresh on 401
- [ ] Write 5+ unit tests
- [ ] Code review & approval

**Timeline:**
- Start: Wednesday 2 PM
- Target completion: Thursday 2 PM
- Effort: 12 hours

**Test Cases (5+):**
1. [ ] Submit - valid document - returns success
2. [ ] Submit - rate limit 429 - retries and succeeds
3. [ ] Submit - validation error from MyInvois - returns failed
4. [ ] Submit - token expired - refreshes and retries
5. [ ] GetSubmissionStatus - valid UUID - returns status

---

#### 2.5: AuditLogger Implementation

**Sub-tasks:**
- [ ] Set up SQL Server connection
- [ ] Implement LogSubmission method
- [ ] Implement duplicate detection (by UUID)
- [ ] Implement GetFailedSubmissions query
- [ ] Write 5 unit tests
- [ ] Code review & approval

**Timeline:**
- Start: Thursday 2 PM
- Target completion: Friday 10 AM
- Effort: 8 hours

**Test Cases (5):**
1. [ ] LogSubmission - successful submission - logs to database
2. [ ] LogSubmission - failed submission - logs error
3. [ ] IsInvoiceAlreadySubmitted - duplicate UUID - returns true
4. [ ] IsInvoiceAlreadySubmitted - new UUID - returns false
5. [ ] GetFailedSubmissions - multiple failures - returns list

---

#### 2.6: Create Audit Table

**Task:**
- [ ] Execute `src/database/create-audit-table.sql` in test DB
- [ ] Execute `src/database/create-audit-views.sql` in test DB
- [ ] Verify schema created
- [ ] Verify views created

**Timeline:**
- Do: Thursday 2 PM (before testing AuditLogger)
- Effort: 2 hours

---

#### 2.7: Unit Tests Writing

**Coverage Goals:**
- Target: 54+ test cases
- Target coverage: ≥80% of all code
- Target: All tests passing

**By Component:**
- MovexInvoiceReader: 12 tests
- MyInvoiceMapper: 8 tests
- MandatoryFieldsValidator: 4 tests
- TINValidator: 4 tests
- DateValidator: 3 tests
- CurrencyValidator: 3 tests
- TotalsValidator: 2 tests
- MyInvoiceSubmitter: 5+ tests
- AuditLogger: 5 tests
- InvoiceProcessor: 5+ tests

**Total: 54+ tests**

---

#### 2.8: Code Review

**Process:**
1. [ ] Developer pushes code to feature branch
2. [ ] Creates pull request with description
3. [ ] Tech Lead reviews code
4. [ ] Checks against ai/memory/05 standards
5. [ ] Requests changes or approves
6. [ ] Developer merges to main

**Checklist:**
- [ ] No hardcoded credentials
- [ ] Proper error handling
- [ ] Logging at appropriate levels
- [ ] All edge cases tested
- [ ] No unused imports
- [ ] Code follows patterns
- [ ] All tests passing
- [ ] Coverage ≥80%

---

### Known Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| MOVEX database unavailable | Blocked on Tuesday | Have test data ready |
| Test DB schema mismatch | Validators fail | Create schema early (Thu 2 PM) |
| Coverage gaps in Friday | Slip to Week 3 | Daily test runs Thu-Fri |
| Code review delays | Blocked on Friday | Start review Thursday |

---

## Sprint 3: Testing & Integration (Feb 17-21, 2026)

### Work Items Summary

| ID | Task | Status | Assignee | Effort | Priority |
|----|------|--------|----------|--------|----------|
| 3.1 | Integration tests (20+) | ✅ Done | QA | 16h | P0 |
| 3.2 | E2E tests (5 scenarios) | ✅ Done | QA | 12h | P0 |
| 3.3 | Close ADR-013 Gap #2 (invoice line items) | ✅ Done | Developer | 4h | P0 |
| 3.4 | InvoiceProcessor orchestrator | ✅ Done | Developer | 4h | P0 |
| 3.5 | DirectQueryDataSource implementation (Dapper+ODBC) | ✅ Done | Developer | 6h | P0 |
| 3.6 | Close ADR-013 Gap #3 (DB2 driver decision) | ✅ Done | Developer | 1h | P0 |
| 3.7 | Documentation update (Week 3 progress) | ✅ Done | Developer | 2h | P1 |
| 3.8 | Performance tests | ⏳ Planned | Developer | 8h | P0 |
| 3.9 | UAT preparation | ⏳ Planned | QA | 8h | P1 |
| **Total** | | | | **61h** | |

### Week 3 Progress (Feb 17-19)

**Completed (Feb 17-18):**
- InvoiceProcessor orchestrator: full pipeline Reader→Mapper→Validators→Submitter→AuditLogger
- 5 integration tests: OAuth flow, submission, duplicate DS302, invalid signature DS301, rate limit retry
- 5 E2E tests: happy path, mixed batch, duplicate detection, audit trail, empty batch
- ADR-013 Gap #2 closed: RawInvoiceLineRecord DTO, MovexInvoiceReader line mapping, data source SQL docs
- Fixed interface alignment: IMyInvoiceMapper → IMyInvoisMapper
- Fixed submission response JSON format (uuid field)
- Fixed E2E test TINs (all-numeric 12-digit format)
- Fixed MovexInvoiceReader AR→Sales type mapping assertion

**Completed (Feb 19):**
- DirectQueryDataSource fully implemented: Dapper+ODBC, AP/AR header queries (fpledg/fsledg/fgledg), batch line item fetching (OINVOL+MITMAS with IN clause batches of 100), dictionary-based company schema mapping
- ADR-013 Gap #3 closed: DB2 driver decided — System.Data.Odbc 9.0.2 (not Net.IBM.Data.Db2) for AS/400 reliability
- NuGet packages added: Dapper 2.1.35, System.Data.Odbc 9.0.2
- Dev environment isolation: appsettings.Development.json uses CMP300 (testing), production uses CMP100
- Tests updated: removed 3 NotImplementedException stubs, added argument validation theory (3 InlineData cases)
- Documentation updated: PROJECT_STATUS.md v4.1 (90%), decision-log, ADR-013, sprint-backlog, release-notes

**Test Results:** 224 total, 224 passing (100%), 0 failures

---

## Sprint 4: Go-Live (Feb 24-28, 2026)

### Work Items Summary

| ID | Task | Status | Assignee | Effort | Priority |
|----|------|--------|----------|--------|----------|
| 4.1 | Finance UAT execution | ⏳ Planned | QA | 16h | P0 |
| 4.2 | Dry run in sandbox | ⏳ Planned | Developer | 8h | P0 |
| 4.3 | Production deployment | ⏳ Planned | Ops | 12h | P0 |
| 4.4 | Go-live execution | ⏳ Planned | Team | 8h | P0 |
| 4.5 | Post-go-live monitoring | ⏳ Planned | Ops | 16h | P1 |
| **Total** | | | | **60h** | |

---

### Progress Tracking Template

**Update daily:**

```
Date: [Date]
Sprint: [Sprint Number]
Team Member: [Name]

Tasks completed today:
- [Task ID: Task name - completion %]

Tasks in progress:
- [Task ID: Task name - completion %, blockers]

Tasks planned for tomorrow:
- [Task ID: Task name]

Blockers/Issues:
- [Issue - impact - plan to resolve]

Code coverage: [Current %]
Tests passing: [X/54]
Build warnings: [X]

Notes:
- [Any other relevant updates]
```

---

### Burndown Chart (Sprint 2)

Target: Complete 96 hours of work by Friday COB

```
Day 1 (Mon):    96h → 84h (12h done)
Day 2 (Tue):    84h → 72h (12h done)
Day 3 (Wed):    72h → 60h (12h done)
Day 4 (Thu):    60h → 40h (20h done)
Day 5 (Fri):    40h → 0h  (40h done)

Target line:    ↘→↘→↘→↘→↘
```

---

### Success Definition (Sprint 2)

**All of the following must be true:**

- [ ] 54+ unit tests implemented
- [ ] All tests passing (100%)
- [ ] Code coverage ≥80%
- [ ] Zero critical defects
- [ ] Code review approved
- [ ] No build warnings
- [ ] All 5 services implemented
- [ ] All 5 validators implemented
- [ ] Ready for Week 3 integration testing

---

**Owner:** Developer, QA, Ops
**Status:** Phase 1 Complete | Phase 2 Sprint 6 Complete ✅ | Sprint 7 Next (Mar 17-21)
**Last Updated:** March 9, 2026

---

---

# Phase 2: Audit Storage Migration (SQLite)

**Initiative:** MVAI-P2
**Epic:** Audit Storage Migration — SQLite
**Linked Plan:** `C:\Users\hsalazar\.claude\plans\harmonic-napping-hollerith.md` (Story 2)
**Cross-project:** SM-Portal runs parallel Sprints 3–5 (see `c:\Projects\SM-Portal\ai\tasks\sprint-backlog.md`)

---

## Sprint 5: ADR & Architecture Review (Mar 4-7, 2026)

### Work Items Summary

| ID | Task | Story | Status | Assignee | Effort | Priority |
|----|------|-------|--------|----------|--------|----------|
| 5.1 | Create `ai/evidence/decision-001-sqlite-audit-storage.md` | Story 1 | ✅ Done | architect-system-design | 3h | P0 |
| 5.2 | Update `ai/memory/08-governance-and-decisions.md` | Story 1 | ✅ Done | architect-system-design | 1h | P0 |
| 5.3 | Update `ai/memory/09-implementation-decisions.md` (ADR-014) | Story 1 | ✅ Done | developer-dotnet | 1h | P0 |
| 5.4 | Update `ai/patterns/audit-logging.md` (SQLite/EF Core pattern) | Story 1 | ✅ Done | developer-dotnet | 1h | P0 |
| 5.5 | Architecture Review sign-off → `ai/evidence/decision-log.md` | Story 1 | ✅ Done | architect-system-design | 2h | P0 |
| 5.6 | Update `WORKSPACE_RULES.md` — SQLite approved for IIS deployments | Story 1 | ✅ Done | architect-system-design | 1h | P0 |
| **Total** | | | | | **8h** | |

### Task Detail: 5.1 — Create ADR decision-001-sqlite-audit-storage.md

**File:** `c:\Projects\MyInvois-Service\ai\evidence\decision-001-sqlite-audit-storage.md`

**Content required:**
- **Status:** Proposed → Accepted (after Architecture Review)
- **Context:** Users requested removal of SQL Server dependency for audit logs; service runs on IIS, <500 invoices/day
- **Decision:** Replace `System.Data.SqlClient` + ADO.NET with `Microsoft.Data.Sqlite` + EF Core 8 (Code-First)
- **Options considered:** SQL Server LocalDB (rejected: still SQL Server runtime), SQL Server Express (rejected: same), LiteDB (rejected: no EF Core provider), JSONL (rejected: not queryable)
- **Consequences:** No TDE (mitigated by BitLocker), WAL mode required, NTFS ACL on `./data/audit.db`
- **Compliance:** ISO 27001 7-year retention maintained; OS-level encryption per BitLocker
- **Skills:** `architecture/audit-logging-framework v1.0+`, `architecture/configuration-management v1.0+`

**Acceptance Criteria:**
- [ ] ADR follows project decision record format (see `ai/evidence/decision-002-separate-portal-api.md` in SM-Portal for reference)
- [ ] Options considered section documents all 4 alternatives
- [ ] Compliance section addresses 7-year retention plan for SQLite file growth
- [ ] Architecture Review sign-off noted in `ai/evidence/decision-log.md`

---

### Task Detail: 5.3 — Update ai/memory/09-implementation-decisions.md

**Add ADR-014 entry:**
- Title: SQLite Audit Storage via EF Core
- Decision date: March 4, 2026
- Supersedes: ADR-002 (SQL Server Audit)
- Decision: Use `Microsoft.Data.Sqlite` + `Microsoft.EntityFrameworkCore.Sqlite 8.0.*`
- Pattern: Code-First, `EnsureCreated()` on startup, WAL mode
- File path: `Data Source=./data/audit.db` (relative to `AppContext.BaseDirectory`)
- Type mappings: GUID → TEXT, DATETIME2 → TEXT (ISO 8601)
- Test pattern: in-memory SQLite (`Data Source=:memory:`) replaces `Mock<IDbConnection>`

---

### Known Risks Sprint 5

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Architecture Review not completed by Fri Mar 7 | Sprint 6 start delayed | Schedule review meeting Thu Mar 6 |
| ADR format disagreement | Rework | Reference SM-Portal decision-002 format upfront |

---

## Sprint 6: SQLite Implementation (Mar 10-14, 2026) ✅ COMPLETE

### Work Items Summary

| ID | Task | Story | Status | Assignee | Effort | Priority |
|----|------|-------|--------|----------|--------|----------|
| 6.1 | NuGet: remove `System.Data.SqlClient`, add Sqlite + EF Core packages | Story 2 | ✅ Done | developer-dotnet | 1h | P0 |
| 6.2 | Create `src/Data/AuditLogEntity.cs` (EF Core entity, 30 columns) | Story 2 | ✅ Done | developer-dotnet | 2h | P0 |
| 6.3 | Create `src/Data/AuditDbContext.cs` + `AuditDbContextFactory.cs` | Story 2 | ✅ Done | developer-dotnet | 2h | P0 |
| 6.3b | EF Code-First via `EnsureCreated()` — no explicit migration needed | Story 2 | ✅ Done | developer-dotnet | 0h | P0 |
| 6.4 | Rewrite `src/Services/AuditLogger.cs` with EF Core (keep `IAuditLogger` unchanged) | Story 2 | ✅ Done | developer-dotnet | 4h | P0 |
| 6.5 | Update `src/DataAccess/ServiceCollectionExtensions.cs` (AddAuditLogging extension) | Story 2 | ✅ Done | developer-dotnet | 1h | P0 |
| 6.6 | Update `appsettings.Development.json` (SQLite connection string) | Story 2 | ✅ Done | developer-dotnet | 0.5h | P0 |
| 6.6b | SQLite DDL reference script — deferred to Sprint 7 (lower priority) | Story 2 | ⏳ Deferred | developer-dotnet | — | P1 |
| 6.7 | Rewrite 3 test files with named in-memory SQLite (unit + integration + E2E) | Story 2 | ✅ Done | developer-dotnet | 5h | P0 |
| 6.8 | Full build + test run (233/233 passing, 0 errors) + code review | Story 2 | ✅ Done | Tech Lead | 2h | P0 |
| **Total** | | | | | **17.5h** | |

### Task Detail: 6.4 — Rewrite AuditLogger.cs

**File:** `c:\Projects\MyInvois-Service\src\MyInvois.Service\Services\AuditLogger.cs`

**Key changes:**
- Constructor: `IDbContextFactory<AuditDbContext>` replaces `IDbConnection`
- `LogSubmission`: `using var ctx = _factory.CreateDbContext(); ctx.AuditLogs.Add(entity); await ctx.SaveChangesAsync()`
- `IsInvoiceAlreadySubmitted`: `ctx.AuditLogs.AnyAsync(x => x.InvoiceNumber == invoiceNumber && x.Status == "Success")`
- `GetFailedSubmissions`: `ctx.AuditLogs.Where(x => x.Status == "Failed" && x.Category == "MyInvois").OrderByDescending(x => x.SubmittedAt).Take(maxResults).ToListAsync()`
- Interface `IAuditLogger` **must not change** (lines 21–37 of current file)

**Definition of Done:**
- [x] `IAuditLogger` interface unchanged (same 3 method signatures, same parameter names)
- [x] `IDbConnection` completely removed from constructor and usages
- [x] All 3 public methods async-native (no `Task.CompletedTask` workarounds)
- [x] Private ADO.NET helpers removed
- [x] Summary comment updated to reference SQLite / EF Core 8 / ADR-014

### Task Detail: 6.7 — Update Integration Tests

**File:** `c:\Projects\MyInvois-Service\tests\MyInvois.Service.Tests\Integration\AuditLoggerIntegrationTests.cs`

**Changes:**
- Replace `Mock<IDbConnection>` setup with: `new SqliteConnection("Data Source=:memory:")`
- Create `AuditDbContext` with in-memory SQLite and call `EnsureCreated()` in test setup
- Test cases remain the same (LogSubmission, IsInvoiceAlreadySubmitted, GetFailedSubmissions)
- Add assertion: `PRAGMA journal_mode` — not applicable for in-memory, note in comment

### Burndown (Sprint 6)

```
Day 1 (Mon): 25h → 19h  (6.1, 6.2 tasks done)
Day 2 (Tue): 19h → 12h  (6.3, 6.3b, 6.4 in progress)
Day 3 (Wed): 12h →  7h  (6.4 complete, 6.5, 6.6 done)
Day 4 (Thu):  7h →  3h  (6.7 tests done)
Day 5 (Fri):  3h →  0h  (6.8 review + merge)
```

### Known Risks Sprint 6 — Resolved

| Risk | Resolution |
|------|-----------|
| EF Core migration generates incorrect SQLite types | Used `EnsureCreated()` — no migration generated; schema created directly from entity |
| Partial index syntax not supported by EF Core SQLite | Confirmed working: `HasFilter("\"Status\" != 'Success'")` |
| `IDbContextFactory` vs `IDbContext` thread safety | Factory creates one context per operation — `using` disposal correct; tests use named in-memory SQLite + `_keepAlive` |
| In-memory SQLite destroyed by context disposal | Fixed: named shared `Mode=Memory;Cache=Shared` + `_keepAlive` SqliteConnection pattern |

### Sprint 6 Completion Notes (Mar 9, 2026)

- **Tests:** 233 total (was 225 before rewrite — 8 new tests added during SQLite migration)
- **No interface change:** `IAuditLogger` three-method contract untouched
- **Pattern established:** Named in-memory SQLite for `IDbContextFactory`-based tests — documented in `ai/patterns/audit-logging.md`
- **Deferred:** `create-audit-table-sqlite.sql` DDL reference script → Sprint 7 (no functional impact)

---

## Sprint 7: Compliance, Backup & Handoff (Mar 17-21, 2026)

### Work Items Summary

| ID | Task | Story | Status | Assignee | Effort | Priority |
|----|------|-------|--------|----------|--------|----------|
| 7.1 | Update `docs/DEPLOYMENT.md` — BitLocker requirement + NTFS ACL setup | Story 4 | ⏳ Ready | expert-myinvois-compliance | 2h | P0 |
| 7.2 | Write backup runbook — robocopy procedure + restore steps | Story 4 | ⏳ Ready | Ops Lead | 2h | P0 |
| 7.3 | Security review coordination with `validator-quality` | Story 4 | ⏳ Ready | validator-quality | 4h | P0 |
| 7.4 | Update `c:\Projects\.github\WORKSPACE_RULES.md` (SQLite approved for IIS) | Story 4 | ⏳ Ready | architect-system-design | 1h | P1 |
| 7.5 | Smoke test: deploy to test IIS, submit 5 invoices, verify `audit.db` rows | Story 4 | ⏳ Ready | developer-dotnet | 2h | P1 |
| **Total** | | | | | **11h** | |

### Task Detail: 7.3 — Security Review Checklist

**Reviewer:** `validator-quality`
**Trigger:** Auth/authz-adjacent change (audit log storage medium change), sensitive data handling

| Check | Description | Pass/Fail |
|-------|-------------|-----------|
| NTFS ACL | `./data/audit.db` readable only by App Pool identity | [ ] |
| WAL journal | `./data/audit.db-wal` also NTFS-restricted | [ ] |
| Connection string | Not in `appsettings.json` (user-secrets / env var) | [ ] |
| SQL injection | EF Core parameterizes all queries — no raw SQL with user input | [ ] |
| Secrets in logs | Connection string not logged at startup | [ ] |
| Backup file ACL | Backup share restricted (not world-readable) | [ ] |

**Record outcome in:** `ai/evidence/decision-log.md`

