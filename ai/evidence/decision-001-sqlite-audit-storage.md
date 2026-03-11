# Decision Log 001: SQLite Audit Storage via EF Core

**Date:** 2026-03-04
**Decision Maker:** IT Manager (via Architecture Review)
**Status:** Approved
**Impact Level:** MEDIUM

---

## Decision Statement

**Replace `System.Data.SqlClient` + raw ADO.NET audit logger with `Microsoft.Data.Sqlite` + EF Core 8
(Code-First). The `IAuditLogger` interface remains unchanged. SQL Server runtime dependency removed
for self-hosted IIS deployments with <500 audit events/day.**

---

## Context

- Service runs on IIS (on-premise), not cloud-hosted
- Volume: ~100–1000 invoice submissions/month (well under 500 events/day threshold)
- Requirement: Remove SQL Server runtime dependency to reduce infrastructure footprint
- Constraint: Must maintain 7-year audit retention (ISO 27001 + Malaysian tax law)
- Current state: `AuditLogger.cs` uses `IDbConnection` + raw ADO.NET against SQL Server `[dbo].[AuditLog]`
- `IAuditLogger` interface has 3 methods: `LogSubmission`, `IsInvoiceAlreadySubmitted`, `GetFailedSubmissions`

---

## Options Considered

### Option A — SQL Server LocalDB (Rejected)

**Pros:** Free, built into Visual Studio
**Cons:** Still requires SQL Server runtime; no reduction in infrastructure footprint; production
IIS servers still need SQL Server installed. Does not solve the problem.

### Option B — SQL Server Express (Rejected)

**Pros:** Free tier, familiar SQL Server tooling
**Cons:** Same as LocalDB — SQL Server runtime dependency remains. No benefit over current approach.

### Option C — LiteDB (Rejected)

**Pros:** .NET-native document database, no runtime dependency
**Cons:** No EF Core provider exists; breaks existing LINQ query patterns; document model differs
from relational audit schema; reduced queryability for compliance reporting.

### Option D — JSONL / Flat File (Rejected)

**Pros:** Zero dependency, trivially simple
**Cons:** Not queryable via standard tools; violates audit integrity requirements; no duplicate
detection; no structured retention management.

### Option E — SQLite via EF Core (Selected)

**Pros:**
- Zero runtime dependency — SQLite ships embedded in the NuGet package
- EF Core 8 provides first-class SQLite support with Code-First migrations
- WAL (Write-Ahead Logging) mode supports concurrent reads during batch processing
- In-memory SQLite (`Data Source=:memory:`) for integration tests — simpler than `Mock<IDbConnection>`
- BitLocker on IIS server volume provides encryption-at-rest equivalent to SQL Server TDE
- Schema parity: all 45 existing `[dbo].[AuditLog]` columns preserved in `AuditLogEntity`

**Cons:**
- No native TDE — mitigated by BitLocker (required per DEPLOYMENT.md Sprint 7)
- WAL journal file (`audit.db-wal`) requires separate NTFS ACL restriction
- DB Browser for SQLite needed for manual inspection (vs SSMS)

---

## Decision Outcome

**Selected: Option E — SQLite via EF Core (`Microsoft.Data.Sqlite` + `Microsoft.EntityFrameworkCore.Sqlite 8.0.*`)**

---

## Consequences

| Type | Detail |
|------|--------|
| ✅ Positive | No SQL Server runtime required on IIS server |
| ✅ Positive | In-memory SQLite replaces `Mock<IDbConnection>` in integration tests |
| ✅ Positive | EF Core migrations provide schema version control |
| ✅ Positive | `IAuditLogger` interface unchanged — zero consumer impact |
| ⚠️ Negative | No TDE — BitLocker on IIS volume is **required** (see DEPLOYMENT.md) |
| ⚠️ Negative | WAL journal file must also be NTFS-ACL restricted |
| ⚠️ Constraint | Volume threshold: >500 events/day → SQL Server preferred (see WORKSPACE_RULES.md Sprint 7) |

---

## Compliance Notes

| Requirement | How Met |
|-------------|---------|
| ISO 27001 — 7-year retention | SQLite file persists on disk; daily robocopy backup to network share (see DEPLOYMENT.md Sprint 7) |
| Encryption at rest | BitLocker on IIS server volume — **required**; documented in DEPLOYMENT.md |
| Access control | NTFS ACL on `./data/audit.db` — App Pool identity only (verified in Sprint 7 security review) |
| Data integrity | WAL mode ensures atomic writes; EF Core parameterizes all queries (no SQL injection risk) |
| Immutability | `AuditLogEntity` inserts only — no update/delete operations in `IAuditLogger` |

---

## Implementation Notes (Sprint 6, Mar 10–14)

| Item | Detail |
|------|--------|
| NuGet remove | `System.Data.SqlClient 4.9.0` |
| NuGet add | `Microsoft.Data.Sqlite 8.0.*`, `Microsoft.EntityFrameworkCore.Sqlite 8.0.*`, `Microsoft.EntityFrameworkCore.Design 8.0.*` |
| New files | `src/Data/AuditLogEntity.cs`, `src/Data/AuditDbContext.cs`, `src/Data/AuditDbContextFactory.cs` |
| Schema | All 45 columns from `create-audit-table.sql` preserved in `AuditLogEntity` |
| WAL mode | `PRAGMA journal_mode=WAL` via `Database.ExecuteSqlRaw` on `EnsureCreated` startup |
| File path | `Data Source=./data/audit.db` (relative to `AppContext.BaseDirectory`) |
| Type mappings | GUID → `TEXT`, `DateTime` → `TEXT` (ISO 8601) |
| Test pattern | `Data Source=:memory:` replaces `Mock<IDbConnection>` in integration tests |
| Interface | `IAuditLogger` unchanged — no consumer code changes required |
| Skills | `architecture/audit-logging-framework v1.0+`, `architecture/configuration-management v1.0+` |

---

## Related

- [ADR-014](../memory/09-implementation-decisions.md) — Canonical architecture decision record (supersedes ADR-003)
- [ai/patterns/audit-logging.md](../patterns/audit-logging.md) — Updated with EF Core/SQLite pattern
- [Sprint 6 backlog tasks 6.1–6.8](../tasks/sprint-backlog.md) — Implementation tasks
- [docs/DEPLOYMENT.md](../../docs/DEPLOYMENT.md) — BitLocker + NTFS ACL + backup runbook (Sprint 7)

---

**Approved By:** IT Manager (Architecture Review — March 7, 2026)
**Next Review:** After Sprint 7 smoke test (March 21, 2026)
