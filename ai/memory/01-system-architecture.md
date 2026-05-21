# MyInvois-Service - System Architecture

**Last Updated**: 2026-05-21
**Status**: Sprint 9 (Active)
**Version**: 2.0

## Architecture Overview

### High-Level Data Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    MOVEX (M3 ERP System)                        │
│             MOVEX Database (IBM DB2 on AS/400)                  │
└─────────────┬───────────────────────────────────────────────────┘
              │
              │ (ODBC / System.Data.Odbc)
              │
┌─────────────▼───────────────────────────────────────────────────┐
│   DB Tables: fpledg / fsledg / OINVOH / ODLINE / MITMAS        │
│   Schemas: mvxcdta (CMP100-Prod) / mvxc300 (CMP300-Dev/UAT)   │
└─────────────┬───────────────────────────────────────────────────┘
              │
              │ (DirectQueryDataSource + Dapper)
              │
┌─────────────▼───────────────────────────────────────────────────┐
│         MyInvois-Service (ASP.NET Core Worker, .NET 8.0)        │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │  1. DirectQueryDataSource (IInvoiceDataSource)              ││
│  │     └─ Fetches RawInvoiceRecord + lines via ODBC+Dapper    ││
│  │     └─ AR join path: FSLEDG→OINVOH→ODLINE                 ││
│  │     └─ AP: fpledg direct query                             ││
│  │                                                              ││
│  │  2. MovexLineItemFetcher                                    ││
│  │     └─ Batch-fetches line items for a date range           ││
│  │     └─ Separate from header fetch for performance          ││
│  │                                                              ││
│  │  3. MyInvoisMapper (IMyInvoisMapper)                        ││
│  │     └─ Transforms MovexInvoice → MyInvoiceDocument         ││
│  │     └─ AR (type "Sales") → DocumentTypeCode "01"           ││
│  │     └─ AP (type "Purchase") → DocumentTypeCode "11"        ││
│  │     └─ Orchestrates 5 validators                           ││
│  │                                                              ││
│  │  4. Validators (5 classes)                                  ││
│  │     ├─ MandatoryFieldsValidator                            ││
│  │     ├─ TINValidator                                         ││
│  │     ├─ DateValidator                                        ││
│  │     ├─ CurrencyValidator                                    ││
│  │     └─ TotalsValidator                                      ││
│  │                                                              ││
│  │  5. MyInvoisTokenService (IMyInvoisTokenService)            ││
│  │     └─ OAuth 2.0 client credentials flow                   ││
│  │     └─ Token caching (1-hour TTL)                          ││
│  │     └─ Endpoint: preprod-api.myinvois.hasil.gov.my         ││
│  │                                                              ││
│  │  6. MyInvoiceSubmitter                                      ││
│  │     └─ XAdES digital signature (custom, not LHDN SDK)      ││
│  │     └─ Submits to LHDN API via Polly retry (3 attempts)    ││
│  │     └─ Reads acceptedDocuments[].uuid from response        ││
│  │     └─ retrySleepProvider: injectable for tests            ││
│  │                                                              ││
│  │  7. InvoiceProcessor (Orchestrator)                         ││
│  │     └─ ProcessDateRangeBatch(from, to, ct)                 ││
│  │     └─ Returns BatchResult with counts                      ││
│  │                                                              ││
│  │  8. DailyBatchHostedService (BackgroundService)            ││
│  │     └─ Fires ProcessDateRangeBatch at configured time       ││
│  │     └─ Controlled by BatchScheduler:Enabled                ││
│  │                                                              ││
│  │  9. AuditLogger                                             ││
│  │     └─ Persists to SQLite via EF Core 8 (WAL mode)        ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────┬───────────────────────────────────────────────────┘
              │
              ├─────────────────┐
              │                 │
              │                 │ (HTTPS REST)
              │                 │
    (SQLite)  │       ┌─────────▼─────────────┐
    ▼         │       │  MyInvois API         │
   ┌──────────┴──────┐│  (LHDNM Platform)     │
   │  SQLite         │└───────────────────────┘
   │  audit.db       │  preprod-api.myinvois
   │  (WAL mode)     │  .hasil.gov.my
   │  7-year         │
   │  retention      │
   └─────────────────┘
```

---

## Skills-Based Architecture Alignment

This project follows the centralized skills registry in `C:\Projects\.github\skills\manifest.json`.

### Skills Used

| Skill ID | Category | Usage |
|----------|----------|-------|
| `integration/movex-db2-data-source` | Integration | DB2 direct access via System.Data.Odbc + Dapper |
| `integration/myinvois-validator` | Integration | 5 validator classes, LHDN spec compliance |
| `architecture/resilience-patterns` | Architecture | Polly retry + circuit breaker (MyInvoiceSubmitter) |
| `architecture/audit-logging-framework` | Architecture | SQLite audit schema + 7-year retention (ADR-014) |
| `architecture/configuration-management` | Architecture | BatchSchedulerSettings, MovexDbSettings, MyInvoisApiSettings |
| `architecture/clean-architecture` | Architecture | Service boundaries + layering |
| `architecture/dotnet-api-design` | Architecture | Interface/DTO conventions |

---

## Component Architecture

### 1. **DirectQueryDataSource** (Data Source Layer)

**Purpose**: Fetch invoice headers and lines from MOVEX DB2 via ODBC

**Responsibilities**:
- AP header query: `fpledg` (schema = mvxcdta/mvxc300 per `ActiveCompanyCodes`)
- AR header query: `fsledg → OINVOH → ODLINE` join path (ADR-016; 96% coverage)
- Batch line item fetch via `MovexLineItemFetcher`
- Convert MOVEX numeric date fields (YYYYMMDD integer) to ISO 8601

**AR line item join path** (ADR-016, 2026-04-02):
```
FSLEDG → OINVOH (ESVONO = UHVONO) → ODLINE (UHIVNO = UBIVNO)
```
Do NOT use OINVOL — `OIIVNO` column does not exist in IBM i 7.4.

**Configuration**:
- `MovexDb.ConnectionString`: ODBC DSN (from User Secrets)
- `MovexDb.ActiveCompanyCodes`: `["100"]` prod, `["100"]` UAT (never `["300"]` in production)
- `MovexDb.ArMinYear`: Minimum AR invoice year filter (2025 in dev)

---

### 2. **MovexLineItemFetcher** (Data Source Layer)

**Purpose**: Batch-fetch line items for a date range, separate from header queries

**Responsibilities**:
- Fetches `ODLINE` records for all voucher numbers in a date range
- Builds line item dictionary keyed by voucher number
- Called by `DirectQueryDataSource.GetInvoicesByDateRangeAsync`

---

### 3. **MyInvoisMapper** (Transformation Layer)

**Purpose**: Transform `MovexInvoice` DTOs to `MyInvoiceDocument` (UBL 2.1 subset)

**Responsibilities**:
- Map MOVEX fields to MyInvois schema
- AR (InvoiceType = "Sales") → `DocumentTypeCode = "01"`, our company = Supplier
- AP (InvoiceType = "Purchase") → `DocumentTypeCode = "11"` (self-billed), external vendor = Supplier, our company = Buyer
- Orchestrate all 5 validators (non-fail-fast, collect all errors)
- Date normalization: MOVEX YYYYMMDD → ISO `yyyy-MM-dd` / `HH:mm:ss`

**Key Method**:
```csharp
MyInvoiceDocument Transform(MovexInvoice invoice)
```

**Validation Pipeline**:
```
Transform → MandatoryFields → TIN → Date → Currency → Totals → Result
```

---

### 4. **Validators** (Validation Layer)

Five specialized validators, each handling one concern:

| Validator | Key Rules |
|-----------|-----------|
| `MandatoryFieldsValidator` | 20+ mandatory fields, non-null/non-empty |
| `TINValidator` | 12-digit Malaysian TIN format |
| `DateValidator` | ISO 8601, rejects "0000-00-00" / "N/A", not in future |
| `CurrencyValidator` | ISO 4217, exchange rate required for non-MYR |
| `TotalsValidator` | `TotalInclTax = TotalExclTax + TotalTax`, ±1 cent tolerance |

---

### 5. **MyInvoisTokenService** (Authentication Layer)

**Purpose**: Manage OAuth 2.0 tokens for LHDN API access

**Responsibilities**:
- Client credentials flow (`/connect/token`)
- Token caching with 1-hour TTL (expires-at based, not timer)
- Single endpoint for both token and API: `preprod-api.myinvois.hasil.gov.my`
- Thread-safe (SemaphoreSlim for refresh)

**Key Interface**:
```csharp
Task<string> GetAccessTokenAsync()
```

---

### 6. **MyInvoiceSubmitter** (Integration Layer)

**Purpose**: Submit validated and signed invoices to LHDN MyInvois API

**Responsibilities**:
- XAdES digital signature (custom implementation — LHDN SDK not used)
- Decimal normalization via `DecimalNormalizer` JsonConverter (LHDN strips trailing zeros on re-parse)
- Polly retry policy: 3 attempts, exponential backoff (5s × 2^attempt)
- Read UUID from `acceptedDocuments[0].uuid` in response envelope
- Optional `retrySleepProvider` constructor parameter (test injection)

**LHDN Response Envelope**:
```json
{
  "submissionUid": "SUB-2026-00001",
  "acceptedDocuments": [{ "uuid": "...", "invoiceCodeNumber": "..." }],
  "rejectedDocuments": []
}
```

**Error Classification**:

| Error Code | Type | Action |
|-----------|------|--------|
| 400, 401, DS101, DS302 | No-Retry | Log + manual review |
| 429, 500, 503 | Retriable | Auto-retry (3 attempts, exponential backoff) |

---

### 7. **InvoiceProcessor** (Orchestrator)

**Purpose**: Coordinate end-to-end batch processing for a date range

**Key Method**:
```csharp
Task<BatchResult> ProcessDateRangeBatch(DateTime from, DateTime to, CancellationToken ct)
```

**Execution Flow**:
```
1. GetInvoicesByDateRangeAsync(from, to)
2. For each invoice:
   a. Transform via MyInvoisMapper
   b. Run 5 validators
   c. If valid: Submit via MyInvoiceSubmitter
   d. If invalid: Log as Skipped
3. Log each result via AuditLogger
4. Return BatchResult { TotalInvoices, SuccessCount, FailedCount, SkippedCount }
```

---

### 8. **DailyBatchHostedService** (Scheduling Layer)

**Purpose**: Trigger nightly batch processing at a configured time

**Controlled by** `BatchScheduler` section in `src/MyInvois.Api/appsettings.json`:
```json
{
  "BatchScheduler": {
    "Enabled": true,
    "DailyRunHour": 2,
    "DailyRunMinute": 0,
    "LookbackDays": 1
  }
}
```

Setting `Enabled: false` disables the scheduled run entirely. Manual triggering is always available via `POST /api/v1/batch/process-range` regardless of this setting.

---

### 9. **AuditLogger** (Compliance Layer)

**Purpose**: Persist all submission results for compliance and retry tracking

**Storage**: SQLite via EF Core 8, WAL mode, file path from `ConnectionStrings:AuditLog` (ADR-014)

**Key Methods**:
```csharp
Task LogSubmissionAsync(SubmissionResult result)
Task<bool> IsAlreadySubmittedAsync(string invoiceNumber)
Task<List<SubmissionResult>> GetFailedSubmissionsAsync(int maxResults)
```

**Retention**: 7 years

---

## Configuration Architecture

Configuration is split across three files by concern:

| File | Scope | Contains |
|------|-------|----------|
| `appsettings.json` (root) | Service defaults | `Logging`, `ConnectionStrings`, `MovexDb`, `MyInvoisApi`, `Companies`, `ForeignPartyDefaults` |
| `appsettings.Development.json` | Dev overrides only | Debug log level, dev DB path, ArMinYear=2025, preprod API URL |
| `src/MyInvois.Api/appsettings.json` | Host-layer only | `AllowedHosts`, `ApiKeys`, `BatchScheduler` |

**Dead config sections (removed)**: `BatchProcessing`, `Processing`, `Validation` — these were never registered in DI. `BatchScheduler` is the only real scheduler config.

### Root appsettings.json (service defaults)
```json
{
  "MovexDb": {
    "ConnectionString": "{from User Secrets}",
    "DataSourceStrategy": "DirectQuery",
    "SchemaCmp100": "mvxcdta",
    "SchemaCmp300": "mvxc300",
    "ActiveCompanyCodes": ["100"],
    "CommandTimeoutSeconds": 60,
    "ArMinYear": 0
  },
  "MyInvoisApi": {
    "BaseUrl": "https://api.myinvois.hasil.gov.my",
    "IdentityBaseUrl": "https://api.myinvois.hasil.gov.my",
    "Environment": "production",
    "ClientId": "{from User Secrets}",
    "ClientSecret": "{from User Secrets}",
    "TIN": "{from User Secrets}"
  },
  "Companies": [
    {
      "Code": "100",
      "TIN": "{from User Secrets}",
      "BRN": "{from User Secrets}",
      "Name": "Scanfil APAC Sdn Bhd",
      "Phone": "6072319006"
    }
  ]
}
```

---

## Security Considerations

### Authentication & Authorization

1. **MOVEX Database (DB2)**
   - ODBC connection string from User Secrets (never hardcoded)
   - Read-only access to MOVEX schemas
   - CONO isolation: `mvxcdta` (CMP100) or `mvxc300` (CMP300) — never hardcoded

2. **MyInvois API**
   - OAuth 2.0 (Client Credentials flow) via `MyInvoisTokenService`
   - Credentials from User Secrets (dev) / Azure Key Vault (prod)
   - Token caching (1-hour TTL)

3. **BatchController API**
   - API Key authentication (`ApiKeys:Primary` / `ApiKeys:Admin`)
   - Keys from User Secrets / Azure Key Vault

### Digital Signature (XAdES)
- Custom XAdES v1.1 implementation (not LHDN SDK)
- Signature rules (all bugs resolved 2026-05-13):
  1. `docDigest = SHA256(Minify(invoice body with NO UBLExtensions AND NO Signature))`
  2. `propsDigest = SHA256(Minify(full QualifyingProperties object))`
  3. UBLExtensions inserted before Signature element
  4. `DecimalNormalizer` JsonConverter strips trailing zeros before signing (LHDN re-serializes)

---

## Data Model (Simplified)

### Input: MovexInvoice (from MOVEX)
```csharp
public class MovexInvoice
{
    public string InvoiceNumber { get; set; }
    public string InvoiceDate { get; set; }     // YYYYMMDD format
    public string InvoiceType { get; set; }     // "Sales" or "Purchase"
    public string CompanyCode { get; set; }
    public string VoucherNumber { get; set; }
    public string CurrencyCode { get; set; }
    public decimal ExchangeRate { get; set; }
    public decimal TotalExclTax { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalInclTax { get; set; }
    public InvoiceParty? Supplier { get; set; }
    public InvoiceParty? Buyer { get; set; }
    public List<InvoiceLine> Lines { get; set; }
}
```

### Processing: MyInvoiceDocument (UBL 2.1 subset)
```csharp
public class MyInvoiceDocument
{
    public string InvoiceNumber { get; set; }
    public string DocumentTypeCode { get; set; }  // "01" AR, "11" AP
    public string IssueDate { get; set; }          // yyyy-MM-dd
    public string IssueTime { get; set; }          // HH:mm:ss
    public string CurrencyCode { get; set; }
    public string SupplierTIN { get; set; }
    public string SupplierName { get; set; }
    public string SupplierBRN { get; set; }
    public string BuyerTIN { get; set; }
    public string BuyerName { get; set; }
    public decimal TotalExclTax { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalInclTax { get; set; }
    public decimal PayableAmount { get; set; }
    public List<MyInvoiceLine> Lines { get; set; }
    public List<ValidationError> ValidationErrors { get; set; }
}
```

### Output: SubmissionResult (to Audit Log)
```csharp
public class SubmissionResult
{
    public string InvoiceNumber { get; set; }
    public string Status { get; set; }          // "Success" / "Failed" / "Skipped"
    public string? MyInvoisUUID { get; set; }
    public string? MyInvoisStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime SubmittedAt { get; set; }
    public long DurationMs { get; set; }
}
```

---

## Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| **Runtime** | .NET | 8.0 |
| **Language** | C# | 12 |
| **DB2 Driver** | System.Data.Odbc | 9.0.2 |
| **ORM** | Dapper | Latest |
| **JSON** | System.Text.Json | .NET 8.0 |
| **Resilience** | Polly | 8.x |
| **Audit DB** | SQLite (EF Core 8) | WAL mode |
| **Logging** | Serilog | 4.0+ |
| **Testing** | xUnit + Moq + FluentAssertions | Latest |

---

## Scalability Considerations

### Phase 1 (Current)
- Daily batch: ~100 AR + 500-1000 AP invoices/month
- Single instance execution on SRXWEBAPP1
- In-memory token caching (1-hour TTL)
- SQLite audit storage (WAL mode, sufficient for single-instance)

### Phase 2 (Future)
- Real-time submission via SM-Portal trigger
- Azure Key Vault for credential management
- Distributed tracing (Application Insights)

---

## Related Documents

- [02-Data Model](02-data-model.md) - Database schema details
- [03-MyInvois Requirements](03-myinvois-requirements.md) - Validation rules
- [04-API Integration](04-api-integration.md) - API specs & integration details
- [09-Implementation Decisions](09-implementation-decisions.md) - ADR log
- [10-Testing Strategy](10-testing-strategy.md) - Test structure and coverage

---

**Owner**: Hector Salazar (Development & Integration Lead)
**Last Review**: 2026-05-21
**Next Review**: 2026-06-30
