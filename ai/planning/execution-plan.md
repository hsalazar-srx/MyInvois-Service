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
**Status:** Sprint 2 COMPLETE, Sprint 3 In Progress — 3 Go-Live Blockers Pending (Tasks 6-8)
**Last Updated:** February 19, 2026

