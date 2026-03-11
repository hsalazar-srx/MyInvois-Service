# Detailed Execution Plan: Sprint 2-3 Implementation Tasks

## Overview

**Sprints:** Feb 10-21, 2026 (Sprint 2 + Sprint 3)
**Goal:** Implement all services, validators, and resolve go-live blockers
**Owner:** Developer
**Sprint 2 Result:** ✅ COMPLETE — 225 tests passing (415% of target)
**Sprint 3 Status:** ⏳ IN PROGRESS — 3 go-live blockers being resolved

---

## Task 1: Implement MovexInvoiceReader Service ✅ COMPLETE

**Owner:** Developer
**Duration:** 1.5 days (Mon-Tue morning)
**Effort:** 12 hours
**Result:** MovexInvoiceReader (179 lines) + DataAccess layer (DirectQueryDataSource 359 lines)
**Success Criteria:**
- [x] All 3 methods implemented
- [x] 12+ unit tests written & passing (actual: 9 unit + integration tests)
- [x] ≥80% code coverage
- [x] Code review approved
- [x] Zero critical defects

### Prerequisites
- [ ] Read ai/memory/03-integration-contracts.md (MOVEX DB2 data access spec)
- [ ] Review SETUP.md (local environment)
- [ ] Verify MOVEX DB2 connection string (from User Secrets)

### Implementation Steps

**Step 1: Create MovexInvoiceReader Service (2 hours)**

File: `src/Services/MovexInvoiceReader.cs`

Interface to implement (already defined):
```csharp
public interface IMovexInvoiceReader
{
    Task<List<MovexInvoice>> GetPendingInvoices(int year, int month);
    Task<MovexInvoice> GetInvoiceById(string invoiceId);
    Task<List<MovexInvoice>> GetInvoicesByDateRange(DateTime from, DateTime to);
}
```

Requirements (from ai/memory/03):
- Query fpledg/fsledg/fgledg DB2 tables via IInvoiceDataSource
- Use DB2 connection string for authentication
- Handle transient DB2 errors with exponential backoff
- Return MovexInvoice DTOs
- Log all data source calls for debugging

Code structure:
```csharp
namespace MyInvois.Service.Services
{
    public class MovexInvoiceReader : IMovexInvoiceReader
    {
        private readonly IInvoiceDataSource _dataSource;
        private readonly ILogger<MovexInvoiceReader> _logger;
        private readonly MovexDbSettings _settings;
        
        public async Task<List<MovexInvoice>> GetPendingInvoices(int year, int month)
        {
            // 1. Open DB2 connection via IInvoiceDataSource
            // 2. Query fpledg/fsledg/fgledg DB2 tables
            // 3. Filter for pending invoices in month
            // 4. Map results to MovexInvoice objects
            // 5. Log results
            // 6. Return list
        }
        
        // ... other methods
    }
}
```

**Step 2: Implement DB2 Connection Management (2 hours)**

File: `src/Services/MovexInvoiceReader.cs` - Add DB2 connection helper

Requirements:
- Use DB2 connection string from MovexDbSettings
- Manage connection pooling via IInvoiceDataSource
- Handle connection timeouts (per ADR-007)
- Auto-reconnect on transient failures

Code pattern:
```csharp
// DB2 connection is managed by IInvoiceDataSource implementations
// (DirectQueryDataSource or StoredProcedureDataSource)
// MovexInvoiceReader delegates data access to the injected IInvoiceDataSource
```

**Step 3: Implement GetPendingInvoices Method (2 hours)**

Requirements (from ai/memory/03):
- Query fpledg/fsledg/fgledg DB2 tables for month
- Filter to pending invoices only
- Handle pagination (limit 100 per request)
- Batch into 100 sales + 50 purchase

Code pattern:
```csharp
public async Task<List<MovexInvoice>> GetPendingInvoices(int year, int month)
{
    var invoices = new List<MovexInvoice>();

    // Query sales invoices from fpledg/fsledg DB2 tables via IInvoiceDataSource
    var salesRecords = await _dataSource.GetPendingInvoicesAsync(year, month, InvoiceType.Sales);
    // Map RawInvoiceRecord results to MovexInvoice objects
    // Add to invoices list

    // Query purchase invoices from fgledg DB2 tables
    var purchaseRecords = await _dataSource.GetPendingInvoicesAsync(year, month, InvoiceType.Purchase);
    // Similar pattern

    // Log & return
    return invoices;
}
```

**Step 4: Implement GetInvoiceById Method (1 hour)**

Requirements:
- Fetch single invoice by ID
- Handle not found (404) gracefully
- Return MovexInvoice DTO

**Step 5: Implement GetInvoicesByDateRange Method (1 hour)**

Requirements:
- Query invoices between dates
- Handle timezone conversion (use UTC)
- Return list of MovexInvoice DTOs

**Step 6: Add Retry Logic (2 hours)**

Requirements (from ai/memory/05):
- Handle transient DB2 errors (connection timeout, communication link failure)
- Exponential backoff: 1s, 2s, 4s, 8s
- Max 3 attempts per request
- Log all retries

Code pattern:
```csharp
private async Task<T> QueryWithRetry<T>(Func<Task<T>> dbCall, string operationName)
{
    int maxAttempts = 3;
    int delayMs = 1000;

    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            return await dbCall();
        }
        catch (Exception ex) when (IsTransientDbError(ex) && attempt < maxAttempts)
        {
            _logger.LogWarning($"DB2 query {operationName} failed, attempt {attempt}/{maxAttempts}, retrying in {delayMs}ms");
            await Task.Delay(delayMs);
            delayMs *= 2; // Exponential backoff
        }
    }

    throw new InvalidOperationException($"DB2 query {operationName} failed after {maxAttempts} attempts");
}
```

### Unit Tests (12+ test cases)

File: `tests/MyInvois.Service.Tests/Services/MovexInvoiceReaderTests.cs`

Test cases:
1. [ ] GetPendingInvoices_ValidMonth_ReturnsSalesAndPurchase
2. [ ] GetPendingInvoices_NoInvoices_ReturnsEmptyList
3. [ ] GetPendingInvoices_DbTransientError_RetriesAndSucceeds
4. [ ] GetPendingInvoices_DbConnectionTimeout_BacksoffAndSucceeds
5. [ ] GetPendingInvoices_DbFailsAllRetries_ThrowsException
6. [ ] GetInvoiceById_ValidId_ReturnsInvoice
7. [ ] GetInvoiceById_NotFound_ThrowsException
8. [ ] GetInvoicesByDateRange_ValidRange_ReturnsFiltered
9. [ ] DataSource_FirstCall_OpensConnection
10. [ ] DataSource_ConnectionValid_ReusesConnection
11. [ ] DataSource_ConnectionDropped_Reconnects
12. [ ] DataSource_ReconnectFails_ThrowsException

### Code Review Checklist

- [ ] No hardcoded credentials
- [ ] All DB2 queries use parameterized access
- [ ] Error handling for transient errors (retry)
- [ ] Error handling for permanent errors (throw)
- [ ] All edge cases tested
- [ ] Code follows patterns in ai/memory/05
- [ ] Logging at appropriate levels (Info/Warn/Error)
- [ ] No unused imports or variables

### Definition of Done

- [ ] All code written & tests passing
- [ ] Code coverage ≥80%
- [ ] Peer code review completed & approved
- [ ] Zero build warnings
- [ ] Builds successfully on other machines
- [ ] Committed to version control

---

## Task 2: Implement MyInvoiceMapper & Start Validators ✅ COMPLETE

**Owner:** Developer
**Duration:** 1.5 days (Tue afternoon - Wed morning)
**Effort:** 12 hours
**Result:** MyInvoisMapper (320 lines) + MandatoryFieldsValidator (274 lines) + TINValidator (96 lines, format-only)
**Success Criteria:**
- [x] MyInvoiceMapper implemented (26 unit tests)
- [x] MandatoryFieldsValidator implemented (20+ unit tests)
- [x] TINValidator implemented (format validation; API validation deferred — see Task 8)
- [x] 20+ unit tests written & passing
- [x] Code review approved

### Implementation Steps

**Step 1: Implement MyInvoiceMapper Service (3 hours)**

File: `src/Services/MyInvoiceMapper.cs`

Interface:
```csharp
public interface IMyInvoiceMapper
{
    Task<(MyInvoiceDocument Document, List<ValidationError> Errors)> Transform(MovexInvoice source);
    Task<bool> ValidateDocument(MyInvoiceDocument doc);
}
```

Requirements:
- Transform MOVEX fields to MyInvois UBL 2.1 fields
- Coordinate 5 validators (see mapping in ai/memory/03)
- Collect validation errors
- Return both document & errors

Mapping (key fields from ai/memory/03):
```csharp
public async Task<(MyInvoiceDocument, List<ValidationError>)> Transform(MovexInvoice source)
{
    var doc = new MyInvoiceDocument();
    var errors = new List<ValidationError>();
    
    // Map basic fields
    doc.InvoiceNumber = source.ORINVN;
    doc.InvoiceDate = source.OIDATE.ToUTC();
    doc.SupplierTIN = source.ORSUNO; // Seller TIN
    doc.BuyerTIN = source.ORCUNO;   // Buyer TIN
    
    // Map amounts
    doc.LineTotal = source.Items.Sum(i => i.LineAmount);
    doc.TaxTotal = source.Items.Sum(i => i.TaxAmount);
    doc.GrandTotal = doc.LineTotal + doc.TaxTotal;
    
    // Map items
    foreach (var item in source.Items)
    {
        doc.Items.Add(new InvoiceItem
        {
            Description = item.AITXDP,
            Quantity = item.AIQA,
            UnitPrice = item.AIPNL,
            Amount = item.AILNAM,
        });
    }
    
    // Run validators
    await ValidateAndCollectErrors(doc, errors);
    
    return (doc, errors);
}
```

**Step 2: Implement Validator Coordination (2 hours)**

Within MyInvoiceMapper:
```csharp
private async Task ValidateAndCollectErrors(MyInvoiceDocument doc, List<ValidationError> errors)
{
    // Mandatory fields
    var mandatoryErrors = _mandatoryValidator.Validate(doc);
    errors.AddRange(mandatoryErrors);
    
    if (mandatoryErrors.Count > 0)
        return; // Stop if mandatory fields missing
    
    // TIN validation
    var tinErrors = await _tinValidator.Validate(doc);
    errors.AddRange(tinErrors);
    
    // Date validation
    var dateErrors = _dateValidator.Validate(doc);
    errors.AddRange(dateErrors);
    
    // Currency validation
    var currencyErrors = _currencyValidator.Validate(doc);
    errors.AddRange(currencyErrors);
    
    // Totals validation
    var totalsErrors = _totalsValidator.Validate(doc);
    errors.AddRange(totalsErrors);
}
```

**Step 3: Implement MandatoryFieldsValidator (2 hours)**

File: `src/Validators/MandatoryFieldsValidator.cs`

Requirements (from ai/memory/03):
- Check 20+ mandatory fields
- Return detailed error messages

Mandatory fields to check:
```csharp
private static readonly string[] MandatoryFields = new[]
{
    nameof(MyInvoiceDocument.InvoiceNumber),
    nameof(MyInvoiceDocument.InvoiceDate),
    nameof(MyInvoiceDocument.SupplierTIN),
    nameof(MyInvoiceDocument.SupplierName),
    nameof(MyInvoiceDocument.BuyerTIN),
    nameof(MyInvoiceDocument.BuyerName),
    nameof(MyInvoiceDocument.LineTotal),
    nameof(MyInvoiceDocument.TaxTotal),
    nameof(MyInvoiceDocument.GrandTotal),
    // ... 11 more fields
};

public List<ValidationError> Validate(MyInvoiceDocument doc)
{
    var errors = new List<ValidationError>();
    
    foreach (var fieldName in MandatoryFields)
    {
        var value = doc.GetType().GetProperty(fieldName)?.GetValue(doc);
        if (value == null || (value is string s && string.IsNullOrWhiteSpace(s)))
        {
            errors.Add(new ValidationError
            {
                Field = fieldName,
                Rule = "Mandatory field",
                Message = $"{fieldName} is required"
            });
        }
    }
    
    return errors;
}
```

**Step 4: Implement TINValidator (2 hours)**

File: `src/Validators/TINValidator.cs`

Requirements (from ai/memory/03):
- Validate Malaysia TIN format
- Call MyInvois API for verification (with 1-hour cache)
- Handle cache misses

Code pattern:
```csharp
public async Task<List<ValidationError>> Validate(MyInvoiceDocument doc)
{
    var errors = new List<ValidationError>();
    
    // Check supplier TIN
    if (!ValidateTINFormat(doc.SupplierTIN))
        errors.Add(new ValidationError { Field = "SupplierTIN", Rule = "Format" });
    else if (!(await IsTINValidPerMyInvois(doc.SupplierTIN)))
        errors.Add(new ValidationError { Field = "SupplierTIN", Rule = "MyInvois" });
    
    // Check buyer TIN
    if (!ValidateTINFormat(doc.BuyerTIN))
        errors.Add(new ValidationError { Field = "BuyerTIN", Rule = "Format" });
    else if (!(await IsTINValidPerMyInvois(doc.BuyerTIN)))
        errors.Add(new ValidationError { Field = "BuyerTIN", Rule = "MyInvois" });
    
    return errors;
}

private bool ValidateTINFormat(string tin)
{
    // Malaysia TIN: 12-digit format
    return Regex.IsMatch(tin, @"^\d{12}$");
}

private async Task<bool> IsTINValidPerMyInvois(string tin)
{
    // Check cache first
    if (_cache.TryGetValue(tin, out var cached))
        return cached.IsValid;
    
    // Call MyInvois API to verify
    var result = await _myInvoisClient.VerifyTIN(tin);
    
    // Cache for 1 hour
    _cache[tin] = new CachedTINResult { IsValid = result, ExpiresAt = DateTime.UtcNow.AddHours(1) };
    
    return result;
}
```

### Unit Tests (12+ test cases)

**MyInvoiceMapper tests (8 cases):**
1. [ ] Transform_ValidMovexInvoice_ReturnsDocument
2. [ ] Transform_MissingMandatoryField_ReturnsErrors
3. [ ] Transform_InvalidTIN_ReturnsErrors
4. [ ] Transform_InvalidDate_ReturnsErrors
5. [ ] Transform_MismatchedTotals_ReturnsErrors
6. [ ] Transform_AllValidators_CoordinatedCorrectly
7. [ ] ValidateDocument_ValidDoc_ReturnsTrue
8. [ ] ValidateDocument_InvalidDoc_ReturnsFalse

**MandatoryFieldsValidator tests (4 cases):**
1. [ ] Validate_AllFieldsPresent_ReturnsNoErrors
2. [ ] Validate_MissingInvoiceNumber_ReturnsError
3. [ ] Validate_MissingSupplierTIN_ReturnsError
4. [ ] Validate_EmptyStringField_TreatsAsMissing

**TINValidator tests (4 cases):**
1. [ ] Validate_ValidTIN_ReturnsNoErrors
2. [ ] Validate_InvalidFormat_ReturnsError
3. [ ] Validate_UnregisteredTIN_ReturnsError
4. [ ] Validate_CachedTIN_UsesCache

---

## Task 3: Continue Validators & MyInvoiceSubmitter ✅ COMPLETE

**Owner:** Developer
**Duration:** 1.5 days (Wed afternoon - Thu morning)
**Effort:** 12 hours
**Result:** DateValidator (151 lines) + CurrencyValidator (145 lines) + TotalsValidator (116 lines) + MyInvoiceSubmitter (423 lines)
**Success Criteria:**
- [x] DateValidator implemented (ISO 8601, placeholder detection, future date rejection)
- [x] CurrencyValidator implemented (ISO 4217, exchange rates, decimal precision)
- [x] TotalsValidator implemented (mathematical consistency, ±0.01 tolerance)
- [x] MyInvoiceSubmitter implemented (OAuth, Polly retry, rate limiting; XAdES placeholder — see Task 7)
- [x] 15+ unit tests written & passing
- [x] Code review approved

### Implementation Steps

**Step 1: Implement DateValidator (2 hours)**

File: `src/Validators/DateValidator.cs`

Requirements (from ai/memory/03):
- Reject placeholder dates (1900-01-01, etc.)
- Ensure UTC format
- Validate leap years
- Check date ranges (invoice date ≤ delivery date ≤ due date)

**Step 2: Implement CurrencyValidator (2 hours)**

File: `src/Validators/CurrencyValidator.cs`

Requirements:
- Validate ISO 4217 currency codes (MYR primary)
- Check exchange rates if multi-currency
- Validate amount formats (2 decimal places)

**Step 3: Implement TotalsValidator (2 hours)**

File: `src/Validators/TotalsValidator.cs`

Requirements:
- Verify: LineTotal + TaxTotal = GrandTotal
- Allow ±1 cent tolerance (rounding differences)
- Check line item amounts sum correctly

**Step 4: Implement MyInvoiceSubmitter (4 hours)**

File: `src/Services/MyInvoiceSubmitter.cs`

Interface:
```csharp
public interface IMyInvoiceSubmitter
{
    Task<SubmissionResult> Submit(MyInvoiceDocument doc);
    Task<SubmissionResult> GetSubmissionStatus(string uuid);
}
```

Requirements:
- Get OAuth token for MyInvois
- Sign document with XAdES v1.1 (use SDK)
- Call MyInvois submission endpoint
- Handle error responses per ai/memory/03
- Implement token refresh on 401

Key implementation:
```csharp
public async Task<SubmissionResult> Submit(MyInvoiceDocument doc)
{
    // 1. Get access token
    var token = await GetMyInvoisAccessToken();
    
    // 2. Sign document (XAdES v1.1)
    var signedXml = await SignDocument(doc);
    
    // 3. Submit to MyInvois
    var request = new HttpRequestMessage(HttpMethod.Post, _settings.SubmissionEndpoint);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    request.Content = new StringContent(signedXml, Encoding.UTF8, "application/xml");
    
    var response = await _httpClient.SendAsync(request);
    
    // 4. Parse response
    var result = await ParseSubmissionResponse(response);
    
    return result;
}
```

### Unit Tests (10+ test cases)

**DateValidator tests (3 cases):**
1. [ ] Validate_ValidDates_ReturnsNoErrors
2. [ ] Validate_PlaceholderDate_ReturnsError
3. [ ] Validate_DateRangeInvalid_ReturnsError

**CurrencyValidator tests (3 cases):**
1. [ ] Validate_ValidCurrency_ReturnsNoErrors
2. [ ] Validate_InvalidCurrency_ReturnsError
3. [ ] Validate_TwoDec imals_AcceptsFormat

**TotalsValidator tests (2 cases):**
1. [ ] Validate_MatchingTotals_ReturnsNoErrors
2. [ ] Validate_MismatchedTotals_ReturnsError

**MyInvoiceSubmitter tests (5+ cases):**
1. [ ] Submit_ValidDocument_ReturnsSuccess
2. [ ] Submit_RateLimit429_RetriesAndSucceeds
3. [ ] Submit_ValidationError_ReturnsFailed
4. [ ] Submit_TokenExpired_RefreshesAndRetries
5. [ ] GetSubmissionStatus_ValidUUID_ReturnsStatus

---

## Task 4: AuditLogger & Final Integration ✅ COMPLETE

**Owner:** Developer
**Duration:** 1 day (Thu afternoon)
**Effort:** 8 hours
**Result:** AuditLogger (202 lines) + InvoiceProcessor (300 lines)
**Success Criteria:**
- [x] AuditLogger implemented (SQL Server persistence, duplicate detection)
- [x] InvoiceProcessor orchestrator wired (batch + single processing)
- [x] 10+ unit tests written & passing (10 AuditLogger + 9 InvoiceProcessor)
- [x] Code review approved

### Implementation Steps

**Step 1: Implement AuditLogger (3 hours)**

File: `src/Services/AuditLogger.cs`

Interface:
```csharp
public interface IAuditLogger
{
    Task LogSubmission(MyInvoiceDocument doc, SubmissionResult result);
    Task<bool> IsInvoiceAlreadySubmitted(string myInvoisUUID);
    Task<List<FailedSubmission>> GetFailedSubmissions(int month);
}
```

Requirements:
- Log to SQL Server [dbo].[AuditLog] table
- Detect duplicates by MyInvois UUID
- Provide failed submission queries

**Step 2: Wire InvoiceProcessor (3 hours)**

File: `src/Services/InvoiceProcessor.cs`

Orchestrator flow:
```csharp
public async Task<BatchResult> ProcessMonthlyBatch(int year, int month)
{
    var result = new BatchResult();
    
    // 1. Fetch pending invoices
    var invoices = await _movexReader.GetPendingInvoices(year, month);
    result.Total = invoices.Count;
    
    // 2. Process each invoice
    foreach (var invoice in invoices)
    {
        try
        {
            // 3. Transform & validate
            var (doc, errors) = await _mapper.Transform(invoice);
            
            if (errors.Count > 0)
            {
                result.Failed++;
                await _auditLogger.LogSubmission(doc, new SubmissionResult { Success = false, Errors = errors });
                continue;
            }
            
            // 4. Submit to MyInvois
            var submitResult = await _submitter.Submit(doc);
            
            if (submitResult.Success)
                result.Success++;
            else
                result.Failed++;
            
            // 5. Log result
            await _auditLogger.LogSubmission(doc, submitResult);
        }
        catch (Exception ex)
        {
            result.Failed++;
            _logger.LogError(ex, "Error processing invoice");
        }
    }
    
    return result;
}
```

### Unit Tests (10+ test cases)

**AuditLogger tests (5 cases):**
1. [ ] LogSubmission_SuccessfulSubmission_LogsToDatabase
2. [ ] LogSubmission_FailedSubmission_LogsError
3. [ ] IsInvoiceAlreadySubmitted_DuplicateUUID_ReturnsTrue
4. [ ] IsInvoiceAlreadySubmitted_NewUUID_ReturnsFalse
5. [ ] GetFailedSubmissions_MultipleFailures_ReturnsList

**InvoiceProcessor tests (5+ cases):**
1. [ ] ProcessMonthlyBatch_AllValid_ReturnsSuccess
2. [ ] ProcessMonthlyBatch_SomeInvalid_ProcessesRest
3. [ ] ProcessMonthlyBatch_ApiError_LogsAndContinues
4. [ ] ProcessSingleInvoice_ValidInvoice_ReturnsResult
5. [ ] RetryFailedInvoice_RetryableError_Retries

---

## Task 5: Integration Testing & Coverage ✅ COMPLETE

**Owner:** Developer
**Duration:** 1 day (Fri morning-noon) + Sprint 3 Mon-Tue
**Effort:** 16 hours (extended into Sprint 3)
**Result:** 225 total tests passing — 184 unit + 35 integration + 5 E2E + 1 infra
**Success Criteria:**
- [x] All 54+ unit tests implemented (actual: 184 unit tests — 341%)
- [x] Code coverage ≥80%
- [x] All tests passing (225/225 — 100% pass rate)
- [x] Zero critical defects
- [x] Code review approved

### Steps

1. [x] Run full test suite: `dotnet test` — 225 passing
2. [x] Check coverage — ≥80% achieved
3. [x] Integration tests: 35 (MyInvois API, DB2, SQL Server, component)
4. [x] E2E tests: 5 (happy path, mixed batch, duplicates, audit, empty batch)
5. [x] DirectQueryDataSource fully implemented (Sprint 3 Mon-Tue)
6. [x] ADR-013 gaps #2 and #3 closed

### Sprint 2 Final Checklist ✅

- [x] All 5 services implemented
- [x] All 5 validators implemented
- [x] 225 tests written (415% of target)
- [x] Coverage ≥80% across all code
- [x] All tests passing
- [x] Zero critical defects
- [x] Code review approved
- [x] Integration and E2E testing complete (Sprint 3)

---

# Sprint 3: Go-Live Blocker Resolution (Feb 20-21)

## Task 6: Implement MovexMasterPartyDataProvider ⏳ PENDING

**Owner:** Developer
**Duration:** 4-6 hours (Thu Feb 20)
**Priority:** CRITICAL — blocks production invoice validation
**Success Criteria:**
- [ ] Supplier data retrieval from CIDMAS table
- [ ] Customer data retrieval from OCUSMA table
- [ ] DI registration swapped from Placeholder → MovexMaster
- [ ] Unit + integration tests passing
- [ ] Party data flows through full pipeline (Reader → Mapper → Validators)

### Prerequisites
- [x] DBA confirmed CIDMAS/OCUSMA table structures
- [ ] Read `src/Database/DB2_PartyData_Reference.sql` for SQL patterns

### Implementation Steps

**Step 1: Implement CIDMAS supplier queries (2 hours)**

File: `src/MyInvois.Service/DataAccess/MovexMasterPartyDataProvider.cs`

- Replace `NotImplementedException` at line ~39 with real CIDMAS query
- Use Dapper+ODBC pattern from `DirectQueryDataSource.cs`
- Query fields: Supplier TIN, BRN, Name, Address (street, city, state, postcode, country)
- Use `MovexDbSettings` for connection string and company schema mapping

**Step 2: Implement OCUSMA customer queries (2 hours)**

Same file, replace `NotImplementedException` at line ~51:
- Query fields: Customer TIN, BRN, Name, Address
- Same Dapper+ODBC pattern

**Step 3: Swap DI registration (30 min)**

File: `src/MyInvois.Service/DataAccess/ServiceCollectionExtensions.cs`
- Change `IPartyDataProvider` registration from `PlaceholderPartyDataProvider` → `MovexMasterPartyDataProvider`
- Keep `PlaceholderPartyDataProvider` available for dev/test via config flag

**Step 4: Update tests (1 hour)**

- Update existing party data tests to expect real data patterns
- Add integration test: full pipeline with real party data
- Verify validators receive non-null TIN/BRN/Address

### Code Review Checklist
- [ ] Uses parameterized Dapper queries (no SQL injection)
- [ ] Connection string from User Secrets (no hardcoded credentials)
- [ ] Company schema mapping matches DirectQueryDataSource pattern
- [ ] Handles missing party records gracefully (log warning, don't crash)

---

## Task 7: Integrate MyInvois SDK (XAdES + UBL) ⏳ PENDING

**Owner:** Developer
**Duration:** 4-6 hours (Thu Feb 20)
**Priority:** CRITICAL — blocks production invoice submission
**Success Criteria:**
- [ ] MyInvois SDK NuGet package added
- [ ] UBL 2.1 serialization replacing TODO at line ~265
- [ ] XAdES v1.1 signing replacing TODO at line ~284
- [ ] SDK wired into DI
- [ ] Submitter tests updated with SDK mocks

### Implementation Steps

**Step 1: Add MyInvois SDK dependency (30 min)**

File: `src/MyInvois.Service/MyInvois.Service.csproj`
- Add MyInvois SDK NuGet package
- Verify compatibility with .NET 8.0

**Step 2: Implement UBL serialization (2 hours)**

File: `src/MyInvois.Service/Services/MyInvoiceSubmitter.cs`
- Replace TODO at line ~265 with SDK serialization call
- Transform `MyInvoiceDocument` → UBL 2.1 XML using SDK
- Validate output against MyInvois schema

**Step 3: Implement XAdES signing (2 hours)**

Same file, replace TODO at line ~284:
- Use SDK to apply XAdES v1.1 signature
- Configure signing certificate (from User Secrets or certificate store)
- Validate signature format per MyInvois requirements

**Step 4: Wire SDK into DI (30 min)**

File: `src/MyInvois.Service/DataAccess/ServiceCollectionExtensions.cs`
- Register SDK services in DI container
- Configure SDK settings from `MyInvoisApiSettings`

**Step 5: Update tests (1 hour)**

File: `tests/MyInvois.Service.Tests/Services/MyInvoiceSubmitterTests.cs`
- Mock SDK interfaces for unit tests
- Add test: valid document → signed XML output
- Add test: signing failure → appropriate error handling

---

## Task 8: Implement TIN API Validation ⏳ PENDING

**Owner:** Developer
**Duration:** 2-3 hours (Fri Feb 21)
**Priority:** HIGH — TIN validation currently throws NotImplementedException
**Success Criteria:**
- [ ] Real MyInvois TIN lookup API call implemented
- [ ] 1-hour IMemoryCache TTL for TIN results
- [ ] API errors handled gracefully (timeout → pass with warning)
- [ ] Unit tests with mocked HTTP calls
- [ ] Cache behavior tested

### Implementation Steps

**Step 1: Implement TIN API call (1.5 hours)**

File: `src/MyInvois.Service/Validators/TINValidator.cs`
- Replace `NotImplementedException` at line ~93
- Use `IHttpClientFactory` (already registered) to create named client
- Call MyInvois TIN verification endpoint: `GET /api/v1.0/taxpayer/validate/{tin}`
- Parse response: valid/invalid/not-found
- On API timeout or error: pass validation with warning log (don't block submission)

**Step 2: Add caching (30 min)**

Same file:
- Inject `IMemoryCache`
- Cache key: `TIN:{tin_value}`
- TTL: 1 hour (`MemoryCacheEntryOptions.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)`)
- Check cache before API call
- Store result after successful API call

**Step 3: Update tests (1 hour)**

File: `tests/MyInvois.Service.Tests/Validators/TINValidatorTests.cs`
- Add test: valid TIN → API returns true → no errors
- Add test: invalid TIN → API returns false → validation error
- Add test: cached TIN → no API call made
- Add test: API timeout → pass with warning (graceful degradation)
- Add test: cache expiry → fresh API call

---

## Task 9: Performance Baseline & UAT Preparation ⏳ PENDING

**Owner:** Developer + QA
**Duration:** 4-6 hours (Fri Feb 21)
**Priority:** HIGH — UAT must start Mon Feb 24
**Success Criteria:**
- [ ] Per-invoice processing time measured (<5s target)
- [ ] Batch throughput measured (100 invoices/batch target)
- [ ] 50 test invoices prepared (mix AP/AR from CMP300)
- [ ] UAT test plan documented
- [ ] Finance test environment configured

### Implementation Steps

**Step 1: Performance baseline (2 hours)**

- Run batch processing against CMP300 dev environment
- Measure: time per invoice, total batch time, API latency
- Identify bottlenecks if >5s per invoice
- Document baseline metrics in PROJECT_STATUS.md

**Step 2: Prepare test invoices (1 hour)**

- Extract 50 invoices from CMP300 (25 AP + 25 AR)
- Verify each has valid party data (from new MovexMasterPartyDataProvider)
- Document expected results per invoice (pass/fail and why)

**Step 3: Create UAT test plan (1 hour)**

- Define test scenarios for Finance team
- Create step-by-step execution guide
- Define pass/fail criteria per test case
- Create UAT sign-off template

**Step 4: Finance environment setup (1 hour)**

- Configure Finance team access to test environment
- Verify Finance can view audit logs
- Create monitoring dashboard or query for Finance to track results
- Brief Finance team on UAT process

---

## Common Issues & Solutions

| Issue | Symptom | Solution |
|-------|---------|----------|
| MOVEX DB2 connection failure | Auth/network failure | Check DB2 connection string (from User Secrets) |
| DB2 connection timeout | Timeout after 30s | Check AS/400 server availability, increase timeout |
| DB2 query error | SQL error from AS/400 | Verify table names (fpledg/fsledg/fgledg), check permissions |
| CIDMAS/OCUSMA query fails | Party data missing | Check company schema mapping, verify table permissions |
| TIN validation slow | Timeout waiting | 1-hour IMemoryCache reduces repeated API calls |
| XAdES signing fails | Certificate error | Verify signing certificate in User Secrets/cert store |
| Test flakiness | Random failures | Mock IInvoiceDataSource results (use Moq) |
| Coverage gaps | <80% coverage | Add tests for error cases |

---

**Owner:** Developer
**Status:** Phase 1 Complete (Tasks 1-9) | Phase 2 In Progress (Tasks 10-15 — Sprint 5 active)
**Last Updated:** March 4, 2026

---

---

# Phase 2: Audit Storage Migration (SQLite)

**Initiative:** MVAI-P2
**Sprints:** 5 (Tasks 10-11), 6 (Tasks 12-14), 7 (Task 15)
**Cross-project:** SM-Portal execution-plan.md Phase 2 runs in parallel
**Plan ref:** `C:\Users\hsalazar\.claude\plans\harmonic-napping-hollerith.md` (Story 2)

> **Why this phase exists:** Users requested removal of the SQL Server dependency for audit logs. The service runs on a self-hosted IIS server with <500 invoices/day — SQLite via EF Core is the appropriate embedded replacement. `IAuditLogger` interface is preserved; no consumers are impacted.

---

## Task 10: ADR + AI Context Updates ⏳ IN PROGRESS

**Owner:** architect-system-design
**Sprint:** 5 (Mar 4-7)
**Effort:** 8h
**Backlog refs:** 5.1, 5.2, 5.3, 5.4, 5.5
**Success Criteria:**
- [ ] `ai/evidence/decision-001-sqlite-audit-storage.md` created and Architecture Review sign-off obtained
- [ ] `ai/memory/08-governance-and-decisions.md` updated with ADR reference
- [ ] `ai/memory/09-implementation-decisions.md` updated (ADR-014 added, ADR-002 superseded)
- [ ] `ai/patterns/audit-logging.md` updated with SQLite/EF Core pattern
- [ ] No code changes made this task

### Prerequisites
- [ ] Read `ai/evidence/` existing files to match numbering convention
- [ ] Read `ai/memory/09-implementation-decisions.md` to understand current ADR numbering (next is ADR-014)
- [ ] Read `ai/patterns/audit-logging.md` to understand current SQL Server pattern

### Step 1: Create decision-001-sqlite-audit-storage.md (3h)

File: `ai/evidence/decision-001-sqlite-audit-storage.md`

Required sections:
```markdown
# Decision 001: SQLite Audit Storage via EF Core

**Date:** March 4, 2026
**Status:** Proposed → Accepted
**Authors:** architect-system-design
**Supersedes:** ADR-002 (SQL Server Audit)

## Context
[Users requested SQL Server dependency removal; <500 invoices/day; self-hosted IIS]

## Decision
Replace System.Data.SqlClient + ADO.NET with Microsoft.Data.Sqlite + EF Core 8 (Code-First).
Per-project SQLite file. NOT shared with SM-Portal.

## Topology: Centralized vs Per-Service Considered
[Document reasoning: MyInvois is independent batch, different schema, must work without SM-Portal]

## Options Considered
[SQL Server LocalDB, SQL Server Express, LiteDB, SQLite — why each was rejected/chosen]

## Consequences
[No TDE → BitLocker; WAL mode; NTFS ACL on ./data/audit.db; 7-year retention maintained]

## Compliance Notes
[ISO 27001 — OS-level BitLocker satisfies encryption at rest; retention managed by file growth budget]

## Skills
architecture/audit-logging-framework v1.0+
architecture/configuration-management v1.0+
```

### Step 2: Update ai/memory/09-implementation-decisions.md (1h)

Add after the last ADR entry:
```markdown
### ADR-014: SQLite Audit Storage via EF Core (March 4, 2026)
**Supersedes:** ADR-002 (SQL Server Audit)
**Decision:** Use Microsoft.Data.Sqlite + Microsoft.EntityFrameworkCore.Sqlite 8.0.*
**Pattern:** Code-First, EnsureCreated() on startup, WAL mode PRAGMA
**File path:** Data Source=./data/audit.db (relative to AppContext.BaseDirectory)
**Type mappings:** GUID → TEXT (string), DATETIME2(7) → TEXT (ISO 8601)
**DI:** IDbContextFactory<AuditDbContext> for thread-safe per-operation context
**Test pattern:** Data Source=:memory: replaces Mock<IDbConnection>
**Evidence:** ai/evidence/decision-001-sqlite-audit-storage.md
```

### Step 3: Update ai/patterns/audit-logging.md (1h)

Replace the SQL Server / ADO.NET section. New pattern:
```markdown
## Audit Logging Pattern: SQLite via EF Core (Phase 2+)

### Setup
// AuditDbContext.cs
public class AuditDbContext : DbContext
{
    public DbSet<AuditLogEntity> AuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLogEntity>()
            .HasCheckConstraint("CK_AuditLog_Status",
                "Status IN ('Success', 'Failed', 'Pending', 'Cancelled')")
            .HasIndex(x => x.InvoiceNumber)
                .HasFilter("\"InvoiceNumber\" IS NOT NULL");
    }
}

// Startup: EnsureCreated + WAL mode
using var ctx = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
ctx.Database.EnsureCreated();
ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");

### Insert (AuditLogger.cs)
using var ctx = _factory.CreateDbContext();
ctx.AuditLogs.Add(entity);
await ctx.SaveChangesAsync(cancellationToken);

### Query (duplicate detection)
return await ctx.AuditLogs
    .AnyAsync(x => x.InvoiceNumber == invoiceNumber && x.Status == "Success", ct);

### Test setup (in-memory SQLite)
var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
var options = new DbContextOptionsBuilder<AuditDbContext>()
    .UseSqlite(connection).Options;
using var ctx = new AuditDbContext(options);
ctx.Database.EnsureCreated();
```

### Code Review Checklist (Task 10)
- [ ] ADR format matches existing decision records in `ai/evidence/`
- [ ] ADR-014 number is correct (verify against existing ADR list in memory/09)
- [ ] No code changes made — documentation only

---

## Task 11: NuGet Package Swap + SQLite Data Layer ⏳ PLANNED

**Owner:** developer-dotnet
**Sprint:** 6 (Mar 10 — Monday)
**Effort:** 8h (Tasks 6.1, 6.2, 6.3, 6.3b)
**Backlog refs:** 6.1, 6.2, 6.3, 6.3b

### Prerequisites
- [ ] Sprint 5 Architecture Review sign-off obtained (Task 10.5 complete)
- [ ] Read `src/Database/create-audit-table.sql` — full 45-column schema to replicate
- [ ] Read `ai/patterns/audit-logging.md` updated version (from Task 10)

### Step 1: NuGet Package Changes (1h)

File: `src/MyInvois.Service/MyInvois.Service.csproj`

Remove:
```xml
<PackageReference Include="System.Data.SqlClient" Version="4.9.0" />
```

Add:
```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.*">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

Verify: `dotnet restore` succeeds, `dotnet build` green.

### Step 2: Create AuditLogEntity.cs (3h)

File: `src/MyInvois.Service/Data/AuditLogEntity.cs`

All 45 columns from `create-audit-table.sql`, mapped to C# types:

```csharp
namespace MyInvois.Service.Data;

public class AuditLogEntity
{
    // Primary key — stored as TEXT in SQLite
    public string AuditId { get; set; } = Guid.NewGuid().ToString();

    // Timestamp — stored as TEXT ISO 8601
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");

    // Actor
    public string? UserId { get; set; }
    public string? UserRole { get; set; }
    public string? IpAddress { get; set; }

    // Action
    public string Action { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info"; // Info | Warning | Error | Critical

    // Resource
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string? Endpoint { get; set; }

    // Result
    public string Status { get; set; } = "Pending"; // Success | Failed | Pending | Cancelled
    public string? StatusCode { get; set; }
    public string? ErrorMessage { get; set; }

    // Payloads
    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }

    // Metadata
    public string? CorrelationId { get; set; }  // Guid stored as TEXT
    public int? Duration { get; set; }
    public int RetryCount { get; set; } = 0;

    // MyInvois-specific
    public string? MyInvoisUUID { get; set; }
    public string? MyInvoisStatus { get; set; }
    public string? MyInvoisSubmissionId { get; set; }

    // MOVEX-specific
    public string? InvoiceNumber { get; set; }
    public string? InvoiceDate { get; set; }  // DATE stored as TEXT
    public string? InvoiceType { get; set; }

    // Financial
    public decimal? TotalAmount { get; set; }
    public decimal? TotalTax { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? ExchangeRate { get; set; }

    // Validation
    public string? ValidationErrors { get; set; }  // JSON array
    public string? SubmissionBatchId { get; set; } // Guid stored as TEXT
}
```

### Step 3: Create AuditDbContext.cs (2h)

File: `src/MyInvois.Service/Data/AuditDbContext.cs`

```csharp
namespace MyInvois.Service.Data;

public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<AuditLogEntity> AuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLogEntity>(entity =>
        {
            entity.HasKey(e => e.AuditId);
            entity.Property(e => e.AuditId).ValueGeneratedNever(); // we set it in entity init

            // CHECK constraints (SQLite supports these)
            entity.HasCheckConstraint("CK_AuditLog_Status",
                "\"Status\" IN ('Success', 'Failed', 'Pending', 'Cancelled')");
            entity.HasCheckConstraint("CK_AuditLog_Severity",
                "\"Severity\" IN ('Info', 'Warning', 'Error', 'Critical')");

            // Partial indexes (SQLite WHERE clause — supported since 3.8.9)
            entity.HasIndex(e => new { e.Status, e.Timestamp })
                .HasFilter("\"Status\" != 'Success'")
                .HasDatabaseName("IX_AuditLog_Status");

            entity.HasIndex(e => new { e.MyInvoisUUID, e.Timestamp })
                .HasFilter("\"MyInvoisUUID\" IS NOT NULL")
                .HasDatabaseName("IX_AuditLog_MyInvoisUUID");

            entity.HasIndex(e => new { e.InvoiceNumber, e.Timestamp })
                .HasFilter("\"InvoiceNumber\" IS NOT NULL")
                .HasDatabaseName("IX_AuditLog_InvoiceNumber");

            entity.HasIndex(e => new { e.SubmissionBatchId, e.Timestamp })
                .HasFilter("\"SubmissionBatchId\" IS NOT NULL")
                .HasDatabaseName("IX_AuditLog_SubmissionBatch");

            // Standard indexes
            entity.HasIndex(e => e.Timestamp).HasDatabaseName("IX_AuditLog_Timestamp");
            entity.HasIndex(e => new { e.UserId, e.Timestamp }).HasDatabaseName("IX_AuditLog_User");
            entity.HasIndex(e => new { e.Action, e.Timestamp }).HasDatabaseName("IX_AuditLog_Action");
        });
    }
}
```

### Step 4: Create AuditDbContextFactory.cs (30min)

File: `src/MyInvois.Service/Data/AuditDbContextFactory.cs`

```csharp
namespace MyInvois.Service.Data;

// Required for: dotnet ef migrations add InitialCreate
public class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite("Data Source=./data/audit.db")
            .Options;
        return new AuditDbContext(options);
    }
}
```

### Step 5: Generate EF Core Migration (30min)

From `src/MyInvois.Service` directory:
```bash
dotnet ef migrations add InitialCreate --output-dir Migrations
```

Review the generated `Migrations/YYYYMMDD_InitialCreate.cs` — verify:
- All 45 columns present
- Types are SQLite-compatible (TEXT, REAL, INTEGER, NUMERIC)
- CHECK constraints present
- Partial indexes present (verify `WHERE` clause syntax)

### Definition of Done (Task 11)
- [ ] `dotnet restore` — no package errors
- [ ] `dotnet build` — 0 errors, 0 warnings
- [ ] `System.Data.SqlClient` no longer in `.csproj`
- [ ] `AuditLogEntity.cs` has all 45 columns from `create-audit-table.sql`
- [ ] `AuditDbContext.cs` has CHECK constraints and all 8 indexes
- [ ] Migration generated and reviewed manually

---

## Task 12: Rewrite AuditLogger.cs (SQLite/EF Core) ⏳ PLANNED

**Owner:** developer-dotnet
**Sprint:** 6 (Mar 11 — Tuesday)
**Effort:** 6h (Task 6.4)
**Backlog ref:** 6.4

### Prerequisites
- [ ] Task 11 complete (AuditDbContext available)
- [ ] Read current `src/MyInvois.Service/Services/AuditLogger.cs` (lines 21-37 = IAuditLogger interface — must not change)
- [ ] Read current `src/MyInvois.Service/Models/SubmissionResult.cs` and `MyInvoiceDocument.cs` for field names

### Implementation

File: `src/MyInvois.Service/Services/AuditLogger.cs`

**Preserve unchanged — IAuditLogger interface (lines 21-37):**
```csharp
public interface IAuditLogger
{
    Task LogSubmission(SubmissionResult result, MyInvoiceDocument? document = null,
        CancellationToken cancellationToken = default);
    Task<List<SubmissionResult>> GetFailedSubmissions(int maxResults = 100,
        CancellationToken cancellationToken = default);
    Task<bool> IsInvoiceAlreadySubmitted(string invoiceNumber,
        CancellationToken cancellationToken = default);
}
```

**New AuditLogger implementation:**

```csharp
public class AuditLogger : IAuditLogger
{
    // Uses skill: architecture/audit-logging-framework v1.0+ (SQLite variant — see ADR-014)
    // Uses skill: architecture/configuration-management v1.0+
    // ISO 27001 compliant: 7-year retention, immutable log entries
    // Replaces ADO.NET/SqlClient with EF Core + SQLite (see decision-001)

    private readonly IDbContextFactory<AuditDbContext> _factory;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(IDbContextFactory<AuditDbContext> factory, ILogger<AuditLogger> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LogSubmission(SubmissionResult result, MyInvoiceDocument? document = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Logging submission for invoice {InvoiceNumber}, Status: {Status}",
            result.InvoiceNumber, result.Status);
        try
        {
            using var ctx = _factory.CreateDbContext();
            ctx.AuditLogs.Add(new AuditLogEntity
            {
                AuditId       = Guid.NewGuid().ToString(),
                Timestamp     = DateTime.UtcNow.ToString("O"),
                Action        = "MyInvois_Submit",
                Category      = "MyInvois",
                Severity      = result.Status == "Failed" ? "Error" : "Info",
                ResourceType  = "Invoice",
                ResourceId    = result.InvoiceNumber ?? string.Empty,
                Status        = result.Status ?? "Pending",
                ErrorMessage  = result.ErrorMessage,
                ResponsePayload = result.RawResponse,
                InvoiceNumber = result.InvoiceNumber,
                MyInvoisUUID  = result.MyInvoisUUID,
                TotalAmount   = document?.TotalInclTax,
                // ... map remaining fields from document
            });
            await ctx.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Audit log entry created for invoice {InvoiceNumber}",
                result.InvoiceNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for invoice {InvoiceNumber}",
                result.InvoiceNumber);
            throw;
        }
    }

    public async Task<bool> IsInvoiceAlreadySubmitted(string invoiceNumber,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Checking duplicate for invoice {InvoiceNumber}", invoiceNumber);
        try
        {
            using var ctx = _factory.CreateDbContext();
            return await ctx.AuditLogs
                .AnyAsync(x => x.InvoiceNumber == invoiceNumber && x.Status == "Success",
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check duplicate for invoice {InvoiceNumber}",
                invoiceNumber);
            throw;
        }
    }

    public async Task<List<SubmissionResult>> GetFailedSubmissions(int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying failed submissions (max: {MaxResults})", maxResults);
        try
        {
            using var ctx = _factory.CreateDbContext();
            var entities = await ctx.AuditLogs
                .Where(x => x.Status == "Failed" && x.Category == "MyInvois")
                .OrderByDescending(x => x.Timestamp)
                .Take(maxResults)
                .ToListAsync(cancellationToken);

            return entities.Select(e => new SubmissionResult
            {
                InvoiceNumber = e.InvoiceNumber,
                Status        = e.Status,
                ErrorCode     = e.StatusCode,
                ErrorMessage  = e.ErrorMessage,
                SubmittedAt   = DateTime.Parse(e.Timestamp)
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query failed submissions");
            throw;
        }
    }
}
```

### Definition of Done (Task 12)
- [ ] `IAuditLogger` interface (lines 21-37) **byte-for-byte unchanged**
- [ ] `IDbConnection` completely removed — no `using System.Data`
- [ ] `EnsureConnectionOpen()` and `AddParameter()` helpers removed
- [ ] All 3 methods fully async (no `await Task.CompletedTask` workarounds)
- [ ] XML doc comments updated (SQL Server → SQLite references)
- [ ] `dotnet build` — 0 errors, 0 warnings after this task

---

## Task 13: DI Registration + Configuration Updates ⏳ PLANNED

**Owner:** developer-dotnet
**Sprint:** 6 (Mar 12 — Wednesday)
**Effort:** 3h (Tasks 6.5, 6.6, 6.6b)
**Backlog refs:** 6.5, 6.6, 6.6b

### Step 1: Update ServiceCollectionExtensions.cs (2h)

File: `src/MyInvois.Service/DataAccess/ServiceCollectionExtensions.cs`

Remove:
```csharp
// OLD: IDbConnection registration (entire block)
services.AddTransient<IDbConnection>(_ =>
    new SqlConnection(configuration.GetConnectionString("AuditLog")));
```

Add:
```csharp
// NEW: EF Core + SQLite
var auditConnectionString = configuration.GetConnectionString("AuditLog")
    ?? "Data Source=./data/audit.db";

services.AddDbContextFactory<AuditDbContext>(options =>
    options.UseSqlite(auditConnectionString));

// Ensure ./data/ directory exists and apply schema + WAL mode on startup
services.AddHostedService<AuditDbStartupService>();
```

Create: `src/MyInvois.Service/Data/AuditDbStartupService.cs`
```csharp
// One-shot IHostedService that runs EnsureCreated() + WAL mode on app startup
public class AuditDbStartupService : IHostedService
{
    private readonly IDbContextFactory<AuditDbContext> _factory;
    private readonly ILogger<AuditDbStartupService> _logger;

    public async Task StartAsync(CancellationToken ct)
    {
        using var ctx = _factory.CreateDbContext();
        // Ensure ./data/ directory exists
        var dbPath = ctx.Database.GetDbConnection().DataSource;
        if (!string.IsNullOrEmpty(dbPath) && dbPath != ":memory:")
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        ctx.Database.EnsureCreated();
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        _logger.LogInformation("Audit SQLite database ready at {Path}", dbPath);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

### Step 2: Update appsettings (30min)

`appsettings.json` — keep key, update comment:
```json
"ConnectionStrings": {
  "AuditLog": "{{FROM_USER_SECRETS}}"
}
```

`appsettings.Development.json`:
```json
"ConnectionStrings": {
  "AuditLog": "Data Source=./data/audit.db"
}
```

### Step 3: Create SQLite DDL Reference Script (30min)

File: `src/Database/create-audit-table-sqlite.sql`
- SQLite DDL equivalent of `create-audit-table.sql`
- For manual schema recovery if `.db` file is lost/corrupted
- Uses `TEXT`/`REAL`/`INTEGER`/`NUMERIC` types; no `GO` statements; no `USE` statement
- Include `PRAGMA journal_mode=WAL;` at top

### Definition of Done (Task 13)
- [ ] `IDbConnection` DI registration removed entirely
- [ ] `AuditDbContextFactory` registered for DI
- [ ] `AuditDbStartupService` creates `./data/` dir on first run
- [ ] `appsettings.Development.json` uses SQLite connection string
- [ ] SQLite DDL reference script created
- [ ] `dotnet build` — 0 errors, 0 warnings

---

## Task 14: Update Integration Tests (SQLite) ⏳ PLANNED

**Owner:** developer-dotnet
**Sprint:** 6 (Mar 13-14)
**Effort:** 4h (Tasks 6.7, 6.8)
**Backlog refs:** 6.7, 6.8

### Prerequisites
- [ ] Tasks 11-13 complete
- [ ] Read current `tests/Integration/AuditLoggerIntegrationTests.cs` — understand existing test structure

### Test Refactor

File: `tests/MyInvois.Service.Tests/Integration/AuditLoggerIntegrationTests.cs`

Replace test setup:
```csharp
// OLD: Mock<IDbConnection>
var mockConn = new Mock<IDbConnection>();
var sut = new AuditLogger(mockConn.Object, logger);

// NEW: In-memory SQLite via EF Core
private AuditDbContext CreateInMemoryContext()
{
    var connection = new SqliteConnection("Data Source=:memory:");
    connection.Open(); // keep open for lifetime of test
    var options = new DbContextOptionsBuilder<AuditDbContext>()
        .UseSqlite(connection)
        .Options;
    var ctx = new AuditDbContext(options);
    ctx.Database.EnsureCreated(); // applies schema to in-memory DB
    return ctx;
}

private AuditLogger CreateSut(AuditDbContext ctx)
{
    var factory = new Mock<IDbContextFactory<AuditDbContext>>();
    factory.Setup(f => f.CreateDbContext()).Returns(ctx);
    return new AuditLogger(factory.Object, NullLogger<AuditLogger>.Instance);
}
```

Test cases (same as before, now verified against real SQLite):
1. `LogSubmission_SuccessfulSubmission_InsertsRowInSqlite`
2. `LogSubmission_FailedSubmission_InsertsRowWithErrorMessage`
3. `IsInvoiceAlreadySubmitted_DuplicateInvoice_ReturnsTrue`
4. `IsInvoiceAlreadySubmitted_NewInvoice_ReturnsFalse`
5. `GetFailedSubmissions_MultipleFailures_ReturnsOrderedList`

Add after existing tests:
6. `LogSubmission_MultipleInvoices_AllPersisted` — verifies row count
7. `GetFailedSubmissions_WithMaxResults_RespectsLimit` — verifies Take()

### Full Test Run

After refactor:
```bash
dotnet test tests/MyInvois.Service.Tests --logger "console;verbosity=normal"
# All 225+ tests must pass
```

### Definition of Done (Task 14)
- [ ] No `Mock<IDbConnection>` remaining in test file
- [ ] All 7 audit logger test cases pass
- [ ] Full suite: 225+ tests passing (100%)
- [ ] Code review approved by Tech Lead

---

## Task 15: Compliance Documentation + Smoke Test ⏳ PLANNED

**Owner:** expert-myinvois-compliance + Ops Lead
**Sprint:** 7 (Mar 17-21)
**Effort:** 11h (Tasks 7.1-7.5)
**Backlog refs:** 7.1, 7.2, 7.3, 7.4, 7.5

### Step 1: Update docs/DEPLOYMENT.md — BitLocker + NTFS ACL (2h)

Add section after existing deployment steps:

```markdown
## Audit Database — SQLite Setup

### 1. Directory Creation
The application creates `.\data\` automatically on first run.
Verify the directory exists after first startup:
    dir .\data\audit.db

### 2. BitLocker (Required — ISO 27001)
The IIS server volume hosting the application MUST be BitLocker-encrypted.
Verify: `manage-bde -status C:`
If not enabled: escalate to Ops — this is a compliance blocker.

### 3. NTFS ACL — App Pool Identity Only
Run after first deployment:
    icacls ".\data" /inheritance:d
    icacls ".\data" /remove "Everyone" "Users" "Authenticated Users"
    icacls ".\data" /grant "IIS AppPool\MyInvoisService:(OI)(CI)F"
Verify: try to open audit.db as a different user — should get Access Denied.

### 4. WAL Journal File
SQLite creates `audit.db-wal` and `audit.db-shm` during operation.
These are also restricted by the NTFS ACL above (inherited from `.\data`).
Do NOT delete these files while the service is running.
```

### Step 2: Write Backup Runbook (2h)

Add section to `docs/DEPLOYMENT.md`:

```markdown
## Audit Database — Backup & Restore

### Daily Backup (Robocopy)
Schedule via Windows Task Scheduler (daily at 02:00):
    robocopy "C:\inetpub\wwwroot\MyInvois-Service\data" "\\backup-server\myinvois-audit\%DATE%" audit.db /COPYALL /LOG:backup.log

Note: robocopy is safe for SQLite in WAL mode (copies consistent snapshot).
Do NOT use xcopy/copy while service is running — use robocopy only.

### Restore Procedure
1. Stop the IIS Application Pool: `Stop-WebAppPool -Name "MyInvoisService"`
2. Copy backup file: `copy \\backup-server\myinvois-audit\{DATE}\audit.db .\data\audit.db`
3. Delete WAL files if present: `del .\data\audit.db-wal .\data\audit.db-shm`
4. Start the Application Pool: `Start-WebAppPool -Name "MyInvoisService"`
5. Verify: trigger one test submission, confirm row appears in audit.db

### Testing the Restore
Monthly: copy audit.db to a temp path, open with DB Browser for SQLite,
run: SELECT COUNT(*) FROM AuditLog WHERE Status = 'Success'
```

### Step 3: Smoke Test in IIS (2h)

```bash
# 1. Deploy to test IIS
# 2. Run 5 test submissions (use existing smoke test or manual trigger)
dotnet test --filter "Category=Smoke"

# 3. Open audit.db in DB Browser for SQLite
# Query: SELECT * FROM AuditLog ORDER BY Timestamp DESC LIMIT 5
# Verify: 5 rows, Status populated, InvoiceNumber populated

# 4. Verify WAL mode
# Query: PRAGMA journal_mode;
# Expected result: wal

# 5. Verify NTFS ACL
icacls ".\data\audit.db"
# Expected: only IIS AppPool\MyInvoisService listed
```

### Definition of Done (Task 15)
- [ ] `docs/DEPLOYMENT.md` has BitLocker + NTFS ACL section
- [ ] `docs/DEPLOYMENT.md` has backup + restore runbook
- [ ] Security review sign-off from `validator-quality` in `ai/evidence/decision-log.md`
- [ ] `WORKSPACE_RULES.md` updated (SQLite approved for self-hosted IIS)
- [ ] Smoke test: 5 rows confirmed in `audit.db` on test IIS
- [ ] Phase 2 complete — no SQL Server dependency remains

---

## Phase 2 Common Issues & Solutions

| Issue | Symptom | Solution |
|-------|---------|----------|
| `./data/` directory missing | `SqliteException: unable to open database` | `AuditDbStartupService` should create it; verify it runs on startup |
| WAL files (`audit.db-wal`) left behind | Slow queries | Run `PRAGMA wal_checkpoint(TRUNCATE)` manually or let it auto-checkpoint |
| EF Core migration `HasFilter` syntax error | Build error | Use double-quoted column names: `"\"Status\" != 'Success'"` |
| `IDbContextFactory` scope issue | `ObjectDisposedException` in tests | Use `CreateDbContext()` not `CreateDbContextAsync()` in sync test setup |
| CHECK constraint violation | `SqliteException: CHECK constraint failed` | Only values in ('Success','Failed','Pending','Cancelled') allowed for Status |
| In-memory test DB not isolated | Test pollution between runs | Use a new `SqliteConnection("Data Source=:memory:")` per test class |
| Backup fails while service running | File locked | Use `robocopy` (safe) not `copy` — robocopy handles WAL mode correctly |

