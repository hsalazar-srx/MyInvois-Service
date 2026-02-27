# MyInvois-Service - System Architecture

**Last Updated**: 2026-02-27
**Status**: MVAI Iteration 1 (Active)
**Version**: 1.1

## 🏗️ Architecture Overview

### High-Level Data Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    MOVEX (M3 ERP System)                        │
│             MOVEX Database (IBM DB2 on AS/400)                  │
└─────────────┬───────────────────────────────────────────────────┘
              │
              │ (DB2 Connection)
              │
┌─────────────▼───────────────────────────────────────────────────┐
│          fpledg/fsledg/fgledg Tables                            │
│    Schemas: mvxcdta (CMP100) / mvxc300 (CMP300)                │
└─────────────┬───────────────────────────────────────────────────┘
              │
              │ (SQL/Stored Procedure)
              │
┌─────────────▼───────────────────────────────────────────────────┐
│         MyInvois-Service (Worker Service, .NET 8.0)             │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │  1. MovexInvoiceReader                                      ││
│  │     └─ Fetches invoices from MOVEX database (DB2)          ││
│  │                                                              ││
│  │  2. MyInvoiceMapper                                         ││
│  │     └─ Transforms to MyInvois schema (UBL 2.1)             ││
│  │     └─ Orchestrates validation                             ││
│  │                                                              ││
│  │  3. Validators (5 classes)                                 ││
│  │     ├─ MandatoryFieldsValidator                           ││
│  │     ├─ TINValidator                                        ││
│  │     ├─ DateValidator                                       ││
│  │     ├─ CurrencyValidator                                   ││
│  │     └─ TotalsValidator                                     ││
│  │                                                              ││
│  │  4. MyInvoiceSubmitter                                      ││
│  │     ├─ OAuth token management                              ││
│  │     ├─ XAdES signature generation                          ││
│  │     └─ MyInvois API submission                             ││
│  │                                                              ││
│  │  5. InvoiceProcessor (Orchestrator)                         ││
│  │     └─ Coordinates batch processing                        ││
│  │                                                              ││
│  │  6. AuditLogger                                             ││
│  │     └─ Logs all submissions to SQL Server                  ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────┬───────────────────────────────────────────────────┘
              │
              ├─────────────────┐
              │                 │
              │                 │ (HTTPS REST)
              │                 │
    (SQL)     │       ┌─────────▼─────────────┐
    ▼         │       │  MyInvois API         │
   ┌──────────┴──────┐│  (LHDNM Platform)     │
   │  SQL Server     │└───────────────────────┘
   │  SRX_AuditLog   │
   │  dbo.AuditLog   │
   │  (7-year        │
   │   retention)    │
   └─────────────────┘
```

---

## 🧩 Skills-Based Architecture Alignment

This project follows the centralized skills registry in `C:\Projects\.github\skills\manifest.json`.

### Skills Used

| Skill ID | Category | Usage |
|----------|----------|-------|
| `integration/m3-transaction-builder` | Integration | NO LONGER PRIMARY (ADR-013) |
| `integration/m3-response-parser` | Integration | NO LONGER PRIMARY (ADR-013) |
| `integration/movex-db2-data-source` | Integration | DB2 data access strategy pattern (DataAccess layer) |
| `architecture/resilience-patterns` | Architecture | Retry/backoff policies (MyInvoiceSubmitter) |
| `architecture/audit-logging-framework` | Architecture | SQL audit schema + retention (AuditLogger) |
| `architecture/configuration-management` | Architecture | Settings + secrets binding |
| `architecture/clean-architecture` | Architecture | Service boundaries + layering |
| `architecture/dotnet-api-design` | Architecture | Interface/DTO conventions |

### Proposed Skills (Gaps)

| Proposed Skill ID | Purpose |
|-------------------|---------|
| `integration/myinvois-document-builder` | MOVEX → UBL 2.1 transformation |
| `integration/myinvois-validator` | LHDNM validation rules |
| `integration/oauth-token-manager` | OAuth token caching/refresh |
| `integration/xades-signer` | XAdES v1.1 signing |
| `integration/api-rate-limiter` | 100 req/min enforcement |

**Skills Audit:** See [00-Skills Audit](00-skills-audit.md) for detailed mapping and gaps.

---

## 📦 Component Architecture

### 1. **MovexInvoiceReader** (Data Source Layer)

**Purpose**: Fetch invoices from MOVEX database (DB2 on AS/400) via IInvoiceDataSource + IPartyDataProvider

**Responsibilities**:
- Query MOVEX DB2 tables (fpledg, fsledg, fgledg)
- Map RawInvoiceRecord DTOs with party enrichment
- Handle DB2 connection errors with retries
- Convert MOVEX date/time format to ISO 8601

**Key Methods**:
```csharp
Task<List<MovexInvoice>> GetPendingInvoices(DateTime fromDate)
Task<MovexInvoice?> GetInvoiceById(string invoiceNumber)
Task<List<MovexInvoice>> GetInvoicesByDateRange(DateTime from, DateTime to)
```

**Configuration**:
- `MovexDbSettings.ConnectionString`: DB2 connection string (from User Secrets)
- `MovexDbSettings.DataSourceStrategy`: DirectQuery or StoredProcedure
- `MovexDbSettings.SchemaCmp100`: Schema for CMP100 (mvxcdta)
- `MovexDbSettings.SchemaCmp300`: Schema for CMP300 (mvxc300)
- `MovexDbSettings.PartyDataSource`: Placeholder or MovexMaster

---

### 2. **MyInvoiceMapper** (Transformation Layer)

**Purpose**: Transform MOVEX invoices to MyInvois schema

**Responsibilities**:
- Map MOVEX fields to MyInvois UBL 2.1 format
- Orchestrate all 5 validators
- Collect validation errors
- Calculate totals & taxes

**Key Methods**:
```csharp
MyInvoiceDocument Transform(MovexInvoice invoice)
bool ValidateDocument(MyInvoiceDocument document, out List<ValidationError> errors)
```

**Validation Pipeline**:
```
Transform → Mandatory Fields → TIN → Date → Currency → Totals → Result
```

**Error Handling**: 
- Invalid invoices NOT submitted (caught before API call)
- Validation errors collected in `ValidationErrors` list
- Errors logged for manual review

---

### 3. **Validators** (Validation Layer)

Five specialized validators, each handling one concern:

#### **MandatoryFieldsValidator**
- Checks all 20+ mandatory fields
- Validates field lengths & formats
- Ensures required fields are non-null

#### **TINValidator**
- Validates 12-digit TIN format
- Checks numeric-only constraint
- Optional: API lookup (cached 1 hour)

#### **DateValidator**
- Rejects placeholder dates ("N/A", "0000-00-00")
- Validates ISO 8601 format (YYYY-MM-DD)
- Ensures date is not in future
- Converts to UTC

#### **CurrencyValidator**
- Validates ISO 4217 currency codes (MYR, USD, SGD, etc.)
- Enforces exchange rate requirement (non-MYR currencies)
- Validates rate precision (max 6 decimal places)
- Rate > 0

#### **TotalsValidator**
- Verifies sum of line amounts = invoice total
- Checks tax calculations
- Validates: TotalInclTax = TotalExclTax + TotalTax
- Rounding tolerance: ±1 cent

---

### 4. **MyInvoiceSubmitter** (Integration Layer)

**Purpose**: Submit validated invoices to MyInvois API

**Responsibilities**:
- Manage OAuth tokens (1-hour cache)
- Generate XAdES v1.1 digital signatures
- Submit XML documents to MyInvois
- Handle rate limiting (100 req/min)
- Extract MyInvois UUID from response
- Classify errors (retriable vs. no-retry)

**Key Methods**:
```csharp
Task<SubmissionResult> Submit(MyInvoiceDocument document)
Task<string?> GetSubmissionStatus(string myInvoisUUID)
Task<string> GetAccessToken()
```

**API Endpoints**:
- `POST /connect/token` - OAuth token
- `POST /api/v1.0/documentsubmissions` - Submit invoice
- `GET /api/v1.0/documents/{uuid}/details` - Get status (Phase 2)

**Error Classification**:

| Error Code | Type | Action |
|-----------|------|--------|
| 400, 401, DS101, DS302 | No-Retry | Log + Manual review |
| 429, 500, 503 | Retriable | Auto-retry (3 attempts) |

---

### 5. **InvoiceProcessor** (Orchestrator)

**Purpose**: Coordinate end-to-end batch processing

**Responsibilities**:
- Schedule monthly batch (1st of month)
- Fetch pending invoices
- Transform to MyInvois format
- Submit in batches (respecting rate limits)
- Aggregate results

**Key Methods**:
```csharp
Task<BatchResult> ProcessMonthlyBatch(CancellationToken cancellationToken)
Task<SubmissionResult> ProcessSingleInvoice(string invoiceId)
Task<SubmissionResult> RetryFailedInvoice(string submissionId)
```

**Batch Configuration**:
- Sales: 1 batch of 100 invoices
- Purchase: 10-20 batches of 50 invoices
- Delay: 0.6s between batches (rate limit: 100 req/min)

**Execution Flow**:
```
1. Get pending invoices from MOVEX database
2. For each invoice:
   a. Transform to MyInvois format
   b. Validate (all 5 validators)
   c. If valid: submit to MyInvois
   d. If invalid: skip (log error for review)
3. For each batch of 50:
   a. Submit all
   b. Wait 0.6s
4. Log all submissions to audit log
5. Return BatchResult (summary)
```

---

### 6. **AuditLogger** (Compliance Layer)

**Purpose**: Log all submissions for audit trail & compliance

**Responsibilities**:
- Insert submission records to `dbo.AuditLog`
- Capture MyInvois UUID, status, errors
- Store request/response payloads
- Query failed submissions for retry
- Detect duplicate submissions

**Key Methods**:
```csharp
Task LogSubmission(SubmissionResult result, MyInvoiceDocument? document)
Task<List<SubmissionResult>> GetFailedSubmissions(int maxResults)
Task<bool> IsInvoiceAlreadySubmitted(string invoiceNumber)
```

**Audit Fields**:
- `InvoiceNumber`: MOVEX invoice ID
- `MyInvoisUUID`: MyInvois document UUID
- `MyInvoisStatus`: Submission status
- `ErrorMessage`: Error details
- `ValidationErrors`: JSON array of validation failures
- `Duration`: Time taken (ms)
- `Timestamp`: When submitted (UTC)

**Retention**: 7 years (SQL Server policy)

---

## 🔄 Data Flow (Monthly Batch)

```
1. SCHEDULER (midnight on 1st of month)
   └─> Triggers InvoiceProcessor.ProcessMonthlyBatch()

2. FETCH (MOVEX Reader via IInvoiceDataSource)
   └─> Query DB2: SELECT FROM {schema}.fpledg/fsledg WHERE epacdt >= @fromDate
       ├─ Status = "Open"
       ├─ FromDate = 1st of previous month
       └─ Returns: ~100 sales + 500-1000 purchase

3. TRANSFORM (Mapper + Validators)
   └─ For each invoice:
      ├─ Transform to UBL 2.1
      ├─ Run 5 validators
      ├─ If valid: prepare for submission
      └─ If invalid: log error, skip

4. SUBMIT (MyInvoiceSubmitter)
   └─ Sales batch (100 invoices):
      ├─ Get OAuth token
      ├─ Generate XAdES signature
      └─ POST /api/v1.0/documentsubmissions (all 100)
   
   └─ Purchase batches (50 invoices each):
      ├─ Batch 1: Submit + 0.6s delay
      ├─ Batch 2: Submit + 0.6s delay
      ├─ ... (up to 20 batches)
      └─ Batch N: Submit (no delay on last)

5. AUDIT (Logger)
   └─ For each submission:
      ├─ INSERT dbo.AuditLog
      ├─ Status: Success / Failed / Skipped
      ├─ MyInvoisUUID: From response
      └─ ErrorMessage: If any

6. RESULT
   └─ Return BatchResult:
      ├─ TotalInvoices: ~600-1100
      ├─ SuccessCount: ~570-1045
      ├─ FailedCount: ~10-20
      ├─ SkippedCount: ~20-30 (invalid)
      └─ SuccessRate: ≥95%
```

---

## 🔐 Security Considerations

### Authentication & Authorization

1. **MOVEX Database (DB2)**
   - Connection string from User Secrets (not hardcoded)
   - DB2 connection encryption enabled
   - Read-only access to MOVEX schemas

2. **MyInvois API**
   - OAuth 2.0 (Client Credentials flow)
   - Credentials from User Secrets (not hardcoded)
   - Token caching (1-hour TTL)
   - Rate limiting: 100 req/min (enforced by MyInvois)

### Data Protection

1. **In Transit**
   - MyInvois API calls over HTTPS (TLS 1.3 minimum)
   - DB2 connections encrypted

2. **At Rest**
   - SQL Server audit logs encrypted (TDE)
   - Sensitive fields masked in logs (OAuth tokens, certificates)
   - 7-year retention with archive to cold storage (Phase 2)

3. **Digital Signature**
   - XAdES v1.1 format
   - Certificate issued by trusted CA
   - MyInvois SDK handles cryptography (no custom implementation)

---

## 📊 Data Model (Simplified)

### Input: MovexInvoice (from MOVEX Database)
```csharp
public class MovexInvoice
{
    public string InvoiceNumber { get; set; }
    public string InvoiceDate { get; set; }
    public string InvoiceType { get; set; } // Sales/Purchase
    public string CompanyCode { get; set; }
    public string VoucherNumber { get; set; }
    public string CurrencyCode { get; set; }
    public decimal ExchangeRate { get; set; }
    public decimal TotalExclTax { get; set; }
    public decimal TotalTax { get; set; }
    public InvoiceParty Supplier { get; set; }
    public InvoiceParty Buyer { get; set; }
    public List<InvoiceLine> Lines { get; set; }
}
```

### Processing: MyInvoiceDocument (Transformed to UBL 2.1)
```csharp
public class MyInvoiceDocument
{
    public string Id { get; set; } // Internal GUID
    public string InvoiceNumber { get; set; }
    public string IssueDate { get; set; } // YYYY-MM-DD
    public string IssueTime { get; set; } // HH:MM:SS
    public string CurrencyCode { get; set; }
    public string SupplierTIN { get; set; }
    public string SupplierName { get; set; }
    public string SupplierBRN { get; set; }
    public string BuyerName { get; set; }
    public string? BuyerTIN { get; set; }
    public decimal TotalExclTax { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalInclTax { get; set; }
    public List<MyInvoiceLine> Lines { get; set; }
    public string? Signature { get; set; } // XAdES v1.1
    public List<ValidationError> ValidationErrors { get; set; }
}
```

### Output: SubmissionResult (to Audit Log)
```csharp
public class SubmissionResult
{
    public string SubmissionId { get; set; }
    public string InvoiceNumber { get; set; }
    public string Status { get; set; } // Success/Failed/Skipped
    public string? MyInvoisUUID { get; set; }
    public string? MyInvoisStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime SubmittedAt { get; set; }
}
```

---

## ⚙️ Configuration & Deployment

### Appsettings.json

```json
{
  "MovexDb": {
    "ConnectionString": "{from User Secrets}",
    "DataSourceStrategy": "DirectQuery",
    "SchemaCmp100": "mvxcdta",
    "SchemaCmp300": "mvxc300",
    "ActiveCompanyCodes": ["100", "300"],
    "CommandTimeoutSeconds": 60,
    "MaxPoolSize": 10,
    "PartyDataSource": "Placeholder",
    "ApInvoiceStoredProc": "",
    "ArInvoiceStoredProc": "",
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
    "BaseUrl": "https://api.myinvois.hasil.gov.my",
    "Environment": "sandbox", // or "production"
    "ClientId": "{from User Secrets}",
    "ClientSecret": "{from User Secrets}",
    "TIN": "000000000000"
  },
  "BatchProcessing": {
    "SalesBatchSize": 100,
    "PurchaseBatchSize": 50,
    "DelayBetweenBatchesMs": 600,
    "MaxRetries": 3,
    "RetryDelaySeconds": 5
  },
  "Processing": {
    "EnableBatchProcessing": true,
    "MonthlySubmissionDay": 1,
    "SubmissionWindowHours": 2
  },
  "Validation": {
    "TINCache_TTL_Hours": 1,
    "RequireExchangeRateForNonMYR": true,
    "AllowNullBuyerTIN": true,
    "DecimalPlaces": 2
  }
}
```

### Deployment Targets

| Environment | Purpose | Host | Database |
|-------------|---------|------|----------|
| **Staging** | Pre-prod testing | SRXAPP-STAGING | SRX_AuditLog (staging) |
| **Production** | Live submission | SRXAPP-PROD | SRX_AuditLog (prod) |

---

## 🚀 Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| **Runtime** | .NET Core | 8.0 |
| **Language** | C# | 12 |
| **DB2 Driver** | Net.IBM.Data.Db2 | Latest |
| **ORM** | Dapper | Latest |
| **JSON** | System.Text.Json | .NET 8.0 |
| **Crypto** | MyInvois SDK | Latest |
| **Database** | SQL Server | 2019+ |
| **ORM** | Entity Framework Core | 8.0 |
| **Logging** | Serilog | 4.0+ |
| **Testing** | xUnit + Moq | Latest |

---

## 📈 Scalability Considerations

### Phase 1 (Current)
- Monthly batch: ~600-1100 invoices
- Single instance execution
- In-memory caching (OAuth tokens)
- No distributed tracing

### Phase 2 (Future)
- Daily/hourly batches (if needed)
- Cloud-ready (Azure Functions)
- Redis cache for scalability
- Distributed tracing (Application Insights)
- Event-driven architecture

---

## 🔗 Related Documents

- [00-Product Vision](00-product-vision.md) - Vision & objectives
- [02-Data Model](02-data-model.md) - Database schema details
- [03-MyInvois Requirements](03-myinvois-requirements.md) - Validation rules
- [04-API Integration](04-api-integration.md) - API specs & integration details
- [05-Deployment Guide](05-deployment-guide.md) - Deployment instructions

---

**Owner**: Architecture Team  
**Last Review**: 2026-02-16
**Next Review**: 2026-03-16

