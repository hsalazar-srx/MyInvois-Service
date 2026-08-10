# MyInvois-Service — Architecture Decision Records (ADRs)

**Version:** 1.0  
**Date:** February 5, 2026  
**Status:** Active

---

## ADR-001: Standalone Service vs Portal Integration

**Date:** February 5, 2026  
**Status:** Accepted

### Context
We needed to decide whether MyInvois submission logic should be built into MOVEX-Portal or as a separate service.

### Decision
**Build as standalone .NET 8.0 Worker Service, expose via HTTP API for portal integration.**

### Rationale
- **Separation of Concerns**: Portal is for user-initiated queries; MyInvois needs scheduled batch processing
- **Independent Scalability**: Service can run independently on task scheduler, future migration to Azure Functions
- **Code Reusability**: Can be called by batch jobs, webhooks, or portal without modification
- **Deployment Flexibility**: Portal updates don't affect service; service can be patched without portal restart

### Alternatives Considered
- ❌ **Integrate into Portal**: Violates single responsibility; couples unrelated concerns
- ❌ **Standalone only (no Portal)**: Users have no visibility; no retry capability

### Consequences
- ✅ Cleaner architecture, easier to test and maintain
- ✅ Can migrate to cloud (Azure Functions) independently
- ⚠️ Requires HTTP client integration in portal (Phase 2)
- ⚠️ Two deployment artifacts instead of one

---

## ADR-002: Monthly Batch Processing (Not Daily)

**Date:** February 5, 2026  
**Status:** Accepted

### Context
MyInvois does not mandate real-time submission; monthly submission is acceptable. Current volume is ~100 sales + 500-1000 purchase invoices/month.

### Decision
**Implement monthly batch submission (1st of month) for MVAI; enable daily in future if required.**

### Rationale
- **Volume alignment**: Current ~600-1100 invoices/month fits one monthly batch
- **Rate limiting**: 100 req/min limit easily handled with batch size 50-100
- **Operational simplicity**: One job/month vs daily scheduling
- **Cost-efficient**: Fewer service starts, lower infrastructure load

### Alternatives Considered
- ❌ **Daily submission**: Unnecessary complexity for current volume
- ⚠️ **Real-time**: MyInvois doesn't require it; can add later if regulations change

### Consequences
- ✅ Simpler operational model
- ✅ Easier to validate and test (one monthly submission)
- ⚠️ If regulations change to require daily, need to refactor
- ⚠️ Month-end rush if volume spikes

---

## ADR-003: SQL Server for Audit Logs (Workspace Standard)

**Date:** February 5, 2026  
**Status:** Accepted (Workspace-Wide)

### Context
Multiple projects in workspace need audit logging. Options: Db2, SQL Server, or mixed.

### Decision
**All projects use SQL Server for audit logs. Db2 retains operational data only.**

### Rationale
- **Centralized Reporting**: Single database for compliance/analytics
- **Better Query Performance**: SQL Server superior for analytical queries
- **Tool Ecosystem**: Power BI, SSMS, Azure Data Studio
- **Future Cloud**: Azure SQL Database path clear
- **Standards Compliance**: ISO 27001 retention policies easier to implement

### Alternatives Considered
- ❌ **Db2 for everything**: Doesn't align with company infrastructure strategy
- ❌ **Mixed (both Db2 & SQL Server)**: Complicates compliance, splitting responsibility

### Consequences
- ✅ Unified compliance/audit
- ✅ Better scalability for analytics
- ⚠️ Requires SQL Server infrastructure (already in place)
- ⚠️ Legacy projects with Db2 grandfathered (no migration)

---

## ADR-004: Batch Size: Sales 100, Purchase 50

**Date:** February 5, 2026  
**Status:** Accepted

### Context
Rate limit: 100 requests/minute = 1 req/0.6 sec. Need to choose batch sizes for sales and purchase invoices.

### Decision
- **Sales**: 1 batch of 100 (monthly)
- **Purchase**: 10-20 batches of 50 (monthly), 0.6s delay between batches

### Rationale
- **Sales (~100/month)**: Single batch under limit, simple processing
- **Purchase (~500-1000/month)**: Batches of 50 = safe margin (10 batches @ 100 RPM uses <10% capacity)
- **0.6s delay**: Safety margin (1 request per 0.6 sec = 100 RPM exactly)
- **Future flexibility**: Can increase batch size if rates improve

### Alternatives Considered
- ❌ **Single large batch of 1000**: Risk of hitting rate limit if MyInvois slower than expected
- ❌ **Batch size 1 (iterate)**: Too many API calls, high latency

### Consequences
- ✅ Safe rate limit utilization (~20%)
- ✅ Predictable performance
- ⚠️ Batch processing takes ~8 minutes for 1000 invoices (acceptable)

---

## ADR-005: XAdES Signature Using MyInvois SDK

**Date:** February 5, 2026  
**Status:** Accepted

### Context
MyInvois requires XAdES v1.1 digital signatures. Options: use SDK, custom implementation, or 3rd-party library.

### Decision
**Use official MyInvois SDK for signature generation. No custom cryptography.**

### Rationale
- **Compliance**: SDK enforces MyInvois-specific rules (no custom mistakes)
- **Maintenance**: Updates from MyInvois don't require code changes
- **Support**: MyInvois provides official support for SDK issues
- **Security**: Professional cryptography implementation

### Alternatives Considered
- ❌ **Custom XAdES**: High risk, regulatory non-compliance
- ⚠️ **3rd-party library**: Support dependency, may not be MyInvois-compatible

### Consequences
- ✅ Guaranteed compliance with MyInvois schema
- ✅ Simplifies implementation (no cryptography knowledge needed)
- ⚠️ Dependency on SDK updates and compatibility

---

## ADR-006: Retry Strategy: Limited Retries, Manual Override

**Date:** February 5, 2026  
**Status:** Accepted (Phase 1)

### Context
Some submissions fail (network, API errors). Need retry strategy that balances reliability with operational control.

### Decision
**Phase 1 (MVAI): Limited retries (3 attempts) + manual retry via audit log queries. Phase 2: Automatic circuit breaker with Polly.**

### Rationale
- **Phase 1 conservative**: 3 retries covers transient failures without automating away problems
- **Audit trail**: Manual review required for persistent failures
- **Operational control**: Finance team has visibility before retry
- **Phase 2 upgrade**: Polly adds resilience without manual intervention

### Alternatives Considered
- ❌ **No retry**: Too risky (network glitches cause failures)
- ❌ **Infinite retry**: Can mask real issues, burn resources

### Consequences
- ✅ Prevents failure cascades in Phase 1
- ✅ Audit trail of all attempts
- ⚠️ Requires manual intervention for persistent failures
- ⏳ Phase 2 will automate retry logic

---

## ADR-007: Token Caching (1 Hour TTL)

**Date:** February 5, 2026  
**Status:** Accepted

### Context
MyInvois OAuth tokens expire; frequent refresh increases latency and API calls.

### Decision
**Cache OAuth tokens with 1-hour TTL. Refresh on 401 response.**

### Rationale
- **Performance**: Reduce token requests from potentially 1000/month to ~30 (1 per day)
- **Rate limits**: Stay well within API rate limits
- **Simplicity**: In-memory cache sufficient for single-instance service
- **Recovery**: Auto-refresh on 401 handles expiry edge cases

### Alternatives Considered
- ❌ **No cache**: 1000 token requests for 1000 invoices (excessive)
- ❌ **Redis cache**: Overkill for Phase 1, can add in Phase 2

### Consequences
- ✅ 95%+ cache hit rate expected
- ✅ Latency reduction ~100ms per request
- ⚠️ Stale token in edge case (handled by 401 refresh)

---

## ADR-008: Error Classification (No-Retry vs Retriable)

**Date:** February 5, 2026  
**Status:** Accepted

### Context
Different errors need different handling. Some are permanent (invalid TIN), others transient (network timeout).

### Decision
**Classify errors explicitly. No-retry errors (400, DS302) → manual review. Retriable errors (429, 500) → automatic retry.**

### Rationale
- **Prevents wasted retries**: Don't retry invalid data (wastes API quota)
- **Operational efficiency**: Humans only review actual problems
- **Clear logging**: Error code + category in audit trail

### Alternatives Considered
- ❌ **Retry all**: Wastes resources on permanent failures
- ❌ **Manual retry only**: Misses transient failures

### Consequences
- ✅ Cleaner error handling
- ✅ Better visibility into problem types
- ⚠️ Requires maintenance of error classification rules

---

## ADR-009: Validators as Separate Classes (Not Inline)

**Date:** February 5, 2026  
**Status:** Accepted

### Context
MyInvois has 5+ validation rules (mandatory fields, TIN, date, currency, totals). How to organize?

### Decision
**Create dedicated validator classes (MandatoryFieldsValidator, TINValidator, DateValidator, CurrencyValidator, TotalsValidator).**

### Rationale
- **Single Responsibility**: Each validator has one job
- **Testability**: Easy to unit test each validator independently
- **Reusability**: Validators can be reused by portal/other services
- **Maintainability**: Adding new rule doesn't modify mapper code
- **Traceability**: Maps directly to MyInvois constraint document

### Alternatives Considered
- ❌ **Inline in Mapper**: Mixing concerns, hard to test
- ❌ **Single monolithic validator**: Hard to maintain, unclear what each rule checks

### Consequences
- ✅ Better code organization
- ✅ 80%+ validator test coverage achievable
- ⚠️ Slightly more boilerplate code (worth it)

---

## ADR-010: MyInvois UUID as Primary Tracking ID

**Date:** February 5, 2026  
**Status:** Accepted

### Context
How to track submissions across MyInvois, MOVEX, and our audit log?

### Decision
**Use MyInvois UUID as primary tracking ID. Store in audit log. Reference in all queries.**

### Rationale
- **Single source of truth**: MyInvois UUID is official unique identifier
- **Easy reconciliation**: Match our audit log to MyInvois records
- **Future reporting**: Build dashboards keyed on UUID
- **Buyer/Seller reference**: UUID in all correspondence with MyInvois

### Alternatives Considered
- ❌ **MOVEX invoice number**: Not unique across systems
- ❌ **Our submission ID**: Doesn't help with MyInvois API calls

### Consequences
- ✅ Clear audit trail linking to MyInvois
- ✅ Easy buyer/seller communication
- ⚠️ UUID returned only on successful submission (failed submissions have no UUID)

---

## ADR-011: Audit Log Retention: 7 Years

**Date:** February 5, 2026  
**Status:** Accepted

### Context
How long to retain audit logs? ISO 27001, Malaysian tax law, operational requirements?

### Decision
**Retain audit logs minimum 7 years (extended from typical 2-3 years).**

### Rationale
- **ISO 27001**: Audit retention for compliance
- **Malaysian Tax**: Invoices valid for 7 years (LHDNM requirement)
- **Business Continuity**: Disputes, audits may arise years later
- **Regulatory**: Proactive stance on compliance

### Alternatives Considered
- ❌ **2 years**: Misses tax/audit requirements
- ❌ **Indefinite**: Expensive storage

### Consequences
- ✅ Compliant with regulations
- ⚠️ Storage cost increases (archive to cold storage Phase 2)

---

## ADR-012: Phase 1 Focus: Core Submission Only

**Date:** February 5, 2026  
**Status:** Accepted

### Context
MVP scope: what's in Phase 1 vs Phase 2?

### Decision
**Phase 1 (MVAI): Submission + validation + audit logging only. Phase 2: Retry UI, status polling, resilience.**

### Rationale
- **MVAI deadline**: 4 weeks = core features only
- **Complexity reduction**: Don't overcomplicate MVP
- **Feedback loop**: Get live in Phase 1, enhance in Phase 2
- **Risk mitigation**: Phase 2 features can be deferred without blocking go-live

### Alternatives Considered
- ❌ **Pack everything into Phase 1**: Miss deadline, risk quality
- ❌ **Defer submission to Phase 2**: Delays MVAI go-live

### Consequences
- ✅ Achievable timeline
- ✅ Core functionality in production by Feb 28
- ⚠️ Manual workarounds for retry until Phase 2
- ⏳ Portal UI dashboard deferred

---

## ADR-013: Replace MOVEX REST API with DB2 Direct Access

**Date:** February 16, 2026
**Status:** Accepted

### Context
The original design (ADRs 001-012) assumed invoice data would be fetched via the MOVEX REST API (M3 MI protocol, HTTP endpoints OIS100MI/OIS350MI). This required the `m3-transaction-builder` and `m3-response-parser` skills. The decision has been made to source invoice data directly from the MOVEX database on IBM DB2/AS400 instead, using either direct SQL queries or stored procedures.

### Decision
**Replace MOVEX REST API data source with direct DB2 database access. Use strategy pattern to support both direct SQL queries and stored procedures, selectable via configuration.**

### Rationale
- **Eliminates API dependency**: No need for MOVEX REST API service to be running
- **Direct data access**: Query MOVEX ledger tables (fpledg, fsledg, fgledg) directly
- **Flexibility**: Strategy pattern allows switching between direct queries and stored procedures without code changes
- **Future-proof**: Stored procedures can be developed independently by DBA team
- **Simplifies architecture**: One fewer network hop, no HTTP client management

### Skills Impact
- `integration/m3-transaction-builder` — **No longer primary** for data source layer (M3 MI protocol specific)
- `integration/m3-response-parser` — **No longer primary** for data source layer (MI response parsing specific)
- NEW: `integration/movex-db2-data-source` — Created for DB2 direct data access

### Alternatives Considered
- ❌ **Keep REST API**: Adds unnecessary HTTP dependency when direct DB access is available
- ❌ **DB2 only (no strategy pattern)**: Locks into one approach, harder to switch later
- ✅ **Strategy pattern (chosen)**: Supports both approaches, config-driven selection

### Consequences
- ✅ Simpler architecture (no HTTP client, no REST API dependency)
- ✅ Flexible data source (switch between query and stored proc via config)
- ✅ New centralized skill created (`movex-db2-data-source`)
- ⚠️ Party data (TIN, BRN, Name, Address) source still undecided — pluggable `IPartyDataProvider` interface designed as gap
- ✅ DB2 driver resolved (Week 3): System.Data.Odbc chosen for AS/400 reliability
- ✅ Invoice line items resolved (Week 3): OINVOL + MITMAS tables identified, `RawInvoiceLineRecord` DTO created, `MovexInvoiceReader` maps lines
- ✅ DirectQueryDataSource implemented (Week 3): Full Dapper+ODBC, AP/AR queries, batch line items, company isolation via ActiveCompanyCodes

### Known Gaps
1. **Party data enrichment**: Supplier/customer TIN, BRN, Name, Address source undecided. `PlaceholderPartyDataProvider` unblocks development.
2. ~~**Invoice line items**: MOVEX table for line details not identified.~~ **CLOSED (Feb 18, 2026)**: OINVOL (invoice lines) + MITMAS (item master for classification codes) identified. `RawInvoiceLineRecord` DTO created, `MovexInvoiceReader` maps raw lines to `InvoiceLine`, SQL patterns documented in `DirectQueryDataSource` and `StoredProcedureDataSource` stubs. E2E pipeline validated with 224 tests passing.
3. ~~**DB2 driver compatibility**: `Net.IBM.Data.Db2` on .NET 8 with AS/400 may need testing; `System.Data.Odbc` as fallback.~~ **CLOSED (Feb 19, 2026)**: `System.Data.Odbc` (v9.0.2) chosen as primary driver. More reliable for IBM i/AS400 systems than Net.IBM.Data.Db2. DirectQueryDataSource fully implemented with Dapper+ODBC. Company schema mapping uses dictionary lookup (extensible). Environment isolation via `ActiveCompanyCodes`: production queries CMP100 only, development queries CMP300 only.

---

## ADR-014: SQLite Audit Storage via EF Core

**Date:** March 4, 2026
**Status:** Accepted
**Supersedes:** ADR-003 (SQL Server for Audit Logs) — for MyInvois-Service only

### Context

Post-go-live (Phase 2), the decision was made to remove the SQL Server runtime dependency from
the MyInvois-Service audit logger. The service runs on IIS (on-premise), processes <500 invoice
audit events/day, and SQL Server infrastructure adds operational overhead for this volume.

### Decision

**Replace `System.Data.SqlClient` + raw ADO.NET with `Microsoft.Data.Sqlite` + EF Core 8
(Code-First). The `IAuditLogger` interface remains unchanged.**

### Rationale

- Zero runtime dependency: SQLite ships embedded in the NuGet package, no server install required
- EF Core 8 provides first-class SQLite support with Code-First migrations
- WAL mode supports concurrent reads during batch processing
- In-memory SQLite (`Data Source=:memory:`) for integration tests — simpler than `Mock<IDbConnection>`
- BitLocker on IIS server volume provides encryption-at-rest equivalent to SQL Server TDE

### Alternatives Considered

- ❌ **SQL Server LocalDB/Express**: Still requires SQL Server runtime; no infrastructure reduction
- ❌ **LiteDB**: No EF Core provider; breaks LINQ query patterns
- ❌ **JSONL flat file**: Not queryable; violates audit integrity requirements

### Implementation Details

- NuGet: `Microsoft.Data.Sqlite 8.0.*`, `Microsoft.EntityFrameworkCore.Sqlite 8.0.*`
- New files: `src/Data/AuditLogEntity.cs`, `src/Data/AuditDbContext.cs`, `src/Data/AuditDbContextFactory.cs`
- WAL mode: `PRAGMA journal_mode=WAL` via `Database.ExecuteSqlRaw` on `EnsureCreated` startup
- File path convention: `Data Source=./data/audit.db` (relative to `AppContext.BaseDirectory`)
- Type mappings: GUID → `TEXT`, `DateTime` → `TEXT` (ISO 8601)
- Test pattern: `Data Source=:memory:` replaces `Mock<IDbConnection>` in integration tests

### Consequences

- ✅ SQL Server runtime dependency removed
- ✅ `IAuditLogger` interface unchanged (zero consumer impact)
- ✅ EF Core migrations provide schema version control
- ⚠️ No TDE — BitLocker required on IIS server volume (see `docs/DEPLOYMENT.md`)
- ⚠️ NTFS ACL on `./data/audit.db` required — App Pool identity only
- ⚠️ Volume threshold: >500 events/day → SQL Server preferred (see `WORKSPACE_RULES.md`)

---

## Future ADRs (Placeholder)

### ADR-015: Cloud Migration Strategy (Phase 3)
- Evaluate Azure Functions vs AWS Lambda vs Kubernetes
- Plan data migration to Azure SQL Database
- Design for scale (monthly → daily submission)

### ADR-016: Real-Time Submission (If Required)
- Switch from monthly batch to daily/hourly
- Event-driven architecture (invoice created → immediately submit)
- Webhook integration with MOVEX

### ADR-017: Status Polling & Reconciliation
- Implement GET /documents/{uuid}/details polling
- Match MyInvois status to MOVEX invoice lifecycle
- Handle rejections, cancellations, amendments

---

## Decision Log

| ADR | Title | Date | Status |
|-----|-------|------|--------|
| 001 | Standalone Service vs Portal | 2026-02-05 | ✅ Accepted |
| 002 | Monthly Batch Processing | 2026-02-05 | ✅ Accepted |
| 003 | SQL Server for Audit Logs | 2026-02-05 | ✅ Accepted (superseded by ADR-014) |
| 004 | Batch Sizes (100 sales, 50 purchase) | 2026-02-05 | ✅ Accepted |
| 005 | XAdES via SDK | 2026-02-05 | ✅ Accepted |
| 006 | Retry Strategy (Limited + Manual) | 2026-02-05 | ✅ Accepted |
| 007 | Token Caching (1h TTL) | 2026-02-05 | ✅ Accepted |
| 008 | Error Classification | 2026-02-05 | ✅ Accepted |
| 009 | Separate Validators | 2026-02-05 | ✅ Accepted |
| 010 | MyInvois UUID as Primary ID | 2026-02-05 | ✅ Accepted |
| 011 | Audit Retention (7 years) | 2026-02-05 | ✅ Accepted |
| 012 | Phase 1 Scope (Core Only) | 2026-02-05 | ✅ Accepted |
| 013 | Replace MOVEX REST API with DB2 Direct Access | 2026-02-16 | ✅ Accepted |
| 014 | SQLite Audit Storage via EF Core | 2026-03-04 | ✅ Accepted |

---

**Owner:** Development Team
**Review Date:** 2026-03-09 (post-MVAI learnings + Phase 2 kickoff)
**Contact:** Architecture Team

---

---

## ADR-015: MyInvois.Api HTTP Host — Implements ADR-001 Portal Integration

**Date:** 2026-03-11
**Status:** Accepted

### Context
SM-Portal required invoice extract functionality (AP/AR list view) sourced from MOVEX DB2.
ADR-001 (Feb 2026) mandated HTTP separation between portal and MyInvois-Service. An initial
implementation attempt used a direct project reference, which was rejected after architectural
review identified two blockers: (1) violation of ADR-001, (2) LHDN OAuth/XAdES credential
surface would be loaded into SM-Portal's process alongside a user-facing Windows AD API.

### Decision
**Create `MyInvois.Api` — a dedicated ASP.NET Core Web API host project in this repository —
and have SM-Portal call it via HTTP with an internal API key.**

### Key Implementation Choices
- **Two-tier API key auth** (`X-API-Key` primary / `X-Admin-Key` admin) with timing-safe
  comparison (`CryptographicOperations.FixedTimeEquals`) — sourced from Reporting-Service pattern
- **API versioning** — route prefix `/api/v1/` from day one to allow non-breaking future changes
- **CorrelationId propagation** — SM-Portal forwards `X-Correlation-Id`; MyInvois.Api pushes it
  into Serilog LogContext for end-to-end tracing
- **Polly retry + circuit breaker** on SM-Portal's `HttpClient` (workspace rule compliance)
- **`totalCount` in response envelope** — enables future server-side pagination without breaking
  the contract
- **`IInvoiceDataSource` used directly** (not `IMovexInvoiceReader`) — `RawInvoiceRecord` already
  carries `CustomerName` (AR) and `PartyId` (AP); party enrichment deferred to a detail view

### Deferred Items
| Item | Condition to activate |
|------|-----------------------|
| Swagger/OpenAPI | When 2nd caller integrates |
| Rate limiting (30 req/min) | When exposed beyond localhost |
| Server-side pagination | When query exceeds 60s timeout |
| Azure Functions compatibility | At Azure migration planning (ODBC constraint: see risk log) |

### Consequences
- ✅ ADR-001 implemented as designed
- ✅ LHDN OAuth credentials isolated from SM-Portal
- ✅ `MovexDb:ConnectionString` secret lives only in `MyInvois.Api` user-secrets
- ✅ SM-Portal deployment decoupled from MyInvois data-layer changes
- ✅ Architecture is compatible with future microservices evolution (replace API key → Azure AD
  Managed Identity; replace ODBC → M3 MI REST API or ODBC proxy sidecar)
- ⚠️ Second IIS site/app pool required on the Windows Server host (port 5051, localhost-only)

### Files Created
- `src/MyInvois.Api/MyInvois.Api.csproj`
- `src/MyInvois.Api/Program.cs`
- `src/MyInvois.Api/appsettings.json`
- `src/MyInvois.Api/Middleware/ApiKeyMiddleware.cs`
- `src/MyInvois.Api/Controllers/InvoicesController.cs`
- `src/MyInvois.Api/Models/InvoiceModels.cs`

---

## ADR-016: AR Invoice Line Items — FSLEDG→OINVOH→ODLINE Join Path

**Date:** 2026-04-02
**Status:** Accepted

### Context

AR invoice line items were returning 0 rows for all invoices. Investigation revealed `BuildArLineItemsSql` used `OINVOL.OIIVNO` — a column that does not exist in `OINVOL` on this DB2 for i installation. The `OdbcException` was silently swallowed in `FetchArLineItemsAsync`, so 0 lines was the silent result for every AR invoice.

An intermediate fix attempted `FSLEDG.ESPYNO = OINVOL.ONPYNO` but produced 1992–9066 duplicate rows per invoice (payer number is not unique per invoice — one payer has many invoices).

### Decision

**Use `FSLEDG → OINVOH (via ESVONO = UHVONO) → ODLINE (via UHIVNO = UBIVNO)` as the canonical AR line item join path.**

### Rationale

- `FSLEDG.ESVONO` = voucher number uniquely identifies one AR posting → one `OINVOH` row
- `OINVOH.UHIVNO` = internal invoice number in ODLINE, the true delivery line FK
- `OINVOL` is a routing/planning table; it has no reliable invoice-level line item link
- Confirmed 96% coverage: 117 of 122 2026 AR invoices have ODLINE rows

### Key DB2 Schema Facts (ODLINE)

| Column | Meaning |
|--------|---------|
| `UBIVNO` | Internal invoice number (FK from `OINVOH.UHIVNO`) |
| `UBIVQT` | Invoiced quantity |
| `UBLNAM` | Line net amount |
| `UBSAPR` | Unit sales price |
| `UBSPUN` | Unit of measure (sales price) |
| `UBITNO` | Item number |
| `UBORNO` | Customer order number |
| `UBPONR` / `UBPOSX` | Order line / sub-line (for ordering + OOLINE join) |

### Totals Recalculation

`FSLEDG.ESCUAM` is the full AR ledger amount but may span multiple deliveries. `ODLINE` rows from one voucher cover only one delivery. Extended `MovexInvoiceReader` totals recalculation (was AP-only) to both AP and AR: `TotalExclTax` / `TotalTax` / `TotalInclTax` are now recalculated from the sum of fetched ODLINE lines.

### Classification Codes

LHDN classification codes (001–045) are a **fixed LHDN reference table**. `MITMAS.MMITCL` is a MOVEX product group code — unrelated to LHDN codes. Removed MITMAS join. Default `"022"` (Others) used for all lines until Finance maps product groups to proper codes. Same correction applied to AP (was `"000"`).

### Files Changed

- `src/MyInvois.Service/DataAccess/DirectQueryDataSource.cs` — `BuildArLineItemsSql`, `FetchArLineItemsAsync`, `ArLineItemDto.ToLineRecord()`
- `src/MyInvois.Service/DataAccess/RawInvoiceRecord.cs` — added `PayerNo` field
- `src/MyInvois.Service/Services/MovexInvoiceReader.cs` — totals recalculation extended to AR

### Consequences

- ✅ 255+ AR invoices now have line items (was 0)
- ✅ 3 AR invoices pass full local validation and reach LHDN pre-prod API
- ✅ All 233 unit tests continue passing
- ⚠️ 4% of 2026 AR invoices (5/122) have no ODLINE rows — these will generate "0 line items" validation errors; Finance must investigate
- ⚠️ Finance team must map product groups (`MITMAS.MMITCL`) to LHDN classification codes before go-live; `"022"` is a placeholder

---

## ADR-017: XAdES Signature — Correct Digest Scope (DS320/DS322 Fix)

**Date:** 2026-05-11
**Status:** Accepted

### Context

All invoice submissions returned HTTP 200 (accepted) but LHDN's asynchronous Step 08 validator subsequently moved every document to `Invalid` state with two errors:

- **DS320** — Signed properties digest value doesn't match digest calculated value from provided signed properties section where ID is `id-xades-signed-props`
- **DS322** — Document digest value doesn't match digest calculated value from existing document content

Finance Manager confirmed zero valid documents in the portal. The bugs existed since the XAdES signing implementation in Sprint 7 (2026-04-01) but were not detected because the HTTP 200 response was treated as success, and no portal check was performed until May 2026.

### Root Cause — DS322 (Document Digest)

`BuildSigned` computed `docDigest` over `BuildUnsigned(doc)` (no `UBLExtensions`). `BuildSubmissionPayload` then sent `Minify(signedDoc)` base64-encoded — the signed document includes `UBLExtensions`. LHDN decodes the base64, strips `UBLExtensions`, re-hashes, and compares to `docDigest`. Any serialization difference between the unsigned document used for hashing and the LHDN-reconstructed canonical form produced a mismatch.

### Root Cause — DS320 (SignedProperties Digest)

`BuildSignedProperties` returned a standalone wrapper `{ "SignedProperties": [...] }` which was hashed for `propsDigest`. The same SignedProperties content was then **reconstructed as a separate object literal** inside `QualifyingProperties` in `UBLExtensions`. LHDN re-extracts the `SignedProperties` node from the embedded document and re-hashes it. Two separate `new { ... }` object literals in C# are not guaranteed to serialize identically — and the wrapper was included in the hash but not in the embedded structure.

### Decision

**Fix both digest scopes so hashed bytes and embedded bytes are byte-for-byte identical.**

1. **DocDigest** — computed over `Minify(BuildUnsigned(doc))`. This canonical JSON (with `Signature` element, without `UBLExtensions`) is exactly what LHDN reconstructs when it strips `UBLExtensions` from the submitted document.

2. **PropsDigest** — computed over `Minify(signedPropsNode)` where `signedPropsNode` is the inner array `[{ Id, SignedSignatureProperties }]` — no wrapper. The **same object reference** is then embedded directly into `QualifyingProperties.SignedProperties`, guaranteeing identical serialization.

### Signing Sequence (correct — per LHDN SDK v1.5)

```
1. canonicalDoc = BuildUnsigned(doc)          // includes Signature element, no UBLExtensions
2. canonicalJson = Minify(canonicalDoc)
3. docDigest = SHA256(canonicalJson) → base64
4. sig = RSA-SHA256(canonicalJson) → base64   // sign canonical bytes, not signed doc
5. certDigest = SHA256(cert.RawData) → base64
6. signedPropsNode = [{ Id, SignedSignatureProperties }]  // inner array only, no wrapper
7. propsDigest = SHA256(Minify(signedPropsNode)) → base64
8. ublExtensions = BuildUblExtensions(..., signedPropsNode)  // embed same object reference
9. finalDoc = envelope with ublExtensions      // submitted bytes include UBLExtensions
```

### Concurrent Fixes

**CF403/CF414 (Contact validation):** `UblDocumentBuilder` emitted `"NA"` for both `Telephone` and `ElectronicMail`. LHDN requires Telephone ≥ 8 characters and rejects `"NA"` email format. Fix: removed `ElectronicMail`; `Telephone` populated from `CompanyDetails.Phone` config field. Added `SupplierPhone`/`BuyerPhone` to `MyInvoiceDocument` and `Phone` to `CompanyDetails`.

**CF321 (Date too old — pre-prod only):** AP invoices posted this week carry old supplier issue dates. AR invoices always use today's date (FSLEDG has no separate invoice date column → mapper fallback = `DateTime.UtcNow`). Not a code bug. Smoke test updated to prefer recent-dated invoices and treat CF321 as a warning rather than hard failure.

### LHDN Async Validation Model

HTTP 200 from `/documentsubmissions` = synchronous acceptance only. Step 08 (signature validation) runs asynchronously. A document can be accepted synchronously and invalidated minutes later. Always verify UUID status in the portal after submission — do not treat HTTP 200 as definitive success.

### Confirmed Working Submissions (2026-05-11)

| Invoice | UUID | Amount |
|---------|------|--------|
| 009709972 | `V99AK6H7RF5G0ETZHJ61QARK10` | MYR 257.40 |
| 009709973 | `BF4CNPV9GW80DFKATX61QARK10` | MYR 3,008.00 |
| 009709974 | `5T0WTYS9KGMZ4Z086A71QARK10` | MYR 9,886.55 |

### Files Changed

- `src/MyInvois.Service/Services/UblDocumentBuilder.cs` — `BuildSigned`, `BuildSignedPropertiesNode` (renamed and refactored), `BuildUblExtensions` (accepts `signedPropsNode`)
- `src/MyInvois.Service/Models/MyInvoiceDocument.cs` — added `SupplierPhone`, `BuyerPhone`
- `src/MyInvois.Service/Configuration/CompanySettings.cs` — added `Phone`
- `src/MyInvois.Service/Services/MyInvoisMapper.cs` — populate `SupplierPhone`/`BuyerPhone`
- `appsettings.json` — `Companies[100/300].Phone = "6072319006"`
- `tests/.../Smoke/FullPipelineSmokeTest.cs` — CF321 tolerance, recent-date preference

### Consequences

- ✅ 3 AR invoices accepted and validated by LHDN pre-prod (DS320/DS322 eliminated)
- ✅ Full pipeline smoke test passes (CF321 correctly treated as environmental, not code bug)
- ✅ Contact block now valid (real phone number, no email placeholder)
- ⚠️ All prior submissions (before 2026-05-11) had broken signatures — they will show DS320/DS322 in portal and cannot be corrected; they must be resubmitted
- ⚠️ HTTP 200 from LHDN is not a sufficient success signal — portal verification required

---

## ADR-018: JSON String Escaping Must Match LHDN's Serializer (DS322 — Second Root Cause)

**Date:** 2026-08-10
**Status:** Accepted
**Amends:** ADR-017 (digest scope). That fix was correct but incomplete.

### Context

After ADR-017 corrected both digest scopes, DS320/DS322 continued to appear intermittently in
Step 08 validation — across sales invoices, credit notes, and purchase documents alike. No common
characteristic was identifiable: two documents with near-identical structure and content would
behave differently, one valid and one rejected.

An enquiry was raised with the MyInvois Help Desk on 2026-07-21. Their reply (received before this
decision) stated only that "signature value 1 / value 3 is calculated wrongly — please follow the
signature document again", and noted that *"even adding extra spacing in the document again signing
the document will trigger this error"*. That last remark describes a byte-level canonicalisation
mismatch, but no specific cause was identified by LHDN. Follow-up questions asking which normalisations
their library applies, and requesting the canonical string they hashed for a named failing document,
remained unanswered.

### Root Cause

`MinifyOptions` in `UblDocumentBuilder` never set an `Encoder`, so `System.Text.Json` used its
**default `JavaScriptEncoder`**. That encoder is deliberately conservative for HTML-injection safety
and escapes characters which LHDN's JSON library does **not** escape when it parses and re-serializes
the submitted document prior to re-hashing:

| Character | .NET default emits | LHDN re-serializes as | Typical source |
|---|---|---|---|
| `&` | `&` | `&` | Company names |
| `+` | `+` | `+` | International phone numbers |
| `'` | `'` | `'` | Customer / supplier names |
| `<` `>` | `<` `>` | `<` `>` | Free-text address lines |
| any non-ASCII | `\uXXXX` | literal UTF-8 | Accented names, en-dashes |

Our bytes carried the escape sequences; LHDN's carried the literal characters. The two SHA-256
digests therefore diverged. Because `Minify` is used for both the document digest and the signed
properties digest, this could surface as DS322 or DS320.

**This explains the absence of a pattern.** The failure is data-dependent, not document-type
dependent: any invoice whose text happened to be plain ASCII passed; any invoice containing one of
the characters above failed. It survived the ADR-017 fix because that addressed digest *scope*, and
it survived the earlier `DecimalNormalizer` fix because that addressed *numbers* — this is *strings*.

### Verification

Both encoder configurations were run against representative MOVEX-sourced values before the change
was made. Every sample containing `&`, `+`, `'`, `<`, or a non-ASCII character produced a different
SHA-256 digest under the two encoders; a plain-ASCII control sample produced an identical digest.

### Decision

**Set `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` on `MinifyOptions`.** This emits the
affected characters literally, matching LHDN's serializer byte-for-byte.

The `Unsafe` prefix refers solely to HTML-rendering contexts (XSS). This JSON is hashed, base64
encoded, and submitted over HTTPS to an API — it is never rendered as HTML, so the caveat does not
apply here.

### Files Changed

- `src/MyInvois.Service/Services/UblDocumentBuilder.cs` — `MinifyOptions.Encoder`, `System.Text.Encodings.Web` using
- `tests/.../Services/UblDocumentBuilderTests.cs` — 7 regression tests covering `&`, `'`, non-ASCII, en-dash, `<>`, `+`-prefixed phone, and a plain-ASCII control

### Consequences

- ✅ Removes a root cause of intermittent DS320/DS322 that survived ADR-017
- ✅ Regression tests pin the encoder behaviour; reverting it fails the suite
- ⚠️ Documents submitted before this change that contained non-ASCII or the affected punctuation had
  invalid signatures and must be resubmitted
- ⚠️ LHDN has not confirmed this diagnosis; it is our own finding. The Help Desk thread should be kept
  open until Step 08 results over a full batch confirm the fix in practice

---

## ADR-019: AR ESTRCD=20 Is a Settlement Posting, Not a Credit Note

**Date:** 2026-08-10
**Status:** Accepted
**Supersedes:** the credit-note interpretation of `ESTRCD=20` assumed by the original AR query design
**Evidence:** `src/Database/Diagnostics/AR_CreditNote_vs_Payment_Profiling.sql`

### Context

The AR query fetched `FSLEDG.ESTRCD IN ('10','20')` and mapped every `ESTRCD=20` row to LHDN document
type `02` (credit note). Business users reported that documents were being submitted as AR credit
notes which were not credit notes, and asked that AR treat them the way AP treats payments.

AP and AR were already asymmetric:

| | AP (`FPLEDG`) | AR (`FSLEDG`) |
|---|---|---|
| Invoice code | `eptrcd = 40` | `ESTRCD = 10` |
| Payment code | `eptrcd = 50` — **excluded at source** | *(none identified)* |
| Credit note | `eptrcd=40` row, negative amount | assumed `ESTRCD = 20` |

An earlier fix (`DeduplicateArRecords`) removed `ESTRCD=20` rows sharing an `ESCINO` with an
`ESTRCD=10` row, treating the pairing as an edge case. It was not an edge case.

### Evidence

Read-only profiling of `FSLEDG`, production company, all divisions, `ESYEA4 >= 2024`. Monetary values
are deliberately not reproduced here; re-run the diagnostics script if figures are needed.

- **No standalone `ESTRCD=20` rows exist.** The pairing-integrity check (Q6) over 646 rows returned
  `UnpairedCount = 0`. Most pairs net to exactly zero; the remainder are partially-settled invoices.
- **Amounts are exact mirror images.** In every division and year sampled, the `ESTRCD=20` min/max is
  the precise negation of the `ESTRCD=10` range, with equal and opposite period sums. In one
  low-volume division the two codes net to exactly zero.
- **`ESTRCD=20` rows frequently outnumber `ESTRCD=10` rows** — by roughly 40% in the two
  highest-volume division/year combinations. Credit notes cannot outnumber invoices; partial
  settlements can, because one invoice attracts multiple payment postings.
- **Only codes 10 and 20 exist** in any division or year sampled.

### Decision

**Filter AR to `ESTRCD = '10'` at source**, exactly as AP filters `eptrcd = 40`. `ESTRCD=20` rows are
never fetched and never submitted.

### Alternatives Considered

- **Derive AR document type from the `ESCUAM` sign, mirroring AP.** Rejected: `ESCUAM` is legitimately
  negative on genuine `ESTRCD=10` invoices once M3 applies a cash receipt against them, so a
  sign-based rule reintroduces the very bug it would be meant to fix — and it would only relabel
  rows, not stop submitting them.
- **Exclude `ESTRCD=20` rows lacking `ODLINE` line items.** Rejected as unnecessary: it presumed
  standalone `ESTRCD=20` rows existed to be filtered. They do not.
- **Leave `DeduplicateArRecords` as the sole guard.** Rejected: functionally equivalent today, but in
  the wrong layer, fetching roughly half the AR result set per batch only to discard it, and leaving
  a misleading `ESTRCD IN ('10','20')` in the SQL for the next reader.

### Files Changed

- `src/MyInvois.Service/DataAccess/DirectQueryDataSource.cs` — `ESTRCD IN (?,?)` → `ESTRCD = ?` at three query sites
- `src/MyInvois.Service/Configuration/MovexDbSettings.cs` — `ArCreditNoteTransCode` → `ArSettlementTransCode`
- `src/MyInvois.Service/Services/MovexInvoiceReader.cs` — corrected transaction-code comment
- `src/Database/Diagnostics/AR_CreditNote_vs_Payment_Profiling.sql` — new, with findings recorded

### Consequences

- ✅ Settlement postings are no longer submitted to LHDN as credit notes
- ✅ AP and AR now follow the same shape: invoices only, settlements excluded at source
- ✅ Materially fewer AR rows fetched per batch
- ⚠️ **If Finance ever issues a genuine AR credit note, it will not be submitted.** The evidence says
  none exist across three years and every division, but this is the single assumption to revisit.
  `ArSettlementTransCode` is retained in configuration as the documented place to handle that case;
  revisit this ADR before wiring it back into the queries
- ⚠️ Finance had not yet confirmed how a genuine AR credit note would be raised at the time of this
  decision. The change was made on data evidence because the status quo was actively submitting
  incorrect documents to a tax authority
- ℹ️ `DeduplicateArRecords` becomes a no-op for `DirectQueryDataSource`; retained as defence-in-depth
  for `StoredProcedureDataSource` with its regression tests intact

---

**End of ADRs**
