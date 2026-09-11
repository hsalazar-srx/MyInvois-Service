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
| ADR-018 | JSON String Escaping Must Match LHDN's Serializer (DS322 second root cause) | `ai/memory/09-implementation-decisions.md` | Amends ADR-017. **Outcome 2026-08-11: Finance confirms almost all Step 08 issues resolved after UAT deploy.** Portal-read, not audit-DB verified; LHDN never confirmed the diagnosis (Help Desk enquiry 2026-07-21 unanswered) |
| ADR-019 | AR ESTRCD=20 Is a Settlement Posting, Not a Credit Note | `ai/memory/09-implementation-decisions.md` | Data-evidence based; profiling script `src/Database/Diagnostics/AR_CreditNote_vs_Payment_Profiling.sql`; Finance confirmation outstanding |
| ADR-020 | Country Codes Validated Against LHDN's Published List | `ai/memory/09-implementation-decisions.md` | Triggered by Belgian supplier rejection in first production batch (2026-09-10). Authority is LHDN's own CSV, embedded — verified to diverge from .NET RegionInfo in both directions (XKK / 6 territories) |
| ADR-021 | CountrySubentityCode Must Be an LHDN State Code (CV302) | `ai/memory/09-implementation-decisions.md` | Root cause confirmed from LHDN's own validationResults, not inferred — long-standing "CV303 unknown" was actually CV302 on the state code field. Also fixes the service discarding LHDN's validation detail |

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
| 2026-04-02 | **ADR-016 — AR line item join path confirmed**: `FSLEDG → OINVOH (ESVONO=UHVONO) → ODLINE (UHIVNO=UBIVNO)`. Do NOT use OINVOL — `OIIVNO` column does not exist in IBM i 7.4. 96% AR coverage. | Dev Team | Sprint 7. 233 tests passing (100%). |
| 2026-04-15 | **CONO=100 = Production, CONO=300 = Development/UAT** (PRE-12). `MOVEX_CONO` environment variable — never hardcoded. All dev/staging/demos use 300. Production deploy only uses 100. | Dev Team | `appsettings.Development.json` updated; `ActiveCompanyCodes` controls per-environment company scope. |
| 2026-05-13 | **XAdES signature — all 4 bugs resolved**: (1) docDigest = SHA256(Minify(invoice body with NO UBLExtensions AND NO Signature)); (2) propsDigest = SHA256(Minify(full QualifyingProperties)); (3) UBLExtensions inserted before Signature; (4) DecimalNormalizer JsonConverter strips trailing zeros pre-sign (LHDN re-serializes). LHDN pre-prod UUID `WAFDWH4YEA7BEMFEF10X0GRK10` confirmed Valid in portal. | Dev Team | XAdES signing is production-ready. |
| 2026-05-21 | **Extracted `MyInvoisTokenService`** from `MyInvoiceSubmitter` — OAuth token management now a separate `IMyInvoisTokenService` interface. Independently testable, single responsibility. | Dev Team | Previous: token logic embedded in submitter. Now: 3-arg constructor injection. |
| 2026-05-21 | **Extracted `MovexLineItemFetcher`** from `DirectQueryDataSource` — batch line item fetching is a separate class, avoiding N+1 queries per invoice header. | Dev Team | Performance improvement for batches >50 invoices. |
| 2026-05-21 | **Dead config classes removed**: `BatchConfiguration`, `ProcessingSettings`, `ValidationSettings` were never registered in DI. Corresponding JSON sections (`BatchProcessing`, `Processing`, `Validation`) removed from `appsettings.json`. Only real scheduler config: `BatchSchedulerSettings` / `BatchScheduler` section. | Dev Team | Sprint 9.X config cleanup. Resolves confusion about dead `EnableBatchProcessing` flag. |
| 2026-05-21 | **Config file rationalization**: Root `appsettings.json` = service defaults; `appsettings.Development.json` = dev overrides only; `src/MyInvois.Api/appsettings.json` = host-layer concerns (`ApiKeys`, `BatchScheduler`, `AllowedHosts`). | Dev Team | Sprint 9 configuration cleanup. |
| 2026-05-21 | **Sprint 9.13 — Polly retry validation**: Fixed 3 pre-existing test failures. Root cause: (1) mock HTTP responses had wrong shape (flat `{uuid}` vs LHDN envelope `{submissionUid, acceptedDocuments[]}`); (2) retry tests used real Polly delays. Fix: correct envelope shape across 4 test files; added injectable `retrySleepProvider` constructor param. | Dev Team | 276 tests passing (100%), up from 273. |
| 2026-05-21 | **AR/AP test coverage added**: `TestDataFactory.CreateValidPurchaseInvoice` rewritten with distinct external supplier party. 3 new `MyInvoisMapperTests` for `DocumentTypeCode` and party-swap logic. E2E `MixedArApBatch` test added. | Dev Team | Sprint 9 AR/AP coverage gap closed. |

| 2026-05-27 | **Validators and IMemoryCache not registered in DI** — `AddMyInvoisSubmissionPipeline()` was missing all 5 validator registrations (`IMandatoryFieldsValidator`, `IDateValidator`, `ICurrencyValidator`, `ITotalsValidator`, `ITINValidator`) and `services.AddMemoryCache()` (required by `TINValidator`). Root cause: unit/integration tests inject mocks directly into constructors, bypassing DI entirely, so missing registrations are invisible until first real request. Fix: all registrations added to `ServiceCollectionExtensions.cs`. | Dev Team | UAT deployment session. 500 errors on `POST /api/v1/batch/process-range` after successful auth. |
| 2026-05-27 | **`ValidateOnBuild = true` added** — `builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = true; o.ValidateScopes = true; })` added to `Program.cs`. App now crashes at startup (not at first request) if any DI registration is missing. Previously the app started healthy, 500 only on first controller hit. | Dev Team | Prevents future silent DI omissions from reaching production undetected. |
| 2026-05-27 | **UAT deploy workflow confirmed**: push to GitHub → download zip to `C:\Projects\MyInvois-Service\v2.0\MyInvois-Service-master\` on SRXWEBAPP1 → `dotnet publish .\src\MyInvois.Api\MyInvois.Api.csproj --configuration Release --output "C:\inetpub\wwwroot\MyInvois-Api\"` → recycle IIS app pool. IIS site `MyInvoisAPI`, port **5051**, root site (no virtual path prefix), physical path `C:\inetpub\wwwroot\MyInvois-Api`. Manual trigger: `POST http://localhost:5051/api/v1/batch/process-range` with `X-API-Key` header. | Dev Team | Correct port is 5051 (not 5000). PID 4 = IIS kernel-mode driver owning the port. |
| 2026-05-27 | **Batch endpoint real-data test succeeded in UAT** — 10 invoices fetched across 2 companies (CONO=100). `PlaceholderPartyDataProvider` in use as expected (`MovexDb:PartyDataSource = "Placeholder"`). Switch to `MovexMaster` blocked on Finance confirming TIN/BRN column names in OCUSMA/CIDMAS. | Dev Team | Pipeline confirmed end-to-end with real MOVEX data. LHDN submission result pending column name confirmation. |

| 2026-06-01 | **Certificate loading: Windows Store by thumbprint** — `CertificateThumbprint` setting added to `MyInvoisApiSettings`. `MyInvoiceSubmitter.LoadCertificate()` tries LocalMachine/CurrentUser store first, falls back to file path. IIS app pool granted private key read access via `certlm.msc`. UAT thumbprint: `A0E772A9F4EC1D26B732515A3430728E82D78FD7`. | Dev Team | Eliminates all password delivery problems for IIS deployments. |
| 2026-06-01 | **CertificatePassword with `{` corrupted by ASP.NET Core config token substitution** — `{` and `}` in config values are treated as placeholder tokens and stripped/corrupted in ALL delivery mechanisms: `appsettings.json`, `web.config` environmentVariables, machine-level env vars. Root cause: the password `Co9!m*{)` contains `{)` which ASP.NET Core config system parses as an incomplete token. Solution: load cert from Windows Store (no password needed) or use `CertificatePasswordFile` (plain-text file on disk, bypasses all config systems). `DecodeConfigPassword()` helper added for Base64 fallback. | Dev Team | Cross-cutting lesson: never store secrets with `{` or `}` in ASP.NET Core config. Use Windows Store, Key Vault, or file-based delivery. |
| 2026-06-01 | **`CompanySettings` not registered in DI** — `IOptions<CompanySettings>` always resolves (returns empty default) so `ValidateOnBuild` does not catch missing `Configure<CompanySettings>()`. Fix: bind `Companies` JSON section directly into dictionary: `builder.Services.Configure<CompanySettings>(cs => builder.Configuration.GetSection("Companies").Bind(cs.Companies))`. | Dev Team | `ValidateOnBuild` only catches missing type registrations, not missing config bindings. |
| 2026-06-01 | **Party data provider switched to `MovexMaster`** — `MovexMasterPartyDataProvider` now injects `ForeignPartyDefaultsSettings`. Supplier TIN defaults to `EI00000000030`, BRN to `IDVRNO` then `NA`. Customer TIN defaults to `EI00000000020`, BRN from `OKVRNO` (Finance confirmed: single field holds ABN for AU, VAT reg no for overseas). `MovexDb:PartyDataSource = "MovexMaster"` set in UAT config. | Dev Team | Finance confirmed: `OKVRNO` = customer BRN for all customer types. Column names confirmed 2026-06-01. |

| 2026-06-03 | **IIS idle timeout kills DailyBatchHostedService** — IIS default idle timeout is 20 minutes. App pool shuts down worker process after 20 min of no HTTP requests, stopping the `BackgroundService` scheduler before it reaches 02:00. Fix: set idle timeout to 0 (`appcmd set apppool "MyInvoisAPI" /processModel.idleTimeout:"00:00:00"`) and disable periodic recycling (`/recycling.periodicRestart.time:"00:00:00"`). Root cause: `BackgroundService` schedulers require a persistent process — IIS idle timeout is incompatible with long-running background work. | Dev Team | UAT overnight batch never fired. Scheduler started at 12:33, process killed at 12:54 (20 min idle). Fix applied; next overnight run scheduled for 02:00 2026-06-04. |

| 2026-06-10 | **AR line item query returned extra lines from sibling invoices** — Three separate bugs in `MovexLineItemFetcher.BuildArLineItemsSql()`: (1) `OINVOH` join used only `ESVONO=UHVONO` — a single voucher can link to multiple `OINVOH` rows in batch postings, pulling sibling invoice lines. Fix: added `oh.UHIVNO = TRIM(f.ESCINO)` to the join. (2) Batch parameter filter used `ESVONO IN (?)` — one voucher covers many customer invoices. Fix: switched to `(ESCINO, ESVONO) IN (VALUES (?,?))` tuple matching (same as AP query). (3) `DecimalNormalizer.TrimEnd('0')` stripped integer digits from `DECIMAL(15,6)` values — DB2 returns `2000` as `2000.000000`; `G29` gives `"2000.000000"`, `TrimEnd('0')` → `"2."`, `TrimEnd('.')` → `"2"`. Fix: only trim when string contains a decimal point. Test `Transform_LargeQuantity_PreservedInDocumentAndUblJson` added. 277 tests passing. | Dev Team | Discovered via UAT Finance feedback: submitted invoices had extra lines and wrong quantities. Root cause traced via DB2 diagnostic queries on `ODLINE.UBIVQT DECIMAL(15,6)`. |

| 2026-06-10 | **EF Core version mismatch between Api and Service projects** — `MyInvois.Api` pinned `Microsoft.EntityFrameworkCore` at `8.0.27`; `MyInvois.Service` uses `8.0.*` which resolved to `8.0.28` via `Microsoft.EntityFrameworkCore.Sqlite`. NuGet NU1605 downgrade error blocked `dotnet publish` on UAT server. Fix: bumped `MyInvois.Api` pin to `8.0.28`. | Dev Team | Blocked UAT redeploy on 2026-06-10. Rule: floating `*` versions in Service project must be matched by explicit pins in Api project whenever a new patch is released. |

---

## How to Add New Entries

1. **Architecture decision** → Create or update ADR in `implementation-decisions.md`.
2. **Add evidence** → Log the decision here with link to meeting notes/approvals.
3. **If user-visible** → Update `ai/evidence/release-notes.md` and `ai/evidence/change-impact.md`.
