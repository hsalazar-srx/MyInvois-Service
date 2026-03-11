# MyInvois-Service - Data Model & Database Schema

**Last Updated**: 2026-03-10
**Status**: MVAI Iteration 1 (Active)
**Version**: 1.1

---

## 📊 SQL Server Schema (Audit Log Database)

### Database: `SRX_AuditLog`

All MyInvois submissions logged to a single, centralized audit table with MyInvois-specific columns.

### Table: `dbo.AuditLog`

**Purpose**: Complete submission audit trail for compliance, reporting, and troubleshooting

**Schema**:

```sql
CREATE TABLE [dbo].[AuditLog] (
    -- Primary Key & Timestamp
    [AuditId]               UNIQUEIDENTIFIER    PRIMARY KEY DEFAULT NEWID(),
    [Timestamp]             DATETIME2(7)        NOT NULL DEFAULT SYSUTCDATETIME(),
    
    -- Who (Actor)
    [UserId]                NVARCHAR(100)       NULL,
    [UserRole]              NVARCHAR(50)        NULL,
    [IpAddress]             NVARCHAR(45)        NULL,
    
    -- What (Action)
    [Action]                NVARCHAR(100)       NOT NULL, -- "MyInvois_Submit"
    [Category]              NVARCHAR(50)        NOT NULL, -- "MyInvois"
    [Severity]              NVARCHAR(20)        NOT NULL, -- Info/Warning/Error/Critical
    
    -- Where (Resource)
    [ResourceType]          NVARCHAR(50)        NOT NULL, -- "Invoice"
    [ResourceId]            NVARCHAR(100)       NOT NULL, -- Invoice number
    [Endpoint]              NVARCHAR(500)       NULL,     -- API endpoint
    
    -- Result
    [Status]                NVARCHAR(20)        NOT NULL, -- Success/Failed/Pending
    [StatusCode]            NVARCHAR(50)        NULL,     -- HTTP code or error code
    [ErrorMessage]          NVARCHAR(MAX)       NULL,     -- Error details
    
    -- Payload (for forensics)
    [RequestPayload]        NVARCHAR(MAX)       NULL,     -- Document XML
    [ResponsePayload]       NVARCHAR(MAX)       NULL,     -- MyInvois API response
    
    -- MyInvois-Specific Fields
    [MyInvoisUUID]          NVARCHAR(100)       NULL,     -- MyInvois document UUID
    [MyInvoisStatus]        NVARCHAR(50)        NULL,     -- Valid/Invalid/Submitted/Cancelled/Rejected
    [MyInvoisSubmissionId]  NVARCHAR(100)       NULL,     -- MyInvois submission reference
    
    -- MOVEX Invoice Tracking
    [InvoiceNumber]         NVARCHAR(50)        NULL,     -- MOVEX invoice ID
    [InvoiceDate]           DATE                NULL,     -- OINVOH.IVDT
    [InvoiceType]           NVARCHAR(20)        NULL,     -- Sales/Purchase
    
    -- Financial Tracking
    [TotalAmount]           DECIMAL(18,2)       NULL,     -- Invoice total (incl tax)
    [TotalTax]              DECIMAL(18,2)       NULL,     -- Tax amount
    [CurrencyCode]          NCHAR(3)            NULL,     -- MYR/USD/SGD
    [ExchangeRate]          DECIMAL(18,6)       NULL,     -- Exchange rate (if applicable)
    
    -- Validation Tracking
    [ValidationErrors]      NVARCHAR(MAX)       NULL,     -- JSON array of errors
    [SubmissionBatchId]     UNIQUEIDENTIFIER    NULL,     -- Groups invoices submitted together
    
    -- Metadata
    [CorrelationId]         UNIQUEIDENTIFIER    NULL,     -- For distributed tracing
    [Duration]              INT                 NULL,     -- Execution time (ms)
    [RetryCount]            INT                 NOT NULL DEFAULT 0,
    
    -- Constraints
    CONSTRAINT [CK_AuditLog_Status] CHECK ([Status] IN ('Success', 'Failed', 'Pending', 'Cancelled')),
    CONSTRAINT [CK_AuditLog_Severity] CHECK ([Severity] IN ('Info', 'Warning', 'Error', 'Critical'))
);
```

### Indexes

```sql
-- Timestamp (for time-range queries)
CREATE NONCLUSTERED INDEX [IX_AuditLog_Timestamp] 
    ON [dbo].[AuditLog] ([Timestamp] DESC);

-- User tracking
CREATE NONCLUSTERED INDEX [IX_AuditLog_User] 
    ON [dbo].[AuditLog] ([UserId], [Timestamp] DESC);

-- Resource lookup
CREATE NONCLUSTERED INDEX [IX_AuditLog_Resource] 
    ON [dbo].[AuditLog] ([ResourceType], [ResourceId], [Timestamp] DESC);

-- Status filtering
CREATE NONCLUSTERED INDEX [IX_AuditLog_Status] 
    ON [dbo].[AuditLog] ([Status], [Timestamp] DESC) 
    WHERE [Status] <> 'Success';

-- Action filtering
CREATE NONCLUSTERED INDEX [IX_AuditLog_Action] 
    ON [dbo].[AuditLog] ([Action], [Timestamp] DESC);

-- MyInvois UUID lookup
CREATE NONCLUSTERED INDEX [IX_AuditLog_MyInvoisUUID] 
    ON [dbo].[AuditLog] ([MyInvoisUUID], [Timestamp] DESC)
    WHERE [MyInvoisUUID] IS NOT NULL;

-- Invoice number lookup (duplicate detection)
CREATE NONCLUSTERED INDEX [IX_AuditLog_InvoiceNumber] 
    ON [dbo].[AuditLog] ([InvoiceNumber], [Timestamp] DESC)
    WHERE [InvoiceNumber] IS NOT NULL;

-- Batch tracking
CREATE NONCLUSTERED INDEX [IX_AuditLog_SubmissionBatch] 
    ON [dbo].[AuditLog] ([SubmissionBatchId], [Timestamp] DESC)
    WHERE [SubmissionBatchId] IS NOT NULL;

-- Status monitoring (failed/pending)
CREATE NONCLUSTERED INDEX [IX_AuditLog_MyInvoisStatus] 
    ON [dbo].[AuditLog] ([MyInvoisStatus], [Timestamp] DESC)
    WHERE [MyInvoisStatus] IN ('Failed', 'Invalid', 'Pending');
```

---

## 📋 Audit Log Views

### View: `dbo.vw_MyInvois_FailedSubmissions`

**Purpose**: Identify invoices that failed submission (manual retry dashboard)

**Query**: Returns failed submissions from last 7 days with retry count < 3

**Columns**:
- InvoiceNumber
- InvoiceDate
- TotalAmount
- Status
- ErrorMessage
- ValidationErrors (JSON)
- RetryCount
- HoursSinceFailed

### View: `dbo.vw_MyInvois_MonthlySummary`

**Purpose**: Monthly batch processing metrics

**Columns**:
- Year, Month, InvoiceType
- TotalInvoices, SuccessCount, FailedCount, PendingCount
- SuccessRate (%)
- TotalAmount
- AvgDurationMs
- LastSubmission (timestamp)

### View: `dbo.vw_MyInvois_DuplicateSubmissions`

**Purpose**: Detect duplicate submissions (same invoice submitted multiple times)

**Columns**:
- InvoiceNumber
- SubmissionCount
- FirstSubmission, LastSubmission
- SuccessfulSubmissions
- MyInvoisUUIDs (comma-separated)

### View: `dbo.vw_MyInvois_ErrorFrequency`

**Purpose**: Error trend analysis

**Columns**:
- ErrorMessage
- StatusCode
- Frequency
- FirstOccurrence, LastOccurrence
- HoursSinceLast

### View: `dbo.vw_MyInvois_BatchMetrics`

**Purpose**: Per-batch performance metrics

**Columns**:
- SubmissionBatchId
- InvoicesInBatch
- SuccessCount, FailedCount
- SuccessRate (%)
- AvgDurationMs, MaxDurationMs
- BatchStartTime, BatchEndTime

---

## 🔄 .NET Data Models

### Raw Data Model: `RawInvoiceRecord` (DB2 → C#)

**Namespace**: `MyInvois.Service.DataAccess`
**Purpose**: Flat DTO mapping DB2 result set columns before party enrichment

**AR query structure** (CHG-007, aligned with `Actual_MOVEX_AR.sql`):
- **Tables**: `FSLEDG f` → INNER JOIN `OCUSMA o` (customer master) → LEFT JOIN `OCUSAD a` (customer address, ADID='INV01')
- **Invoice number**: `ESCINO` (not ESIVNO)
- **Date field**: `ESRGDT` (entry date, not ESACDT accounting date)
- **Filters**: Division, TransCode, CustomerStatus, MinYear (all configurable via `MovexDbSettings`)
- **Customer data**: Name, MasterAddress1-4, InvoiceeName, Address1-4, PostCode
- **GL join**: AP only (FGLEDG); AR does not join GL

**AP query structure** (unchanged):
- **Tables**: `FPLEDG p` → LEFT JOIN `FGLEDG g` (GL ledger on voucher)
- **Invoice number**: `EPSINO`
- **Date field**: `EPACDT` (accounting date)

#### Transaction Type Codes (AP/AR)

To avoid accidental inclusion of non-invoice records (payments, adjustments, reversals), use transaction code filters explicitly.

**FPLEDG `EPTRCD` (AP / supplier-side)**

| Code | Meaning |
|------|---------|
| 10 | Supplier Invoice |
| 11 | Credit Invoice |
| 12 | Debit Adjustment |
| 15 | Interest Invoice |
| 20 | Supplier Payment |
| 21 | On-account Payment |
| 22 | Bank Payment |
| 30 | Write-off |
| 40 | Adjustment |
| 50 | Exchange Rate Difference |
| 90 | Reversal Transaction |

**FSLEDG `ESTRCD` (AR / customer-side)**

| Code | Meaning |
|------|---------|
| 10 | Customer Invoice |
| 11 | Credit Invoice |
| 12 | Debit Note |
| 15 | Interest Invoice |
| 20 | Customer Payment |
| 21 | On-account Payment |
| 22 | Bank Payment |
| 30 | Write-off |
| 40 | Adjustment |
| 50 | Exchange Rate Difference |
| 90 | Reversal Transaction |

**Current service behavior (`DirectQueryDataSource`)**
- AP currently does **not** filter by `p.eptrcd`; all posting codes returned by the query (invoices, payments, adjustments, reversals, etc.) are included.
- AR extracts records via configurable `ArTransCode` (default `"10"`, customer invoice), which limits AR submissions to specific transaction codes.
- Only the AR side is currently constrained by transaction code; excluding non-invoice transactions on the AP side would require enabling an additional `p.eptrcd = 10` filter in the service.

### Input Model: `MovexInvoice`

**Namespace**: `MyInvois.Service.Models`  
**Purpose**: Maps MOVEX database records (DB2 on AS/400) to C# object

```csharp
public class MovexInvoice
{
    public string InvoiceNumber { get; set; }        // fpledg.epinbn / fsledg.esinbn
    public string InvoiceDate { get; set; }          // fpledg.epacdt / fsledg.esacdt (YYYYMMDD or ISO)
    public string InvoiceType { get; set; }          // Sales | Purchase
    public string CompanyCode { get; set; }          // fpledg.epcono / fsledg.escono
    public string VoucherNumber { get; set; }        // fpledg.epvono / fsledg.esvono
    public string CurrencyCode { get; set; }         // ISO 4217 (MYR, USD, SGD)
    public decimal ExchangeRate { get; set; }        // fpledg.eparat / fsledg.esarat
    public decimal TotalExclTax { get; set; }        // fpledg.epcuam / fsledg.escuam (excl tax)
    public decimal TotalTax { get; set; }            // fpledg.epvtam / fsledg.esvtam
    public decimal TotalInclTax { get; set; }        // Calculated: TotalExclTax + TotalTax

    public InvoiceParty? Supplier { get; set; }      // Seller (via IPartyDataProvider)
    public InvoiceParty? Buyer { get; set; }         // Buyer/Customer (via IPartyDataProvider)
    public List<InvoiceLine> Lines { get; set; }     // Line items
    public string Status { get; set; }               // Open/Posted/Completed
}

public class InvoiceParty
{
    public string? TIN { get; set; }                 // Tax ID (12 digits)
    public string Name { get; set; }                 // Registered name
    public string? BRN { get; set; }                 // Business Reg Number
    public string? AlternativeId { get; set; }       // NRIC/Passport/Army ID
    public string IdScheme { get; set; }             // BRN | NRIC | PASSPORT | ARMY
    public string? Address { get; set; }             // Registered address
    public string? ContactPerson { get; set; }
}

public class InvoiceLine
{
    public int LineNumber { get; set; }
    public string ItemNumber { get; set; }
    public string Description { get; set; }          // ≤300 chars
    public string ClassificationCode { get; set; }   // 3 chars
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; }        // EA, KG, etc.
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }           // Qty × UnitPrice
    public string TaxCode { get; set; }              // UN/ECE 5153
    public decimal TaxRate { get; set; }             // %
    public decimal TaxAmount { get; set; }
}
```

### Processing Model: `MyInvoiceDocument`

**Namespace**: `MyInvois.Service.Models`  
**Purpose**: Transformed invoice ready for MyInvois submission (UBL 2.1)

```csharp
public class MyInvoiceDocument
{
    public string Id { get; set; }                   // GUID
    public string Version { get; set; }              // "1.1"
    public string DocumentTypeCode { get; set; }     // "01" (invoice)
    public string InvoiceNumber { get; set; }        // ≤50 chars
    public string IssueDate { get; set; }            // YYYY-MM-DD
    public string IssueTime { get; set; }            // HH:MM:SS
    public string CurrencyCode { get; set; }         // ISO 4217
    public decimal ExchangeRate { get; set; }        // 1.0 if MYR
    
    // Supplier Block
    public string SupplierTIN { get; set; }          // Mandatory
    public string SupplierName { get; set; }         // ≤300 chars
    public string SupplierBRN { get; set; }
    public string SupplierIdScheme { get; set; }     // BRN
    public string? SupplierAddress { get; set; }
    
    // Buyer Block
    public string? BuyerTIN { get; set; }            // Optional if unavailable
    public string BuyerName { get; set; }            // Mandatory
    public string? BuyerAlternativeId { get; set; }
    public string BuyerIdScheme { get; set; }
    public string? BuyerAddress { get; set; }
    
    // Totals
    public decimal TotalExclTax { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalInclTax { get; set; }
    public decimal PayableAmount { get; set; }
    
    // Lines & Metadata
    public List<MyInvoiceLine> Lines { get; set; }
    public string? Signature { get; set; }           // XAdES v1.1
    public string SignatureAlgorithm { get; set; }   // RSA-SHA256
    public string? SourceInvoiceNumber { get; set; } // Audit trail
    public DateTime CreatedAt { get; set; }
    public List<ValidationError> ValidationErrors { get; set; }
}

public class MyInvoiceLine
{
    public int LineNumber { get; set; }
    public string ItemNumber { get; set; }
    public string Description { get; set; }          // ≤300 chars
    public string ClassificationCode { get; set; }   // 3 chars
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; }        // EA, KG
    public decimal UnitPrice { get; set; }
    public decimal LineTotalExclTax { get; set; }    // Qty × UnitPrice
    public string TaxCode { get; set; }
    public decimal TaxRate { get; set; }             // %
    public decimal TaxAmount { get; set; }
    public decimal LineTotalInclTax { get; set; }
}

public class ValidationError
{
    public string FieldName { get; set; }
    public string Message { get; set; }
    public string Severity { get; set; }             // Error/Warning
    public string ViolatedRule { get; set; }         // TIN_Format, MandatoryField
}
```

### Result Model: `SubmissionResult`

**Namespace**: `MyInvois.Service.Models`  
**Purpose**: Outcome of a single invoice submission

```csharp
public class SubmissionResult
{
    public string SubmissionId { get; set; }         // GUID
    public string InvoiceNumber { get; set; }        // MOVEX invoice
    public string Status { get; set; }               // Success | Failed | Skipped | Pending
    public string? MyInvoisUUID { get; set; }        // MyInvois response
    public string? MyInvoisStatus { get; set; }      // Valid | Invalid | Submitted | Rejected
    public string? SubmissionReference { get; set; } // MyInvois ref
    public int? HttpStatusCode { get; set; }         // 200, 400, 429, 500
    public string? ErrorCode { get; set; }           // DS301, DS302, DS101
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public long DurationMs { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string? RawResponse { get; set; }         // Full MyInvois response
    public MyInvoiceDocument? InvoiceData { get; set; } // Submitted document
    public bool IsSuccess => Status == "Success";
}

public class BatchResult
{
    public string BatchId { get; set; }
    public string BatchType { get; set; }            // Sales | Purchase
    public int TotalInvoices { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }            // Validation failures
    public decimal SuccessRate { get; set; }         // %
    public List<SubmissionResult> Submissions { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long DurationSeconds { get; set; }
    public string? ErrorSummary { get; set; }
}

public class TokenResponse
{
    public string AccessToken { get; set; }
    public string TokenType { get; set; }            // Bearer
    public int ExpiresIn { get; set; }               // Seconds (3600)
    public DateTime IssuedAt { get; set; }
    public bool IsValid => DateTime.UtcNow < IssuedAt.AddSeconds(ExpiresIn - 300);
}
```

---

## 🔌 Configuration Models

**Location**: `MyInvois.Service.Configuration`

```csharp
// MOVEX Database Settings (ADR-013: DB2 direct access)
public class MovexDbSettings
{
    public string ConnectionString { get; set; }     // From User Secrets
    public string DataSourceStrategy { get; set; }   // "DirectQuery" | "StoredProcedure"
    public string SchemaCmp100 { get; set; }         // "mvxcdta" (CMP100)
    public string SchemaCmp300 { get; set; }         // "mvxc300" (CMP300)
    public string[] ActiveCompanyCodes { get; set; } // ["100", "300"]
    public int CommandTimeoutSeconds { get; set; }   // 60
    public int MaxPoolSize { get; set; }             // 10
    public string PartyDataSource { get; set; }      // "Placeholder" | "MovexMaster"
    public string? ApInvoiceStoredProc { get; set; } // Stored proc for AP invoices
    public string? ArInvoiceStoredProc { get; set; } // Stored proc for AR invoices

    // AR query filters (CHG-007: aligned with Actual_MOVEX_AR.sql)
    public string ArDivision { get; set; }           // "L" — FSLEDG.ESDIVI
    public string ArTransCode { get; set; }          // "10" — FSLEDG.ESTRCD
    public string ArCustomerStatus { get; set; }     // "20" — OCUSMA.OKSTAT (active)
    public int ArMinYear { get; set; }               // dynamic — FSLEDG.ESYEA4 > ArMinYear

    // TIN/BRN column mappings — pending Finance team confirmation (ADR-013 Known Gap #1)
    // Leave empty until column names are confirmed; MovexMasterPartyDataProvider returns NULL when empty
    public string SupplierTinColumn { get; set; }    // CIDMAS column for supplier TIN (e.g., "IDCFC1")
    public string SupplierBrnColumn { get; set; }    // CIDMAS column for supplier BRN
    public string CustomerTinColumn { get; set; }    // OCUSMA column for customer TIN (e.g., "OKCFC1")
    public string CustomerBrnColumn { get; set; }    // OCUSMA column for customer BRN
}

// MyInvois API Settings
public class MyInvoisApiSettings
{
    public string BaseUrl { get; set; }
    public string TokenEndpoint { get; set; }        // /connect/token
    public string SubmissionEndpoint { get; set; }   // /api/v1.0/documentsubmissions
    public string DetailsEndpoint { get; set; }      // /api/v1.0/documents/{uuid}/details
    public int TimeoutSeconds { get; set; }          // 30
    public string Environment { get; set; }          // sandbox | production
    public string ClientId { get; set; }             // From User Secrets
    public string ClientSecret { get; set; }         // From User Secrets
    public string TIN { get; set; }                  // Organization TIN
}

// Batch Processing Settings
public class BatchConfiguration
{
    public int SalesBatchSize { get; set; }          // 100
    public int PurchaseBatchSize { get; set; }       // 50
    public int DelayBetweenBatchesMs { get; set; }   // 600 (0.6 sec for 100 req/min limit)
    public int MaxRetries { get; set; }              // 3
    public int RetryDelaySeconds { get; set; }       // 5
    public int CircuitBreakerThreshold { get; set; } // 5 failures
    public int CircuitBreakerDurationSeconds { get; set; } // 60
}

// Processing Settings
public class ProcessingSettings
{
    public bool EnableBatchProcessing { get; set; }  // true
    public int MonthlySubmissionDay { get; set; }    // 1
    public int SubmissionWindowHours { get; set; }   // 2
    public int MaxInvoicesPerRun { get; set; }       // 1000
}

// Validation Settings
public class ValidationSettings
{
    public int TINCache_TTL_Hours { get; set; }      // 1
    public bool RequireExchangeRateForNonMYR { get; set; } // true
    public bool AllowNullBuyerTIN { get; set; }      // true
    public int DecimalPlaces { get; set; }           // 2
}

// Foreign party default TIN/BRN (MyInvois EI numbers for non-Malaysian entities)
// Maps to appsettings.json["ForeignPartyDefaults"]
public class ForeignPartyDefaultsSettings
{
    public string SupplierTIN { get; set; }          // "EI00000000030" — foreign suppliers (self-billed imports)
    public string SupplierBRN { get; set; }          // "NA"
    public string BuyerTIN { get; set; }             // "EI00000000020" — foreign buyers (export sales)
    public string BuyerBRN { get; set; }             // "NA"
}
```

---

## 📦 Data Retention & Compliance

### Retention Policy

| Category | Retention | Reason |
|----------|-----------|--------|
| **Successful Submissions** | 7 years | Malaysian tax law (invoice valid 7 years) |
| **Failed Submissions** | 3 years | Audit trail & troubleshooting |
| **Validation Errors** | 2 years | Operational feedback |
| **Request/Response Payloads** | 1 year | Compliance forensics |

### Archive Strategy (Phase 2)

- After 1 year: Archive to cold storage (Azure Archive)
- After 7 years: Delete (if no active disputes)
- Encryption: TDE at rest, TLS in transit

---

## 🔍 Data Validation Rules

See [03-MyInvois Requirements](03-myinvois-requirements.md) for complete validation rules.

**Summary**:
- **Mandatory Fields**: 20+ across supplier, buyer, invoice, line items
- **TIN Format**: 12 digits, numeric only
- **Date Format**: YYYY-MM-DD (real dates, no placeholders)
- **Currency**: ISO 4217, exchange rate if non-MYR
- **Totals**: Mathematical consistency (±1 cent tolerance)
- **Line Items**: 3-char classification, ≤300 char descriptions, qty > 0

---

## 🔗 Related Documents

- [00-Product Vision](00-product-vision.md) - Vision & objectives
- [01-System Architecture](01-system-architecture.md) - Component architecture
- [03-MyInvois Requirements](03-myinvois-requirements.md) - Validation rules & field mapping
- [04-API Integration](04-api-integration.md) - API specifications
- [05-Deployment Guide](05-deployment-guide.md) - Database setup & deployment

---

**Owner**: Data Architecture Team  
**Last Review**: 2026-02-16
**Next Review**: 2026-03-16

