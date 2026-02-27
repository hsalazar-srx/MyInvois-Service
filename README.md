# MyInvois-Service — e-Invoicing Integration Platform

**Version:** 0.1-DESIGN  
**Status:** Phase 1 — Implementation In Progress (DB2 Direct Access per ADR-013)
**Target Go-Live:** MVAI (February 28, 2026)  
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
- Error handling and logging to SQL Server
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
- ✅ Audit logs stored in **SQL Server** (SRX_AuditLog database)
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
│   └── AuditLogger.cs                 ← Log to SQL Server
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
├── database/                          ← SQL Server schemas
│   ├── create-audit-table.sql
│   └── create-audit-views.sql
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
- **SQL Server 2019+** with database `SRX_AuditLog` created
- **IBM DB2 driver** (`Net.IBM.Data.Db2` NuGet package) for MOVEX AS400 access
- **MOVEX DB2/AS400** connection credentials (server, database, schemas: `mvxcdta`/`mvxc300`)
- **MyInvois Sandbox credentials** (ClientId, ClientSecret, TIN)
- **Windows domain account** for local development

### Local Setup (5 minutes)

#### 1. Clone & Restore

```bash
git clone https://github.com/SRX/MyInvois-Service.git
cd MyInvois-Service
dotnet restore
```

#### 2. Configure User Secrets

```powershell
# Set MOVEX DB2/AS400 connection
dotnet user-secrets set "MovexDb:ConnectionString" "Server=YOUR_AS400;Database=YOUR_DB;UserID=your-user;Password=your-password;"

# Set MyInvois credentials
dotnet user-secrets set "MyInvoisApi:ClientId" "your-client-id"
dotnet user-secrets set "MyInvoisApi:ClientSecret" "your-client-secret"

# Set SQL Server connection string
dotnet user-secrets set "ConnectionStrings:AuditLog" "Server=YOUR_SERVER;Database=SRX_AuditLog;Integrated Security=true;TrustServerCertificate=true;"
```

#### 3. Create Audit Log Database

```powershell
# Run SQL schema
sqlcmd -S YOUR_SERVER -i .\database\create-audit-table.sql -d SRX_AuditLog
sqlcmd -S YOUR_SERVER -i .\database\create-audit-views.sql -d SRX_AuditLog
```

#### 4. Run Locally

```bash
dotnet run --launch-profile Development
```

---

## 📊 Configuration

### appsettings.json Structure

```json
{
  "MovexDb": {
    "ConnectionString": "Server=YOUR_AS400;Database=YOUR_DB;",
    "SchemaData": "mvxcdta",
    "SchemaProgram": "mvxc300",
    "CommandTimeoutSeconds": 30,
    "DataSourceStrategy": "DirectQuery"
  },
  "MyInvoisApi": {
    "BaseUrl": "https://sandbox.myinvois.hasil.gov.my",
    "Environment": "sandbox"
  },
  "CertificateSettings": {
    "StoragePath": "C:\\Certs\\MyInvois\\myinvois-cert.pfx",
    "PasswordReference": "Credential:MyInvoisCert",
    "Thumbprint": "[VALUE FROM FINANCE DOCUMENTATION]",
    "ValidityCheckIntervalDays": 14
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

**Certificate Configuration Notes:**
- ⚠️ **DO NOT** put certificate password directly in `appsettings.json`
- Password must be retrieved from **Windows Credential Manager** or **DPAPI** (see SETUP.md)
- `Thumbprint` is documented for audit/verification purposes only
- `ValidityCheckIntervalDays=14` triggers monitoring alerts 14 days before expiry

All sensitive values stored in **User Secrets** (not committed to repo).

---

## 🔑 Key Features

### 1. MOVEX Integration (DB2 Direct Access — ADR-013)
- Query invoices directly from IBM DB2/AS400 tables (`fpledg`, `fsledg`, `fgledg`)
- DataAccess layer with strategy pattern (`DirectQueryDataSource` / `StoredProcedureDataSource`)
- Pluggable `IPartyDataProvider` interface for data retrieval
- Uses `Net.IBM.Data.Db2` + Dapper for efficient database access
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
- SQL Server [dbo].[AuditLog] table (standard schema)
- MyInvois-specific fields (UUID, status, error codes)
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
- **At Rest**: SQL Server TDE (Transparent Data Encryption)
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

### Week 1 (Feb 3-7): Foundation ✓
- ✅ Project scaffolding
- ✅ Configuration files
- ✅ SQL Server schema
- ✅ Models & DTOs
- ⏳ Next: Week 2

### Week 2 (Feb 10-14): Core Implementation
- [ ] Implement `MovexInvoiceReader`
- [ ] Implement `MyInvoiceMapper` + all validators
- [ ] Implement `MyInvoiceSubmitter` (OAuth, submission)
- [ ] Implement `AuditLogger`
- [ ] Unit tests (≥80% coverage)

### Week 3 (Feb 17-21): Integration Testing
- [ ] Integration tests (sandbox)
- [ ] Batch processing test (100+ invoices)
- [ ] Create runbook
- [ ] Performance tuning

### Week 4 (Feb 24-28): UAT & Go-Live
- [ ] UAT with Finance team
- [ ] MVAI dry-run
- [ ] **MVAI go-live** (Feb 28)
- [ ] Monitor + support

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
- **After-hours**: IT Ops (SQL Server issues) + Dev On-Call

### Escalation Path
1. Check logs (Application Insights or file logs)
2. Query [dbo].vw_MyInvois_FailedSubmissions
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
- [ ] **Database**: SRX_AuditLog created, TDE enabled
- [ ] **Schema**: [dbo].[AuditLog] created with all indexes
- [ ] **Secrets**: All User Secrets configured (API keys, connection strings, certificate)
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
- [ ] **Audit Trail**: Sample submissions logged to SQL Server
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

**Last Updated:** February 16, 2026
**Maintained By:** Development Team  
**Status:** ✅ Active Development  

---

**Questions?** Contact the Development Team or refer to related documentation.
