# MyInvois-Service - API Integration Guide

**Last Updated**: 2026-02-16
**Status**: MVAI Iteration 1 (Active)  
**Version**: 1.0

---

## 🔌 Overview

This document describes integration with two external data sources:
1. **MOVEX Database** (read invoices from IBM DB2 on AS/400)
2. **MyInvois API** (submit e-invoices)

---

## Part 1: MOVEX Database Integration (DB2 on AS/400)

### Purpose

Read pending invoices from MOVEX ERP database (IBM DB2 on AS/400) using direct SQL queries or stored procedures.

### Connection

```
Connection string configured via User Secrets (dotnet user-secrets):
  Server=<AS400_HOST>;Database=<DB_NAME>;UID={from secrets};PWD={from secrets};
```

- **Driver**: Net.IBM.Data.Db2 (primary), System.Data.Odbc (fallback)
- **ORM**: Dapper for lightweight mapping
- **Connection pooling**: MaxPoolSize configurable (default: 10)

### Authentication

- **Method**: DB2 connection authentication via User Secrets
- **Credentials**: Stored in `dotnet user-secrets` (not in appsettings.json)
- **Scope**: Read-only access to MOVEX ledger schemas
- **Encryption**: DB2 connection encryption enabled

### Data Access Patterns

The DataAccess layer uses a strategy pattern (`IInvoiceDataSource`) with two implementations:

#### 1. DirectQueryDataSource (Default)

Executes SQL queries directly against MOVEX ledger tables.

**Tables**:
- `fpledg` (AP - Accounts Payable ledger)
- `fsledg` (AR - Accounts Receivable ledger)
- `fgledg` (GL - General Ledger)

**Schemas**:
- `mvxcdta` for CMP100 (company 100)
- `mvxc300` for CMP300 (company 300)

**Example Query (AP Invoices)**:
```sql
SELECT
    epcono AS CompanyCode,
    epinbn AS InvoiceNumber,
    epvono AS VoucherNumber,
    epacdt AS AccountingDate,
    epcuam AS Amount,
    epvtam AS TaxAmount,
    eparat AS ExchangeRate,
    epsuno AS SupplierNumber
FROM {schema}.fpledg
WHERE epacdt >= @fromDate
  AND eptrcd IN (40, 41)  -- Invoice transaction codes
ORDER BY epacdt
```

**Example Query (AR Invoices)**:
```sql
SELECT
    escono AS CompanyCode,
    escino AS InvoiceNumber,
    esvono AS VoucherNumber,       -- KEY: links to OINVOH.UHVONO for line items
    TRIM(CHAR(esvono)) AS VoucherNumber,
    esacdt AS AccountingDate,
    escuam AS Amount,
    esvtam AS TaxAmount,
    esarat AS ExchangeRate,
    escuno AS CustomerNumber,
    TRIM(espyno) AS PayerNo
FROM {schema}.fsledg
WHERE esacdt >= @fromDate
  AND estrcd IN (10, 11)  -- Invoice transaction codes
ORDER BY esacdt
```

**AR Line Items Join Path (FSLEDG → OINVOH → ODLINE)**:

The correct join path for AR invoice line items, discovered 2026-04-02:

```sql
SELECT TRIM(f.ESCINO) AS InvoiceNo,
    ROW_NUMBER() OVER (PARTITION BY f.ESCINO ORDER BY dl.UBPONR, dl.UBPOSX) AS LineNumber,
    TRIM(dl.UBITNO) AS ItemNumber,
    COALESCE(TRIM(ol.OBITDS), TRIM(dl.UBITNO), '') AS Description,
    dl.UBIVQT AS Quantity,
    COALESCE(TRIM(dl.UBSPUN), 'EA') AS UnitOfMeasure,
    dl.UBSAPR AS UnitPrice,
    dl.UBLNAM AS LineTotal,
    COALESCE(TRIM(ol.OBVTCD), '') AS TaxCode,
    0 AS TaxAmount
FROM {schema}.FSLEDG f
JOIN {schema}.OINVOH oh ON f.ESCONO = oh.UHCONO AND f.ESVONO = oh.UHVONO
JOIN {schema}.ODLINE dl ON oh.UHCONO = dl.UBCONO AND oh.UHIVNO = dl.UBIVNO
LEFT JOIN {schema}.OOLINE ol ON dl.UBCONO = ol.OBCONO
    AND TRIM(dl.UBORNO) = TRIM(ol.OBORNO)
    AND dl.UBPONR = ol.OBPONR AND dl.UBPOSX = ol.OBPOSX
WHERE f.ESCONO = ? AND f.ESVONO IN ({placeholders})
ORDER BY f.ESCINO, dl.UBPONR, dl.UBPOSX
```

**Key schema facts (ODLINE)**:
- `UBIVNO` — internal invoice number (links from `OINVOH.UHIVNO`)
- `UBIVQT` — invoiced quantity
- `UBLNAM` — line amount (net)
- `UBSAPR` — unit sales price
- `UBSPUN` — unit of measure for sales price
- `UBITNO` — item number (for description, join to OOLINE.OBITDS)

**Why NOT OINVOL**: `OINVOL.OIIVNO` does not exist in MVXCOBJ. `OINVOL` is a routing/planning table with no reliable invoice line link. Using `FSLEDG.ESPYNO = OINVOL.ONPYNO` is non-unique (returns all invoices for a payer).

**Classification Codes**: LHDN classification codes (001–045) are a fixed LHDN reference table. They are **NOT stored in MOVEX**. `MITMAS.MMITCL` is a MOVEX product group code — completely unrelated to LHDN codes. Default `"022"` (Others) is used until Finance maps product groups to proper codes.

#### 2. StoredProcedureDataSource

Calls configured stored procedures for environments where direct table access is restricted.

**Configuration**:
```json
{
  "MovexDb": {
    "DataSourceStrategy": "StoredProcedure",
    "ApInvoiceStoredProc": "MYLIB.GET_AP_INVOICES",
    "ArInvoiceStoredProc": "MYLIB.GET_AR_INVOICES"
  }
}
```

#### 3. Strategy Selection

The strategy is selected via `MovexDbSettings.DataSourceStrategy` configuration:
- `"DirectQuery"` → `DirectQueryDataSource` (default)
- `"StoredProcedure"` → `StoredProcedureDataSource`

### Party Data Enrichment (IPartyDataProvider)

Invoice records from ledger tables do not contain full party details (name, TIN, address). Party enrichment is handled by `IPartyDataProvider`:

- **PlaceholderPartyDataProvider**: Returns placeholder/default party data (for initial development)
- **MovexMasterPartyDataProvider**: Queries MOVEX master tables for full supplier/customer details

Selected via `MovexDbSettings.PartyDataSource` (`"Placeholder"` or `"MovexMaster"`).

### RawInvoiceRecord DTO

Raw database rows are mapped to `RawInvoiceRecord` in the DataAccess layer before being enriched and converted to `MovexInvoice`:

```csharp
public class RawInvoiceRecord
{
    public string CompanyCode { get; set; }
    public string InvoiceNumber { get; set; }
    public string VoucherNumber { get; set; }  // FSLEDG.ESVONO — used as join key for AR line items
    public int AccountingDate { get; set; }    // YYYYMMDD as int
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ExchangeRate { get; set; }
    public string CounterpartyNumber { get; set; } // Supplier or Customer number
    public int TransactionCode { get; set; }
    public string? PayerNo { get; set; }       // FSLEDG.ESPYNO — populated for AR invoices
}
```

**AR line item fetch key**: `FetchArLineItemsAsync` groups by `VoucherNumber` (ESVONO). The SQL uses `ESVONO IN (?)` and joins via `OINVOH.UHVONO`. Do NOT use `(InvoiceNumber, PayerNo)` as key for AR — payer number is non-unique across invoices.

### Implementation (MovexInvoiceReader)

```csharp
public class MovexInvoiceReader : IMovexInvoiceReader
{
    private readonly IInvoiceDataSource _dataSource;
    private readonly IPartyDataProvider _partyProvider;
    private readonly MovexDbSettings _settings;

    public async Task<List<MovexInvoice>> GetPendingInvoices(DateTime fromDate)
    {
        // 1. Query raw invoice records from DB2 via strategy
        var rawRecords = await _dataSource.GetInvoicesSince(fromDate);

        // 2. Enrich with party data
        var invoices = new List<MovexInvoice>();
        foreach (var record in rawRecords)
        {
            var invoice = MapToMovexInvoice(record);
            invoice.Supplier = await _partyProvider.GetSupplier(record.CounterpartyNumber);
            invoice.Buyer = await _partyProvider.GetBuyer(record.CounterpartyNumber);
            invoices.Add(invoice);
        }

        return invoices;
    }

    public async Task<MovexInvoice?> GetInvoiceById(string invoiceNumber)
    {
        var record = await _dataSource.GetInvoiceByNumber(invoiceNumber);
        if (record == null) return null;

        var invoice = MapToMovexInvoice(record);
        invoice.Supplier = await _partyProvider.GetSupplier(record.CounterpartyNumber);
        invoice.Buyer = await _partyProvider.GetBuyer(record.CounterpartyNumber);
        return invoice;
    }

    private MovexInvoice MapToMovexInvoice(RawInvoiceRecord record)
    {
        return new MovexInvoice
        {
            CompanyCode = record.CompanyCode,
            InvoiceNumber = record.InvoiceNumber,
            VoucherNumber = record.VoucherNumber,
            InvoiceDate = record.AccountingDate.ToString(),
            TotalExclTax = record.Amount,
            TotalTax = record.TaxAmount,
            TotalInclTax = record.Amount + record.TaxAmount,
            ExchangeRate = record.ExchangeRate
        };
    }
}
```

### Error Handling

| Error | Type | Action |
|-------|------|--------|
| DB2 connection failure | Retriable | Retry with backoff (3 attempts), then fail batch |
| Connection pool exhausted | Retriable | Wait and retry, log warning |
| SQL timeout | Retriable | Retry with increased timeout |
| Invalid SQL / schema not found | No-Retry | Log error, fail immediately |
| Authentication failure | No-Retry | Log critical, check User Secrets configuration |
| ODBC driver not found | No-Retry | Log critical, check Net.IBM.Data.Db2 NuGet package |

**General Principles**:
- All DB2 errors logged with full exception details
- Connection errors trigger circuit breaker after threshold
- Read-only access prevents accidental data modification
- Command timeout configurable via `MovexDbSettings.CommandTimeoutSeconds`

---

## Part 2: MyInvois API Integration

### Purpose

Submit validated, transformed invoices to MyInvois (LHDNM) for e-invoice issuance.

### Base URL

```
https://api.myinvois.hasil.gov.my  (Production)
https://sandbox.myinvois.hasil.gov.my  (Testing)
```

### Authentication: OAuth 2.0 (Client Credentials)

**Flow**:
```
1. Request token from /connect/token
2. Include token in Authorization header
3. Cache token for 1 hour (expiry - 5 min buffer)
4. Auto-refresh on 401 response
```

**Token Endpoint**: `POST /connect/token`

**Request Body** (form-urlencoded):
```
grant_type=client_credentials
client_id=YOUR_CLIENT_ID
client_secret=YOUR_CLIENT_SECRET
scope=InvoiceService
```

**Response**:
```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "scope": "InvoiceService"
}
```

**Implementation** (OAuth caching):

```csharp
public class MyInvoiceSubmitter : IMyInvoiceSubmitter
{
    private TokenResponse? _cachedToken;
    
    public async Task<string> GetAccessToken()
    {
        // Check if cached token is still valid
        if (_cachedToken != null && _cachedToken.IsValid)
        {
            return _cachedToken.AccessToken;
        }
        
        // Refresh token
        var request = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _settings.ClientId),
            new KeyValuePair<string, string>("client_secret", _settings.ClientSecret),
            new KeyValuePair<string, string>("scope", "InvoiceService")
        });
        
        var response = await _httpClient.PostAsync(
            $"{_settings.BaseUrl}{_settings.TokenEndpoint}", 
            request);
        
        var json = await response.Content.ReadAsStringAsync();
        _cachedToken = JsonSerializer.Deserialize<TokenResponse>(json);
        
        return _cachedToken.AccessToken;
    }
}
```

### Endpoints

#### 1. Submit Invoice (Primary Endpoint)

**Endpoint**: `POST /api/v1.0/documentsubmissions`

**Headers**:
```
Authorization: Bearer {access_token}
Content-Type: application/json
X-Request-ID: {guid}  (for tracing)
```

**Request Body** (JSON):
```json
{
  "documents": [
    {
      "format": "UBL",
      "documentHash": "SHA256_HASH",
      "codeNumber": "INV-2026-00001",
      "documentType": "01",
      "issueDate": "2026-02-05",
      "issueTime": "14:30:45",
      "supplierTIN": "123456789012",
      "supplierName": "Acme Corp Sdn Bhd",
      "supplierBRN": "000123456789",
      "buyerTIN": "987654321098",
      "buyerName": "Tech Solutions Ltd",
      "currencyCode": "MYR",
      "exchangeRate": 1.0,
      "totalExcludingTax": 1415.09,
      "totalTaxAmount": 84.91,
      "totalIncludingTax": 1500.00,
      "payableAmount": 1500.00,
      "lineItems": [
        {
          "itemNumber": "1",
          "description": "Software License",
          "classificationCode": "620",
          "quantity": 1,
          "unitOfMeasure": "EA",
          "unitPrice": 1000.00,
          "lineTotalExcludingTax": 1000.00,
          "taxType": "01",
          "taxRate": 6.0,
          "taxAmount": 60.00,
          "lineTotalIncludingTax": 1060.00
        }
      ],
      "signature": "XADES_SIGNATURE_CONTENT"
    }
  ],
  "signatureMethod": "xmldsig",
  "signature": "DOCUMENT_BATCH_SIGNATURE"
}
```

**Response** (Success: 202 Accepted):
```json
{
  "submissionUID": "550e8400-e29b-41d4-a716-446655440000",
  "submissionId": "SUB-2026-00001",
  "acceptedDocuments": [
    {
      "uuid": "660e8400-e29b-41d4-a716-446655440111",
      "invoiceCodeNumber": "INV-2026-00001",
      "status": "VALID",
      "receivedDateTime": "2026-02-05T14:30:45Z",
      "validationErrors": []
    }
  ],
  "rejectedDocuments": [],
  "processingTimestamp": "2026-02-05T14:30:45Z"
}
```

**Error Response** (400 Bad Request):
```json
{
  "error": "DS102",
  "message": "Missing mandatory field: supplierTIN",
  "details": [
    {
      "field": "supplierTIN",
      "rule": "MANDATORY",
      "description": "TIN must be 12 numeric digits"
    }
  ],
  "timestamp": "2026-02-05T14:30:45Z"
}
```

#### 2. Get Document Details (Status Polling - Phase 2)

**Endpoint**: `GET /api/v1.0/documents/{uuid}/details`

**Headers**:
```
Authorization: Bearer {access_token}
```

**Response** (200 OK):
```json
{
  "uuid": "660e8400-e29b-41d4-a716-446655440111",
  "submissionId": "SUB-2026-00001",
  "invoiceCodeNumber": "INV-2026-00001",
  "status": "VALID",
  "uin": "S1PR230100004534",
  "qrCode": "data:image/png;base64,...",
  "issuedDateTime": "2026-02-05T14:30:45Z",
  "longStatus": "Accepted - No validation errors",
  "validationErrors": []
}
```

### Implementation (MyInvoiceSubmitter)

```csharp
public class MyInvoiceSubmitter : IMyInvoiceSubmitter
{
    public async Task<SubmissionResult> Submit(MyInvoiceDocument document)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // 1. Get OAuth token
            var token = await GetAccessToken();
            
            // 2. Prepare request
            var payload = new
            {
                documents = new[] { ConvertToMyInvoisFormat(document) },
                signatureMethod = "xmldsig",
                signature = document.Signature
            };
            
            // 3. Submit to MyInvois API
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_settings.BaseUrl}{_settings.SubmissionEndpoint}")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };
            
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Request-ID", Guid.NewGuid().ToString());
            
            var response = await _httpClient.SendAsync(request);
            
            // 4. Parse response
            var responseJson = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                // Success
                var result = JsonSerializer.Deserialize<MyInvoisSubmissionResponse>(responseJson);
                
                return new SubmissionResult
                {
                    Status = "Success",
                    MyInvoisUUID = result.AcceptedDocuments[0].Uuid,
                    MyInvoisStatus = result.AcceptedDocuments[0].Status,
                    HttpStatusCode = (int)response.StatusCode,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    RawResponse = responseJson
                };
            }
            else
            {
                // Error
                var error = JsonSerializer.Deserialize<MyInvoisError>(responseJson);
                
                var result = new SubmissionResult
                {
                    Status = "Failed",
                    HttpStatusCode = (int)response.StatusCode,
                    ErrorCode = error.Error,
                    ErrorMessage = error.Message,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    RawResponse = responseJson
                };
                
                // Determine if retriable
                result.Status = IsRetriableError(error.Error) ? "Pending" : "Failed";
                
                return result;
            }
        }
        catch (Exception ex)
        {
            return new SubmissionResult
            {
                Status = "Failed",
                ErrorMessage = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
    }
    
    private bool IsRetriableError(string errorCode)
    {
        return errorCode switch
        {
            "429" => true,  // Rate limited
            "500" => true,  // Server error
            "503" => true,  // Service unavailable
            _ => false
        };
    }
}
```

### Rate Limiting

**Limit**: 100 requests per minute

**Implemented via**:
- Batch size: 50-100 invoices per request
- Delay between batches: 600ms (0.6 sec = 100 req/min)
- Circuit breaker (Phase 2): Auto-backoff on 429 responses

---

## Part 3: Rate Limiting & Error Handling

### MyInvois Error Classification

| Error Code | Type | Retriable | Action |
|-----------|------|-----------|--------|
| DS101-DS103 | Validation | ❌ No | Manual review of document |
| DS301-DS302 | Signature/Duplicate | ❌ No | Check audit log for duplicates |
| DS401-DS402 | TIN | ❌ No | Verify TIN with finance team |
| 429 | Rate Limited | ✅ Yes | Backoff 60s, retry |
| 500, 503 | Server Error | ✅ Yes | Exponential backoff, max 3 retries |

### Retry Strategy (Phase 1)

```csharp
public async Task<SubmissionResult> Submit(MyInvoiceDocument document)
{
    int retryCount = 0;
    
    while (retryCount < _settings.MaxRetries)
    {
        try
        {
            var result = await SubmitInternal(document);
            
            if (result.IsSuccess || !IsRetriableError(result.ErrorCode))
            {
                return result;  // Don't retry
            }
            
            // Retriable error
            retryCount++;
            await Task.Delay(TimeSpan.FromSeconds(
                _settings.RetryDelaySeconds * (int)Math.Pow(2, retryCount)));
        }
        catch (HttpRequestException ex) when (IsNetworkError(ex))
        {
            // Network error - retry
            retryCount++;
            await Task.Delay(TimeSpan.FromSeconds(_settings.RetryDelaySeconds));
        }
    }
    
    return new SubmissionResult 
    { 
        Status = "Failed", 
        ErrorMessage = "Max retries exceeded"
    };
}
```

---

## Part 4: Request Signing (XAdES v1.1)

**Responsibility**: MyInvois SDK (no custom implementation)

**Process**:
1. Build XML document (UBL 2.1 schema)
2. Call SDK to sign with private key (XAdES v1.1)
3. Include signature in submission request

**Key Points**:
- Certificate: Issued by trusted CA
- Algorithm: RSA-SHA256
- Format: XAdES v1.1 (MyInvois requirement)

---

## Part 5: Integration Checklist

### MOVEX Integration
- ✅ Windows AD auth configured
- ✅ IP allow-list includes SRXWEBAPP1
- ✅ HTTP client timeout: 30 seconds
- ✅ Retry logic for transient errors
- ✅ Date format conversion (YYYYMMDD → ISO 8601)

### MyInvois Integration
- ✅ OAuth token caching (1-hour TTL)
- ✅ Auto-refresh on 401
- ✅ Rate limiting (600ms between batches)
- ✅ Error classification (retriable vs. no-retry)
- ✅ XAdES signature generation
- ✅ Duplicate detection (MyInvois UUID)
- ✅ Audit logging (all submissions)

---

## Part 6: Troubleshooting Guide

### "401 Unauthorized from MyInvois"
- Check OAuth credentials in User Secrets
- Verify token endpoint URL
- Confirm sandbox/production setting

### "DS302 Duplicate Submission"
- Check audit log: `SELECT * FROM dbo.AuditLog WHERE InvoiceNumber = 'INV-2026-00001'`
- Verify MyInvois UUID matches
- Manual recovery: Log as skipped, don't resubmit

### "429 Rate Limited"
- Reduce batch size or increase delay (600ms → 800ms)
- Monitor MyInvois status on their dashboard
- Phase 2: Implement circuit breaker

### "500 Internal Server Error"
- Likely temporary; retry automatically
- If persistent: Contact MyInvois support
- Log full request/response for forensics

---

---

## Part 7: Status Polling (Phase 2)

### Get Submission Status

```
GET /api/v1.0/documents/{uuid} HTTP/1.1
Host: sandbox.myinvois.hasil.gov.my
Authorization: Bearer {access_token}
```

**Status Response:**
```json
{
  "uuid": "550e8400-e29b-41d4-a716-446655440000",
  "status": "VALID",
  "submissionDate": "2026-02-05T10:15:30Z",
  "validationDate": "2026-02-05T10:15:45Z",
  "acceptanceDate": "2026-02-05T10:16:00Z",
  "rejectionReason": null,
  "buyerAction": null
}
```

**Status Meanings:**

| Status | Meaning | Next Action |
|--------|---------|-------------|
| **SUBMITTED** | Accepted, awaiting validation | Poll again in 5 min |
| **VALID** | Passed validation | Complete ✅ |
| **INVALID** | Failed validation | Manual review ⚠️ |
| **APPROVED** | Buyer approved (Phase 2) | Complete ✅ |
| **REJECTED** | Buyer rejected (Phase 2) | Correct & resubmit |

**Polling Implementation (Phase 2):**

```csharp
public async Task<DocumentStatus> PollSubmissionStatus(
  Guid uuid, int maxAttempts = 12, int delaySeconds = 5)
{
  for (int attempt = 1; attempt <= maxAttempts; attempt++)
  {
    var status = await GetDocumentStatus(uuid);
    
    if (status.Status == "VALID" || status.Status == "INVALID")
      return status; // Terminal state reached

    _logger.LogInformation($"Status: {status.Status}, attempt {attempt}/{maxAttempts}");
    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
  }

  throw new TimeoutException($"Status polling timeout for {uuid}");
}
```

---

## Part 8: Rate Limiting & Batch Processing

### Rate Limit Details

- **Limit:** 100 requests/minute
- **Response Header:** `X-RateLimit-Remaining: {remaining}`
- **Behavior:** Returns 429 when exceeded
- **Reset:** Automatic at next minute

### Rate Limit Handling

```csharp
// Service enforces delays between batches
var batchSize = 50;
var invoices = GetInvoices().ToList();
var batches = invoices.Chunk(batchSize);

foreach (var batch in batches)
{
  // Process batch
  foreach (var invoice in batch)
  {
    var result = await _submitter.Submit(invoice);
    // ...
  }

  // Wait before next batch (0.6s = 100 req/min)
  await Task.Delay(600); // milliseconds
}
```

### Rate Limit Calculation

```
Monthly invoices: 100 (sales) + 500 (purchase) = 600
Requests per invoice: 1
Total monthly requests: 600

Per-minute average: 600 / (30 days × 24 hours × 60 min) ≈ 0.014 req/min
Peak burst (10:00-10:05): 600 / 5 = 120 req/min

Rate limit: 100 req/min
Utilization: 20% (safely under limit)
Batch delay: 600ms between 50-invoice batches
Expected duration: ~100 invoices in 8 minutes (safe)
```

---

## Part 9: Testing Endpoints

### Sandbox Environment

```
Base URL: https://sandbox.myinvois.hasil.gov.my
Token Endpoint: {baseUrl}/connect/token
Submission: {baseUrl}/api/v1.0/documentsubmissions
```

**Test Credentials (Provided by LHDNM):**
- Client ID: (see admin)
- Client Secret: (see admin, stored in Key Vault)

### Test Data

**Valid Test TIN:** 123456789012  
**Valid Test BRN:** 987654321  
**Test Supplier:** "PT Test Technology"  
**Test Buyer:** "Test Corporation"  

### Health Check

```bash
curl -X POST https://sandbox.myinvois.hasil.gov.my/connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=YOUR_ID" \
  -d "client_secret=YOUR_SECRET" \
  -d "scope=MyInvoiceAPI"
```

**Expected Response:**
```json
{
  "access_token": "...",
  "token_type": "Bearer",
  "expires_in": 3600
}
```

---

## Part 10: Migration to Production

### Pre-Production Checklist

- [ ] Certificate acquired from Malaysian CA
- [ ] Certificate installed and tested
- [ ] Production API credentials obtained
- [ ] Rate limit testing completed
- [ ] Error handling tested with sandbox
- [ ] Audit logging verified
- [ ] Rollback procedure documented
- [ ] Support contact established with LHDNM

### Configuration Changes

**Development:**
```
BaseUrl: https://sandbox.myinvois.hasil.gov.my
ClientId: SANDBOX_ID
ClientSecret: (from User Secrets)
```

**Production:**
```
BaseUrl: https://prod.myinvois.hasil.gov.my
ClientId: PROD_ID
ClientSecret: (from Azure Key Vault)
```

---

## Part 11: Support & Resources

### LHDNM Support

- **Email:** support@myinvois.hasil.gov.my
- **Hours:** Mon-Fri 8 AM - 5 PM (Malaysia Time)
- **Response Time:** 24 hours typical
- **Escalation:** Contact assigned account manager

### Documentation

- **API Reference:** [MyInvois API Docs](https://sandbox.myinvois.hasil.gov.my/docs) (requires login)
- **UBL 2.1 Schema:** [OASIS UBL 2.1](https://docs.oasis-open.org/ubl/UBL-2.1.html)
- **XAdES Standard:** [ETSI XAdES](https://www.etsi.org/deliver/etsi_ts/103100_103199/103171/01.04.01_60/ts_103171v010401p.pdf)

### Troubleshooting

**Issue: "Invalid Token"**
- Verify credentials in Key Vault
- Check token TTL
- Refresh token explicitly

**Issue: "Duplicate Submission"**
- Query audit log for invoice number
- Check MyInvois submission history
- Generate new UUID if needed

**Issue: "Rate Limit Exceeded"**
- Check batch delay (should be 600ms)
- Reduce batch size
- Verify no parallel submissions

---

## Part 12: Glossary

| Term | Definition |
|------|-----------|
| **MyInvois** | Malaysian e-invoicing system |
| **UBL 2.1** | Universal Business Language (XML schema) |
| **XAdES v1.1** | XML digital signature standard |
| **TIN** | Tax Identification Number (Malaysia) |
| **BRN** | Business Registration Number |
| **UUID** | Unique identifier returned by MyInvois |
| **Sandbox** | Testing environment (no real submissions) |
| **Production** | Live environment (real submissions) |
| **OAuth 2.0** | Authentication protocol |
| **Token TTL** | Time to Live (1 hour for MyInvois) |
| **Batch** | Group of invoices submitted together |

---

## 🔗 Related Documents

- [01-System Architecture](01-system-architecture.md) - Architecture & components
- [03-MyInvois Requirements](03-myinvois-requirements.md) - Validation rules & traceability
- [05-Deployment Guide](05-deployment-guide.md) - Setup instructions

---

**Owner**: Integration Team  
**Source:** MyInvois API Reference & Implementation Guide
**Last Review**: 2026-02-06  
**Next Review**: 2026-03-05

