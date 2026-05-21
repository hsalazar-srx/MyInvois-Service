# MyInvois-Service Testing Strategy

**Document Version:** 2.0
**Date:** 2026-05-21
**Owner:** Hector Salazar (Development & Integration Lead)
**Status:** Active — 276 tests, 100% passing (Sprint 9)

---

## 1. Testing Overview

### 1.1 Testing Pyramid (Current State)

```
             /\
            /  \  E2E Tests (~15)
           /----\  BatchProcessingE2ETests
          /      \  Full pipeline with mocked HTTP
         /--------\
        /          \ Integration Tests (~25)
       /            \  MyInvoisSubmissionIntegrationTests
      /              \  MyInvoisMapperTests (integration paths)
     /----------------\
    /                  \ Unit Tests (~236)
   /                    \  All validators, mapper, submitter,
  /______________________\  token service, data source, audit logger
```

**Total: 276 tests, 0 failures, 0 skipped** (as of Sprint 9, May 2026)
**Runtime**: ~8 min 14 s

### 1.2 Coverage Targets

| Layer | Target | Current Status |
|-------|--------|---------------|
| **Unit** | ≥80% | ✅ Achieved (validators, mapper, services) |
| **Integration** | ≥70% | ✅ Achieved |
| **E2E** | ≥50% | ✅ Achieved |
| **Overall** | ≥75% | ✅ Achieved |

### 1.3 Test Project Structure

```
tests/MyInvois.Service.Tests/
├── Services/
│   ├── MyInvoisMapperTests.cs          (unit — mapper + AR/AP coverage)
│   ├── MyInvoiceSubmitterTests.cs      (unit — HTTP mock, Polly retry)
│   ├── MyInvoisTokenServiceTests.cs    (unit — OAuth, caching)
│   ├── AuditLoggerTests.cs             (unit — SQLite EF Core)
│   ├── MandatoryFieldsValidatorTests.cs
│   ├── TINValidatorTests.cs
│   ├── DateValidatorTests.cs
│   ├── CurrencyValidatorTests.cs
│   └── TotalsValidatorTests.cs
├── Integration/
│   ├── MyInvoisSubmissionIntegrationTests.cs  (mocked HTTP, full submitter)
│   ├── InvoiceProcessorIntegrationTests.cs
│   └── TestDataFactory.cs              (shared test fixtures)
├── E2E/
│   └── BatchProcessingE2ETests.cs      (full pipeline, mocked data source + HTTP)
└── DataAccess/
    └── DirectQueryDataSourceTests.cs
```

---

## 2. Key Testing Patterns

### 2.1 HTTP Mocking (MyInvoiceSubmitter / Integration)

All tests that exercise `MyInvoiceSubmitter` mock the `HttpMessageHandler` using `Moq.Protected()`:

```csharp
var httpHandlerMock = new Mock<HttpMessageHandler>();
httpHandlerMock.Protected()
    .Setup<Task<HttpResponseMessage>>("SendAsync",
        ItExpr.Is<HttpRequestMessage>(req =>
            req.RequestUri!.ToString().Contains("documentsubmissions")),
        ItExpr.IsAny<CancellationToken>())
    .ReturnsAsync(new HttpResponseMessage
    {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            submissionUid = "SUB-2026-00001",
            acceptedDocuments = new[]
            {
                new { uuid = "12345678-1234-1234-1234-123456789012",
                      invoiceCodeNumber = "INV-TEST-001" }
            },
            rejectedDocuments = Array.Empty<object>()
        }))
    });
```

**Critical**: The mock must return the LHDN envelope shape (`submissionUid` + `acceptedDocuments[]`), NOT a flat `{ uuid, status }` response. The submitter reads `parsed?.AcceptedDocuments?.FirstOrDefault()?.Uuid`.

### 2.2 Polly Retry — Zero-Delay Injection

Retry tests inject a zero-delay `retrySleepProvider` to avoid 35s waits:

```csharp
var sut = new MyInvoiceSubmitter(
    httpClientFactoryMock.Object,
    Options.Create(apiSettings),
    tokenService,
    loggerMock.Object,
    retrySleepProvider: _ => TimeSpan.Zero);   // bypass Polly backoff in tests
```

Production code uses the default (no parameter) which applies `5s × 2^attempt` (5s, 10s, 20s).

The `CreateSubmitter` helper in integration tests also accepts `retrySleepProvider`:
```csharp
private MyInvoiceSubmitter CreateSubmitter(
    HttpMessageHandler handler,
    Func<int, TimeSpan>? retrySleepProvider = null) { ... }
```

### 2.3 AR vs AP Invoice Coverage

`TestDataFactory` provides distinct fixtures for each invoice type:

```csharp
// AR (Sales) — our company is Supplier, customer is Buyer
//              DocumentTypeCode = "01"
var arInvoice = TestDataFactory.CreateValidSalesInvoice();

// AP (Purchase / Self-Billed) — external vendor is Supplier, our company is Buyer
//                               DocumentTypeCode = "11", zero-rated tax
var apInvoice = TestDataFactory.CreateValidPurchaseInvoice();
```

The AP fixture deliberately uses distinct party data (different TINs, names, BRNs) so that party-swap logic is exercised, not just the `InvoiceType` field.

**Mapper tests covering AR/AP distinction** (in `MyInvoisMapperTests.cs`):
```csharp
[Fact]
void Transform_SalesInvoice_SetsDocumentTypeCode01()

[Fact]
void Transform_PurchaseInvoice_SetsDocumentTypeCode11()

[Fact]
void Transform_PurchaseInvoice_SupplierAndBuyerRolesAreSwappedVsAR()
```

### 2.4 E2E Mixed AR/AP Batch

`BatchProcessingE2ETests.cs` includes a test that submits a mixed batch:
```csharp
[Fact]
async Task E2E_MixedArApBatch_BothTypesSubmittedSuccessfully()
{
    // 2 AR + 2 AP records; all submitted; Times.Exactly(4) verified
}
```

`CreateValidAPRecord` helper uses `InvoiceType = "AP"` and `PartyId = "SUP-001"` resolved via `GetSupplierDetailsAsync`.

### 2.5 OAuth Token Mocking

The `IMyInvoisTokenService` is mocked at the interface level in submitter tests:
```csharp
var tokenServiceMock = new Mock<IMyInvoisTokenService>();
tokenServiceMock
    .Setup(s => s.GetAccessTokenAsync())
    .ReturnsAsync("test-bearer-token");
```

Token service unit tests (`MyInvoisTokenServiceTests.cs`) test the real implementation with a mocked `HttpMessageHandler`.

---

## 3. Test Data Factory

`TestDataFactory` (in `tests/MyInvois.Service.Tests/Integration/`) provides:

| Method | Purpose |
|--------|---------|
| `CreateValidSalesInvoice(id?)` | AR invoice, MYR, 6% SST, Scanfil as Supplier |
| `CreateValidPurchaseInvoice(id?)` | AP invoice, MYR, zero-rated, external vendor as Supplier |
| `CreateMultiLineInvoice(lineCount)` | Sales invoice with N lines, totals recalculated |
| `CreateInvalidInvoice_MissingFields()` | Empty InvoiceNumber, null Supplier/Buyer |
| `CreateInvalidInvoice_MismatchedTotals()` | Valid otherwise but TotalExclTax ≠ line sum |
| `CreateMixedBatch(validCount, invalidCount)` | Combined list for batch processing tests |
| `CreateValidDocument(id?)` | Pre-transformed `MyInvoiceDocument` (skips mapper) |
| `CreateSuccessResult(invoiceNumber)` | `SubmissionResult` with Status=Success, random UUID |
| `CreateFailedResult(invoiceNumber, code, msg)` | `SubmissionResult` with Status=Failed |

---

## 4. Test Execution

### 4.1 Run All Tests

```powershell
cd c:\Projects\MyInvois-Service
dotnet test --configuration Release
```

Expected output:
```
Passed!  - Failed: 0, Passed: 276, Skipped: 0, Total: 276
Duration: ~8 min 14 s
```

### 4.2 Run by Category

```powershell
# Unit tests only (fast)
dotnet test --filter "FullyQualifiedName~Services"

# Integration tests
dotnet test --filter "FullyQualifiedName~Integration"

# E2E tests
dotnet test --filter "FullyQualifiedName~E2E"
```

### 4.3 Stop Stale Test Hosts (if tests hang)

```powershell
Stop-Process -Name "testhost" -Force
```

---

## 5. Test Failure Remediation

### Common Failures

| Symptom | Cause | Fix |
|---------|-------|-----|
| UUID is empty in `SubmissionResult` | Mock response has wrong shape (flat `{uuid}` instead of LHDN envelope) | Use `acceptedDocuments: [{ uuid, invoiceCodeNumber }]` envelope |
| Rate-limit retry test takes 35s | `_sut` uses real Polly delays | Build local submitter with `retrySleepProvider: _ => TimeSpan.Zero` |
| Build fails due to locked DLL | Stale testhost from previous run | `Stop-Process -Name "testhost" -Force` then rebuild |
| AP invoice parties same as AR | `CreateValidPurchaseInvoice` returns AR parties | Use the updated factory — supplier TIN = `C88888888888` (external vendor) |
| DS302 duplicate error in manual UAT | Same invoice submitted twice | Check `AuditLogger.IsAlreadySubmittedAsync` before retry |

### Investigating Audit Log

```sql
-- Check recent submissions (SQLite)
SELECT InvoiceNumber, Status, MyInvoisUUID, ErrorCode, ErrorMessage, SubmittedAt
FROM SubmissionAuditLog
ORDER BY SubmittedAt DESC
LIMIT 20;
```

---

## 6. What NOT to Do

- Do NOT use `_sut` (the default shared instance) in retry/rate-limit tests — it uses real Polly delays
- Do NOT return a flat `{ uuid, status }` response from HTTP mocks — the production code reads the LHDN envelope
- Do NOT share a single `CreateValidPurchaseInvoice` that just swaps `InvoiceType` — party roles must differ
- Do NOT use `dotnet test --no-build` unless you've just built — stale binaries mask fixes

---

**Owner**: Hector Salazar
**Last Updated**: 2026-05-21
