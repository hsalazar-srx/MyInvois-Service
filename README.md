# MyInvois-Service — e-Invoicing Integration Platform

**Version:** 0.1-DESIGN  
**Status:** Phase 1 — Implementation In Progress (DB2 Direct Access per ADR-013)
**Target Go-Live:** MVAI (March 31, 2026 — extension reflects dependencies on MOVEX DB2 readiness, MyInvois API stability, and internal audit sign-off)
**Architecture:** Hybrid (Standalone Service + MOVEX-Portal Integration)
**Data Source:** MOVEX Database (IBM DB2/AS400) — Direct Access (ADR-013)

---

## 📋 Project Overview

**MyInvois-Service** is a standalone .NET 8.0 microservice that transforms MOVEX invoices into Malaysian MyInvois e-invoicing format and submits them to the official LHDNM system.

### Scope (MVAI Iteration 1)

✅ **In Scope:**
- Monthly batch submission (sales: ~100/month, purchase: ~500-1000/month)
- Transform MOVEX invoices to MyInvois UBL 2.1 schema
- Mandatory field validation (20+ fields per constraints)
- Error handling and audit logging to SQLite (EF Core, ADR-014)
- Manual retry mechanism (via audit log queries)

❌ **Out of Scope (Phase 2):**
- Portal UI dashboard (retry buttons, status polling)
- Real-time daily submission
- Automatic retry with circuit breaker
- Status polling from MyInvois API
- Cloud migration (Azure Functions)

---

## 📋 Workspace Standards Compliance

**IMPORTANT:** This project MUST comply with **[WORKSPACE_RULES.md](../.github/WORKSPACE_RULES.md)**.

**Key Requirements:**
- ✅ Audit logs stored in **SQLite** (`audit.db` via EF Core 8, ADR-014 — replaces SQL Server)
- ✅ Connection strings in **User Secrets** (development) or **Azure Key Vault** (production)
- ✅ **TLS 1.2+** for all external API connections (MyInvois API)
- ✅ **7-year retention** for audit logs (ISO 27001 compliance)
- ✅ PascalCase naming for C# classes, camelCase for variables
- ✅ Quarterly backup restoration tests

**Validation:** Run `../.github/scripts/validate-workspace-compliance.ps1` before committing.

---

## 🧩 Skills-Based Architecture Compliance

**This project follows SRX Skills-Based Architecture** - reusing centralized skills and consulting domain experts.

### Skills Used

| Skill ID | Category | Used In | Status |
|----------|----------|---------|--------|
| `architecture/configuration-management` | Architecture | `appsettings.json`, User Secrets | ✅ CONFIGURED |
| `architecture/clean-architecture` | Architecture | `src/` folder structure | ✅ SCAFFOLDED |
| `architecture/dotnet-api-design` | Architecture | Service interfaces | ✅ DESIGNED |
| `integration/movex-db2-data-source` | Integration | `DataAccess/`, `MovexInvoiceReader.cs` | ✅ v1.0 IMPLEMENTED (ADR-013) |
| `architecture/resilience-patterns` | Architecture | `MyInvoiceSubmitter.cs` | 📝 DOCUMENTED (needs implementation) |
| `architecture/audit-logging-framework` | Architecture | `AuditLogger.cs` | 📝 DOCUMENTED (needs implementation) |

### Skills Gaps (New Skills Proposed)

| Proposed Skill ID | Purpose | Status |
|-------------------|---------|--------|
| `integration/myinvois-document-builder` | Transform MOVEX → MyInvois UBL 2.1 | 📝 Spec Needed |
| `integration/myinvois-validator` | Validate LHDNM mandatory fields | 📝 Spec Needed |
| `integration/oauth-token-manager` | OAuth 2.0 token caching & refresh | 📝 Spec Needed |
| `integration/xades-signer` | XAdES v1.1 XML signatures | 📝 Spec Needed |
| `integration/api-rate-limiter` | API rate limiting (100 req/min) | 📝 Spec Needed |

### Agents Consulted

| Agent | Consulted? | Purpose |
|-------|------------|---------|
| `@expert-movex-dotnet` | ⚠️ **Should have been consulted** | M3 integration patterns, .NET API design |

**Skills Audit:** See [ai/memory/00-skills-audit.md](ai/memory/00-skills-audit.md) for complete audit.

**Centralized Registry:** [.github/skills/](../.github/skills/) and [.github/agents/](../.github/agents/)

---

## 🏗️ Architecture

### Project Structure

```
MyInvois-Service/
├── Services/                          ← Business logic
│   ├── InvoiceProcessor.cs            ← Main orchestrator
│   ├── MovexInvoiceReader.cs          ← Fetch from MOVEX DB2/AS400 database
│   ├── MyInvoiceMapper.cs             ← Transform + validate
│   ├── MyInvoiceSubmitter.cs          ← Submit to MyInvois API
│   └── AuditLogger.cs                 ← Log to SQLite (EF Core, ADR-014)
│
├── Validators/                        ← Field-level validation
│   ├── MandatoryFieldsValidator.cs
│   ├── TINValidator.cs
│   ├── DateValidator.cs
│   ├── CurrencyValidator.cs
│   └── TotalsValidator.cs
│
├── Models/                            ← DTOs
│   ├── MovexInvoice.cs
│   ├── MyInvoiceDocument.cs
│   └── SubmissionResult.cs
│
├── DataAccess/                        ← DB2 data access layer (ADR-013)
│   ├── IPartyDataProvider.cs          ← Pluggable data provider interface
│   ├── DirectQueryDataSource.cs       ← Direct SQL query strategy
│   └── StoredProcedureDataSource.cs   ← Stored procedure strategy
│
├── Configuration/                     ← Settings & configuration
│   ├── MovexDbSettings.cs             ← MOVEX DB2/AS400 connection settings
│   ├── MyInvoisApiSettings.cs
│   ├── BatchConfiguration.cs
│   ├── ProcessingSettings.cs
│   └── ValidationSettings.cs
│
├── Controllers/                       ← HTTP API (for manual triggers)
│   └── InvoiceController.cs
│
├── Database/                          ← DB2 reference SQL scripts (MOVEX queries)
│   ├── AP_AR_Invoices_CMP100_CMP300.sql
│   └── DB2_PartyData_Reference.sql
│
├── tests/                             ← Unit & integration tests
│   ├── MyInvois.Service.Tests/
│   └── testdata/
│
├── ai/                                ← AI/LLM context
│   ├── memory/
│   │   ├── 03-myinvois-requirements.md  ← Constraints + traceability (merged)
│   │   ├── 09-implementation-decisions.md    ← ADRs
│   │   ├── 10-testing-strategy.md           ← Testing plans
│   └── evidence/
│
├── docs/                              ← Operational documentation
│   ├── SETUP.md                       ← Local setup guide
│   ├── DEPLOYMENT.md                  ← Deployment runbook
│   └── TROUBLESHOOTING.md             ← Operational support
│
├── appsettings.json                   ← Production settings
├── appsettings.Development.json       ← Development overrides
├── Program.cs                         ← Entry point
├── MyInvois.Service.csproj            ← Project file
└── README.md                          ← This file
```

---

## 🚀 Getting Started

### Prerequisites

- **.NET 8.0 SDK** (or later)
- **IBM DB2 iSeries Access ODBC driver** for MOVEX AS400 access (`System.Data.Odbc` — no separate NuGet needed)
- **MOVEX DB2/AS400** connection credentials (server, database, schemas: `mvxcdta`/`mvxc300`)
- **MyInvois Sandbox credentials** (ClientId, ClientSecret, TIN)
- **Windows domain account** for local development

### Local Setup (5 minutes)

#### 1. Clone, Restore & Enable Hooks

```bash
git clone https://github.com/hsalazar-srx/MyInvois-Service.git
cd MyInvois-Service
dotnet restore
```

Then activate the pre-commit hook (one-time, per developer):

```powershell
# Option A — automated (recommended)
.\setup-hooks.ps1

# Option B — manual
git config core.hooksPath .githooks
```

> The pre-commit hook enforces **Skills-First Architecture** — it blocks `src/` commits
> when `ai/memory/00-skills-audit.md` is absent. See [.githooks/README.md](.githooks/README.md).

#### 2. Configure User Secrets

```powershell
# Set MOVEX DB2/AS400 connection
dotnet user-secrets set "MovexDb:ConnectionString" "Server=YOUR_AS400;Database=YOUR_DB;UserID=your-user;Password=your-password;"

# Set MyInvois credentials
dotnet user-secrets set "MyInvoisApi:ClientId" "your-client-id"
dotnet user-secrets set "MyInvoisApi:ClientSecret" "your-client-secret"

# SQLite audit log path is pre-configured in appsettings.Development.json (./data/audit.db)
# No secret needed for local development — the database is auto-created on first run
```

#### 3. Run — Audit Database Created Automatically

The SQLite `audit.db` is created by EF Core on first startup. No manual schema step needed.

#### 4. Run Locally

```bash
dotnet run --launch-profile Development
```

---

## 📊 Configuration

### appsettings.json Structure

All sensitive values are stored in **User Secrets** (never committed). The keys below map
directly to the `appsettings.json` template already in the repo.

```json
{
  "MovexDb": {
    "ConnectionString": "{{FROM_USER_SECRETS}}",
    "DataSourceStrategy": "DirectQuery",
    "SchemaCmp100": "mvxcdta",
    "SchemaCmp300": "mvxc300",
    "ActiveCompanyCodes": ["100", "300"],
    "CommandTimeoutSeconds": 60,
    "MaxPoolSize": 10,
    "PartyDataSource": "Placeholder",
    "ArDivision": "L",
    "ArTransCode": "10",
    "ArCustomerStatus": "20",
    "ArMinYear": 0,
    "SupplierTinColumn": "",
    "SupplierBrnColumn": "",
    "CustomerTinColumn": "",
    "CustomerBrnColumn": ""
  },
  "MyInvoisApi": {
    "BaseUrl": "https://sandbox.myinvois.hasil.gov.my",
    "Environment": "sandbox"
  },
  "Companies": {
    "100": { "TIN": "...", "Name": "...", "BRN": "..." },
    "300": { "TIN": "...", "Name": "...", "BRN": "..." }
  },
  "ForeignPartyDefaults": {
    "SupplierTIN": "EI00000000030",
    "BuyerTIN": "EI00000000020"
  },
  "BatchProcessing": {
    "SalesBatchSize": 100,
    "PurchaseBatchSize": 50,
    "DelayBetweenBatchesMs": 600
  },
  "Validation": {
    "TINCache_TTL_Hours": 1,
    "RequireExchangeRateForNonMYR": true,
    "AllowNullBuyerTIN": true
  }
}
```

**Key configuration notes:**

| Key | Description |
|-----|-------------|
| `MovexDb:DataSourceStrategy` | `DirectQuery` (default) or `StoredProcedure` |
| `MovexDb:PartyDataSource` | `Placeholder` (dev) or `MovexMaster` (queries CIDMAS/OCUSMA) |
| `MovexDb:ArDivision` / `ArTransCode` / `ArCustomerStatus` | AR query filters — defaults match production values |
| `MovexDb:ArMinYear` | Set to `0` to use dynamic (last-year) default, or set an explicit year |
| `MovexDb:SupplierTinColumn` / `CustomerTinColumn` | CIDMAS/OCUSMA column names for TIN — leave empty until confirmed by Finance |
| `Companies:100` / `Companies:300` | Supplier TIN/BRN/Name for each company — populate before production |
| `ForeignPartyDefaults` | Generic EI numbers for non-Malaysian parties (MyInvois standard) |

---

## 🔑 Key Features

### 1. MOVEX Integration (DB2 Direct Access — ADR-013)
- Query invoices directly from IBM DB2/AS400 tables (`fpledg`, `fsledg`, `fgledg`)
- DataAccess layer with strategy pattern (`DirectQueryDataSource` / `StoredProcedureDataSource`)
- Pluggable `IPartyDataProvider` interface for data retrieval
- Uses `System.Data.Odbc` + Dapper for efficient database access (ADR-013 Gap #3)
- Schemas: `mvxcdta` (data), `mvxc300` (programs)

### 2. MyInvois Transformation
- Map MOVEX fields to UBL 2.1 schema
- Apply 20+ mandatory field validations
- Generate validation errors for audit trail
- Support multiple invoice types (sales, purchase)

### 3. Validation Engine
- **MandatoryFieldsValidator**: Supplier TIN, buyer name, invoice number, etc.
- **TINValidator**: Format validation + optional API lookup
- **DateValidator**: No placeholders, real dates, UTC conversion
- **CurrencyValidator**: ISO 4217 codes, exchange rates
- **TotalsValidator**: Mathematical consistency

### 4. Submission & Retry
- OAuth token caching (1 hour TTL)
- Rate limit handling (100 req/min)
- Error classification (no-retry vs retriable)
- Manual retry via audit log queries

### 5. Audit Logging
- SQLite `audit.db` via EF Core 8 — 30-column schema, WAL mode, auto-created on startup (ADR-014)
- MyInvois-specific fields (UUID, submission ID, invoice financials)
- Request/response payloads for forensics
- Correlation IDs for distributed tracing

---

## 📈 Performance Targets (MVAI)

| Metric | Target | Measurement |
|--------|--------|------------|
| **Submission success rate** | ≥95% | (Success / Total) × 100 |
| **Avg time per invoice** | <5 sec | End-to-end (fetch + transform + submit) |
| **Batch throughput** | ≥100 invoices in <10 min | Sales batch timing |
| **Audit log completeness** | 100% | All submissions logged |

---

## 🔐 Security & Compliance

### Authentication
- **MOVEX DB2/AS400**: Connection string (stored in User Secrets)
- **MyInvois API**: OAuth 2.0 Client Credentials (token cached)
- **Database**: Integrated Security (Windows domain account)
- **Invoice Signing**: X.509 digital certificate (Malaysian CA, stored in encrypted server storage)

### Encryption
- **In Transit**: TLS 1.2+ for all API calls
- **At Rest**: BitLocker on IIS server volume + NTFS ACL on `audit.db` (ADR-014)
- **Secrets**: User Secrets (dev), Encrypted Server Storage (production)
- **Digital Certificate**: Stored in encrypted directory with Windows EFS + NTFS permissions
- **Certificate Private Key**: Never exported, protected by Windows credential storage (Credential Manager or DPAPI)

### Digital Certificate Requirements (XAdES v1.1 Signing)
- **Format**: X.509 certificate in PFX/P12 format
- **Issuer**: Recognized Malaysian Certificate Authority (Mykad CA, Entrust, DigiCert, Cybertrust, GlobalSign)
- **Algorithm**: RSA-SHA256 (not SHA1)
- **Validity**: Minimum 2-3 years
- **Non-repudiation**: Must support non-repudiation capability
- **Ownership**: Issued in company name (legal entity name)
- **Storage Location (Production)**: `C:\Certs\MyInvois\myinvois-cert.pfx` (encrypted with EFS)
- **Password Storage**: Windows Credential Manager or DPAPI (never in config files)

### Audit Trail
- **7-year retention** (ISO 27001, Malaysian tax law)
- **Immutable logging** (append-only)
- **PII protection** (no customer details in logs)
- **Correlation IDs** for tracing

### Error Handling
| Error | Handling | Retry? | User Action |
|-------|----------|--------|------------|
| **Duplicate (DS302)** | No retry | N/A | Mark as "Already Submitted" |
| **Invalid TIN** | No retry | N/A | Correct in MOVEX |
| **Rate limit (429)** | Exponential backoff | Yes (3x) | Log warning |
| **Server error (500)** | Retry 3x | Yes | Mark as "Failed", retry later |
| **Unauthorized (401)** | Refresh token | Yes (1x) | Auto-recover |

---

## 📝 Development Timeline (MVAI)

### Phase 0 (Feb 3-7): Foundation ✅
- ✅ Project scaffolding, configuration, models, documentation

### Phase 1 (Feb 10 — Feb 28): Core Implementation ✅
- ✅ `MovexInvoiceReader`, `MyInvoiceMapper`, all validators
- ✅ `MyInvoiceSubmitter` (OAuth, XAdES, retry, rate limiting)
- ✅ `DirectQueryDataSource` (Dapper+ODBC, ADR-013)
- ✅ 184+ unit tests, 5 integration tests, 5 E2E tests

### Sprint 6 (Mar 9): SQLite Migration ✅
- ✅ `AuditLogger` migrated from SQL Server to SQLite via EF Core 8 (ADR-014)
- ✅ 233 tests passing (100%)

### Sprint 7 / UAT (Mar 17-30): Go-Live Readiness 🔄
- 🔄 AP invoice SQL fix (Sprint 7 blocker)
- 🔄 Compliance validation, backup runbook, smoke tests
- 🔄 **MVAI go-live Mar 31** (extended per ADR-016)

---

## 🧪 Testing Strategy

### Unit Tests
- **Target**: ≥80% code coverage
- **Framework**: xUnit + FluentAssertions + Moq
- **Focus**: Validators, mappers, error handling

### Integration Tests
- **Target**: All critical paths
- **Approach**: TestServer + in-memory database (or sandbox)
- **Tests**:
  - Valid invoice submission
  - Duplicate detection
  - Invalid TIN handling
  - Currency validation
  - Batch processing (100 invoices)

### Load Tests
- **Month-end volume**: 1000 invoices
- **Tool**: JMeter or custom C# script
- **Success rate target**: ≥95%

---

## 📞 Support & Escalation

### On-Call Rotation
- **Weekday (9-17)**: Dev Team
- **After-hours**: IT Ops (server/infrastructure issues) + Dev On-Call

### Escalation Path
1. Check logs (Application Insights or file logs)
2. Query SQLite audit log: `sqlite3 data/audit.db "SELECT InvoiceNumber,Status,ErrorMessage FROM AuditLogs WHERE Status='Failed' ORDER BY Timestamp DESC LIMIT 20;"`
3. Check MyInvois API status (myinvois.hasil.gov.my)
4. Escalate to IT Manager (if > 50% failure rate)

### Common Issues
See [TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) for:
- Database connection errors
- MyInvois API errors (DS302, 429, 500)
- MOVEX invoice data issues
- Certificate problems

---

## 📚 Related Documentation

- [SETUP.md](docs/SETUP.md) — Detailed local setup guide
- [DEPLOYMENT.md](docs/DEPLOYMENT.md) — Production deployment runbook
- [TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) — Operational support guide
- [03-myinvois-requirements.md](ai/memory/03-myinvois-requirements.md) — MyInvois requirements and traceability (merged)
- [../.github/WORKSPACE_RULES.md](../../.github/WORKSPACE_RULES.md) — Workspace-wide standards

---

## ✅ Compliance Checklist

Before MVAI go-live, verify:

### Database & Configuration
- [ ] **Audit DB**: `./data/` directory exists with write permissions for service account; `audit.db` created on first startup
- [ ] **WAL mode**: Confirmed after first run (`PRAGMA journal_mode;` returns `wal`)
- [ ] **SQLite backup**: Daily backup script scheduled per DEPLOYMENT.md (7-year retention, ADR-014)
- [ ] **Secrets**: All User Secrets configured (MovexDb connection string, API keys, certificate)
- [ ] **Certificate**: Digital certificate purchased from recognized Malaysian CA
- [ ] **Certificate Storage**: Encrypted directory created (`C:\Certs\MyInvois`), EFS enabled
- [ ] **Certificate Password**: Stored securely in Windows Credential Manager or DPAPI
- [ ] **appsettings.json**: Certificate path and references configured correctly

### Connectivity & API Access
- [ ] **MOVEX DB2/AS400**: Connection tested, tables accessible (fpledg, fsledg, fgledg)
- [ ] **MyInvois Sandbox**: Credentials validated, test submission successful
- [ ] **Certificate with MyInvois**: Signature test passed in sandbox
- [ ] **MyInvois Portal**: Certificate registered (thumbprint/serial number)

### Testing & Validation
- [ ] **Unit Tests**: ≥80% coverage, all passing
- [ ] **Integration Tests**: Batch of 100 invoices passing
- [ ] **Certificate Test**: Signature generation and submission to sandbox ✓
- [ ] **Backup Test**: Disaster recovery tested and documented

### Operational Readiness
- [ ] **Documentation**: README, SETUP, DEPLOYMENT, TROUBLESHOOTING runbooks complete
- [ ] **Monitoring**: Certificate expiry monitoring script deployed
- [ ] **Backup Procedure**: Encrypted backup created and verified
- [ ] **Audit Trail**: Sample submissions logged to SQLite `audit.db`, WAL mode active
- [ ] **On-Call Team**: Briefed on certificate renewal procedures and escalation
- [ ] **Finance**: Approved budget, registered certificate with MyInvois

---

## 🔄 Release Versioning

**Format:** `MAJOR.MINOR-PHASE`

- `1.0-MVAI`: Initial MVAI release (this version)
- `1.1-Phase2`: Portal integration, resilience patterns
- `2.0-Production`: Cloud migration, advanced features

---

## 📄 License

Internal project for SRX Global. See [LICENSE](LICENSE) for details.

---

**Last Updated:** March 18, 2026
**Maintained By:** Development Team  
**Status:** ✅ Active Development  

---

**Questions?** Contact the Development Team or refer to related documentation.
