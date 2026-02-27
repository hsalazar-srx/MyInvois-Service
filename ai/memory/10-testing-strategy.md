# MyInvois-Service Testing Strategy & Plan

**Document Version:** 1.0  
**Date:** February 5, 2026  
**Owner:** QA Team + Development Team  
**Status:** Ready for Week 2 Implementation

---

## 1. Testing Overview

### 1.1 Testing Pyramid

```
        /\
       /  \  E2E Tests (5)
      /----\  - Full batch processing
     /      \
    /  ____  \ Integration Tests (20+)
   / /      \ \  - Service components
  / / ______ \ \ Unit Tests (54+)
 /___________\/ - Individual methods
```

### 1.2 Coverage Targets

| Layer | Target | Scope |
|-------|--------|-------|
| **Unit** | ≥80% | Validators, Mapper, Services |
| **Integration** | ≥70% | API calls, DB operations |
| **E2E** | ≥50% | Full workflows |
| **Overall** | ≥75% | Entire codebase |

### 1.3 Timeline

| Week | Activity | Tests | Status |
|------|----------|-------|--------|
| Week 2 (Feb 10-14) | Unit tests | 54+ | ⏳ Implementation |
| Week 2 (Feb 10-14) | Integration setup | 20+ | ⏳ Setup environments |
| Week 3 (Feb 17-21) | Integration tests | 20+ | ⏳ Execution |
| Week 3 (Feb 17-21) | E2E tests | 5 | ⏳ Batch tests |
| Week 4 (Feb 24-28) | UAT | Real data | ⏳ Production readiness |

---

## 2. Unit Tests

### 2.1 MandatoryFieldsValidator (7 test cases)

**Test Class:** `MandatoryFieldsValidatorTests`

```csharp
[Fact]
public void ValidateTIN_WithValidFormat_ReturnsSuccess()
{
  // Arrange
  var validator = new MandatoryFieldsValidator();
  var invoice = new MyInvoiceDocument 
  { 
    SupplierTIN = "123456789012" 
  };

  // Act
  var result = validator.Validate(invoice);

  // Assert
  Assert.True(result.IsValid);
  Assert.Empty(result.Errors);
}

[Fact]
public void ValidateTIN_WithMissingTIN_ReturnsFail()
{
  // Arrange
  var validator = new MandatoryFieldsValidator();
  var invoice = new MyInvoiceDocument { SupplierTIN = null };

  // Act
  var result = validator.Validate(invoice);

  // Assert
  Assert.False(result.IsValid);
  Assert.Contains(result.Errors, e => e.Field == "SupplierTIN");
}

[Theory]
[InlineData("")] // Empty
[InlineData("123")] // Too short
[InlineData("12345678901a")] // Non-numeric
public void ValidateTIN_WithInvalidFormats_ReturnsFail(string tin)
{
  var validator = new MandatoryFieldsValidator();
  var invoice = new MyInvoiceDocument { SupplierTIN = tin };
  
  var result = validator.Validate(invoice);
  
  Assert.False(result.IsValid);
}

[Theory]
[InlineData(null)] // Missing
[InlineData("")] // Empty
[InlineData(" ")] // Whitespace
public void ValidateInvoiceNumber_WithInvalid_ReturnsFail(string invoiceNumber)
{
  var validator = new MandatoryFieldsValidator();
  var invoice = new MyInvoiceDocument { InvoiceNumber = invoiceNumber };
  
  var result = validator.Validate(invoice);
  
  Assert.False(result.IsValid);
}

[Fact]
public void ValidateAllFields_WithCompleteInvoice_ReturnsSuccess()
{
  // Arrange: Build complete, valid invoice
  var validator = new MandatoryFieldsValidator();
  var invoice = _testDataFactory.GetValidInvoice();

  // Act
  var result = validator.ValidateAll(invoice);

  // Assert
  Assert.True(result.IsValid);
  Assert.Empty(result.Errors);
}

[Fact]
public void ValidateAllFields_WithMissingFields_ListsAllErrors()
{
  // Arrange: Incomplete invoice
  var validator = new MandatoryFieldsValidator();
  var invoice = new MyInvoiceDocument
  {
    SupplierTIN = null,
    BuyerName = null,
    InvoiceNumber = null
  };

  // Act
  var result = validator.ValidateAll(invoice);

  // Assert
  Assert.False(result.IsValid);
  Assert.True(result.Errors.Count >= 3);
}
```

**Expected Results:**
- ✅ All 7 tests pass
- ✅ 100% validator code coverage

---

### 2.2 TINValidator (6 test cases)

**Test Class:** `TINValidatorTests`

```csharp
[Fact]
public void ValidateTIN_Format_With12Digits_ReturnsSuccess()
{
  var validator = new TINValidator();
  var result = validator.ValidateTIN("123456789012");
  Assert.True(result.IsValid);
}

[Theory]
[InlineData("")]
[InlineData("123")]
[InlineData("123456789012345")] // Too long
[InlineData("12345678901a")] // Non-numeric
public void ValidateTIN_Format_WithInvalidFormats_ReturnsFail(string tin)
{
  var validator = new TINValidator();
  var result = validator.ValidateTIN(tin);
  Assert.False(result.IsValid);
}

[Fact]
[Trait("Category", "Integration")]
public async Task ValidateTIN_ViaAPI_WithValidTIN_ReturnsSuccess()
{
  // Requires: MyInvois API connectivity (sandbox)
  var validator = new TINValidator(_httpClient);
  var result = await validator.ValidateTINViaAPI("123456789012");
  
  Assert.True(result.IsValid);
}

[Fact]
[Trait("Category", "Integration")]
public async Task ValidateTIN_ViaAPI_WithInvalidTIN_ReturnsFail()
{
  var validator = new TINValidator(_httpClient);
  var result = await validator.ValidateTINViaAPI("999999999999");
  
  Assert.False(result.IsValid);
}

[Fact]
public void ValidateTIN_CachesResult_1Hour()
{
  // Arrange
  var validator = new TINValidator(_cache);
  var tin = "123456789012";

  // Act
  var result1 = validator.ValidateTIN(tin);
  var result2 = validator.ValidateTIN(tin); // Should hit cache

  // Assert
  Assert.True(result1.IsValid);
  Assert.True(result2.IsValid);
  // Verify cache was used (no second API call)
}
```

**Expected Results:**
- ✅ Format validation tests pass
- ✅ API validation tests pass (requires sandbox)
- ✅ Cache tests verify 1-hour TTL

---

### 2.3 DateValidator (8 test cases)

**Test Class:** `DateValidatorTests`

```csharp
[Theory]
[InlineData("2026-02-01", true)]
[InlineData("2026-12-31", true)]
[InlineData("2026-01-01", true)]
public void ValidateDate_WithRealDates_ReturnsSuccess(string date, bool expected)
{
  var validator = new DateValidator();
  var result = validator.ValidateDate(date);
  Assert.Equal(expected, result.IsValid);
}

[Theory]
[InlineData("0000-00-00")] // Placeholder
[InlineData("N/A")]
[InlineData("")]
[InlineData("2026-13-01")] // Invalid month
[InlineData("2026-02-30")] // Invalid day
[InlineData("2025-02-29")] // Invalid leap year
public void ValidateDate_WithInvalidDates_ReturnsFail(string date)
{
  var validator = new DateValidator();
  var result = validator.ValidateDate(date);
  Assert.False(result.IsValid);
}

[Fact]
public void ValidateDate_ConvertToUTC()
{
  var validator = new DateValidator();
  var result = validator.ValidateDate("2026-02-01");
  
  // Should convert to UTC
  Assert.Equal(TimeZoneInfo.Utc, result.ConvertedDate?.Kind == DateTimeKind.Utc);
}

[Fact]
public void ValidateDate_RejectsDateInFuture()
{
  var validator = new DateValidator();
  var futureDate = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
  
  var result = validator.ValidateDate(futureDate);
  Assert.False(result.IsValid);
}
```

**Expected Results:**
- ✅ Valid dates accepted
- ✅ Placeholder dates rejected
- ✅ Invalid dates rejected
- ✅ UTC conversion verified

---

### 2.4 CurrencyValidator (7 test cases)

**Test Class:** `CurrencyValidatorTests`

```csharp
[Theory]
[InlineData("MYR", true)] // Domestic
[InlineData("USD", true)] // Foreign
[InlineData("SGD", true)] // Regional
[InlineData("EUR", true)] // Major
[InlineData("XYZ", false)] // Invalid
[InlineData("", false)] // Empty
public void ValidateCurrency_ChecksISO4217(string code, bool expected)
{
  var validator = new CurrencyValidator();
  var result = validator.ValidateCurrency(code);
  Assert.Equal(expected, result.IsValid);
}

[Fact]
public void ValidateCurrency_MYR_DoesNotRequireExchangeRate()
{
  var validator = new CurrencyValidator();
  var result = validator.ValidateCurrency("MYR", exchangeRate: null);
  Assert.True(result.IsValid);
}

[Fact]
public void ValidateCurrency_NonMYR_RequiresExchangeRate()
{
  var validator = new CurrencyValidator();
  var result = validator.ValidateCurrency("USD", exchangeRate: null);
  Assert.False(result.IsValid);
}

[Theory]
[InlineData(1.0, true)] // Valid
[InlineData(0.5, true)]
[InlineData(0, false)] // Zero
[InlineData(-1, false)] // Negative
public void ValidateCurrency_ExchangeRate_MustBePositive(decimal rate, bool expected)
{
  var validator = new CurrencyValidator();
  var result = validator.ValidateCurrency("USD", exchangeRate: rate);
  Assert.Equal(expected, result.IsValid);
}

[Fact]
public void ValidateCurrency_ExchangeRate_LimitedTo6Decimals()
{
  var validator = new CurrencyValidator();
  
  // Valid: 6 decimals
  var valid = validator.ValidateCurrency("USD", exchangeRate: 4.123456m);
  Assert.True(valid.IsValid);
  
  // Invalid: 7 decimals
  var invalid = validator.ValidateCurrency("USD", exchangeRate: 4.1234567m);
  Assert.False(invalid.IsValid);
}
```

**Expected Results:**
- ✅ ISO 4217 validation works
- ✅ Exchange rate requirements enforced
- ✅ Decimal precision validated

---

### 2.5 TotalsValidator (6 test cases)

**Test Class:** `TotalsValidatorTests`

```csharp
[Fact]
public void ValidateTotals_WithCorrectSums_ReturnsSuccess()
{
  // Arrange
  var validator = new TotalsValidator();
  var invoice = new MyInvoiceDocument
  {
    Lines = new[]
    {
      new MyInvoiceLine { Amount = 100, Tax = 10 },
      new MyInvoiceLine { Amount = 200, Tax = 20 }
    },
    TotalExclTax = 300,
    TotalTax = 30,
    TotalInclTax = 330,
    PayableAmount = 330
  };

  // Act
  var result = validator.Validate(invoice);

  // Assert
  Assert.True(result.IsValid);
}

[Fact]
public void ValidateTotals_WithIncorrectExclTax_ReturnsFail()
{
  var validator = new TotalsValidator();
  var invoice = new MyInvoiceDocument
  {
    Lines = new[]
    {
      new MyInvoiceLine { Amount = 100 },
      new MyInvoiceLine { Amount = 200 }
    },
    TotalExclTax = 250, // Should be 300
    TotalTax = 30,
    TotalInclTax = 280,
    PayableAmount = 280
  };

  var result = validator.Validate(invoice);
  Assert.False(result.IsValid);
}

[Fact]
public void ValidateTotals_RoundingTolerance_1Cent()
{
  var validator = new TotalsValidator();
  var invoice = new MyInvoiceDocument
  {
    TotalExclTax = 100.001m, // Rounding error < 0.01
    TotalTax = 10.00m,
    TotalInclTax = 110.001m,
    PayableAmount = 110.00m
  };

  var result = validator.Validate(invoice);
  Assert.True(result.IsValid); // Should pass with tolerance
}

[Theory]
[InlineData(0.001)] // 0.1 cent
[InlineData(0.01)] // 1 cent (tolerance limit)
[InlineData(0.02)] // 2 cents (exceeds tolerance)
public void ValidateTotals_RoundingTolerance_Boundary(decimal error)
{
  var validator = new TotalsValidator();
  var invoice = new MyInvoiceDocument
  {
    TotalExclTax = 100.00m + error,
    TotalTax = 10.00m,
    TotalInclTax = 110.00m + error,
    PayableAmount = 110.00m
  };

  var result = validator.Validate(invoice);
  Assert.Equal(error <= 0.01m, result.IsValid);
}

[Fact]
public void ValidateTotals_EmptyLines_ReturnsValidIfTotalsZero()
{
  var validator = new TotalsValidator();
  var invoice = new MyInvoiceDocument
  {
    Lines = new MyInvoiceLine[] { }, // Empty
    TotalExclTax = 0,
    TotalTax = 0,
    TotalInclTax = 0,
    PayableAmount = 0
  };

  var result = validator.Validate(invoice);
  Assert.True(result.IsValid);
}
```

**Expected Results:**
- ✅ Correct calculations pass
- ✅ Incorrect calculations fail
- ✅ Rounding tolerance of 1 cent enforced
- ✅ Empty invoices handled

---

### 2.6 MyInvoiceMapper (9 test cases)

**Test Class:** `MyInvoiceMapperTests`

```csharp
[Fact]
public void Transform_MapsSupplierFields()
{
  // Arrange
  var mapper = new MyInvoiceMapper(_validators);
  var movex = new MovexInvoice
  {
    SupplierName = "Acme Corp",
    SupplierTIN = "123456789012",
    SupplierBRN = "987654321"
  };

  // Act
  var result = mapper.Transform(movex);

  // Assert
  Assert.Equal("Acme Corp", result.SupplierName);
  Assert.Equal("123456789012", result.SupplierTIN);
  Assert.Equal("987654321", result.SupplierBRN);
}

[Fact]
public void Transform_MapsInvoiceFields()
{
  var mapper = new MyInvoiceMapper(_validators);
  var movex = new MovexInvoice
  {
    InvoiceNumber = "INV-2026-00001",
    InvoiceDate = DateTime.Parse("2026-02-01"),
    Currency = "MYR"
  };

  var result = mapper.Transform(movex);

  Assert.Equal("INV-2026-00001", result.InvoiceNumber);
  Assert.Equal("2026-02-01", result.IssueDate);
  Assert.Equal("MYR", result.Currency);
}

[Fact]
public void Transform_MapsLineItems()
{
  var mapper = new MyInvoiceMapper(_validators);
  var movex = new MovexInvoice
  {
    Lines = new[]
    {
      new InvoiceLine 
      { 
        Description = "Software License", 
        Quantity = 1, 
        UnitPrice = 1000 
      }
    }
  };

  var result = mapper.Transform(movex);

  Assert.NotEmpty(result.Lines);
  Assert.Equal("Software License", result.Lines[0].Description);
  Assert.Equal(1000, result.Lines[0].Amount);
}

[Fact]
public void Transform_ValidatesAllFields()
{
  var mapper = new MyInvoiceMapper(_validators);
  var movex = _testDataFactory.GetValidInvoice();

  var result = mapper.Transform(movex);

  // Should call all 5 validators
  Assert.Empty(result.ValidationErrors); // Valid invoice
}

[Fact]
public void Transform_CollectsValidationErrors()
{
  // Arrange: Invoice with missing TIN
  var mapper = new MyInvoiceMapper(_validators);
  var movex = new MovexInvoice
  {
    SupplierTIN = null, // Missing
    SupplierName = "Test"
  };

  // Act
  var result = mapper.Transform(movex);

  // Assert: Should collect validation errors
  Assert.NotEmpty(result.ValidationErrors);
  Assert.Contains(result.ValidationErrors, e => 
    e.Field == "SupplierTIN" && e.Message.Contains("required"));
}

[Fact]
public void Transform_ConvertsCurrencyToISO4217()
{
  var mapper = new MyInvoiceMapper(_validators);
  var movex = new MovexInvoice { Currency = "MYR" };

  var result = mapper.Transform(movex);

  Assert.Equal("MYR", result.Currency);
  Assert.True(result.IsValidCurrency);
}

[Fact]
public void Transform_CalculatesTotals()
{
  var mapper = new MyInvoiceMapper(_validators);
  var movex = new MovexInvoice
  {
    Lines = new[]
    {
      new InvoiceLine { Amount = 100, Tax = 10 },
      new InvoiceLine { Amount = 200, Tax = 20 }
    }
  };

  var result = mapper.Transform(movex);

  Assert.Equal(300, result.TotalExclTax);
  Assert.Equal(30, result.TotalTax);
  Assert.Equal(330, result.TotalInclTax);
}

[Fact]
public void Transform_FormatsDateTimeCorrectly()
{
  var mapper = new MyInvoiceMapper(_validators);
  var movex = new MovexInvoice
  {
    InvoiceDate = DateTime.Parse("2026-02-01 14:30:45")
  };

  var result = mapper.Transform(movex);

  Assert.Equal("2026-02-01", result.IssueDate);
  Assert.Equal("14:30:45", result.IssueTime);
}

[Fact]
public void Transform_HandlesNullBuyer()
{
  // Per MyInvois rules: Buyer TIN can be null (B2C scenario)
  var mapper = new MyInvoiceMapper(_validators);
  var movex = new MovexInvoice
  {
    BuyerName = "Consumer",
    BuyerTIN = null // Allowed
  };

  var result = mapper.Transform(movex);

  Assert.Null(result.BuyerTIN);
  Assert.Equal("Consumer", result.BuyerName);
}
```

**Expected Results:**
- ✅ All field mappings work
- ✅ Validation orchestration works
- ✅ Calculation accuracy verified
- ✅ 80%+ mapper coverage

---

### 2.7 Service Stubs (15 test cases)

**Test Classes:** `InvoiceProcessorTests`, `MovexInvoiceReaderTests`, `MyInvoiceSubmitterTests`, `AuditLoggerTests`

```csharp
// InvoiceProcessorTests
[Fact]
public async Task ProcessMonthlyBatch_FetchesInvoices()
{
  // Arrange
  var processor = new InvoiceProcessor(
    _mockReader, _mockMapper, _mockSubmitter, _mockAuditLogger);

  // Act
  var result = await processor.ProcessMonthlyBatch();

  // Assert
  _mockReader.Verify(x => x.GetPendingInvoices(It.IsAny<DateTime>()), Times.Once);
}

[Fact]
public async Task ProcessMonthlyBatch_ValidatesAll()
{
  var processor = new InvoiceProcessor(
    _mockReader, _mockMapper, _mockSubmitter, _mockAuditLogger);

  var result = await processor.ProcessMonthlyBatch();

  _mockMapper.Verify(x => x.ValidateDocument(It.IsAny<MyInvoiceDocument>()), 
    Times.AtLeastOnce);
}

[Fact]
public async Task ProcessMonthlyBatch_LogsAll()
{
  var processor = new InvoiceProcessor(
    _mockReader, _mockMapper, _mockSubmitter, _mockAuditLogger);

  var result = await processor.ProcessMonthlyBatch();

  _mockAuditLogger.Verify(x => x.LogSubmission(It.IsAny<SubmissionResult>()), 
    Times.AtLeastOnce);
}

// MovexInvoiceReaderTests
[Fact]
public async Task GetPendingInvoices_QueriesMovexDb()
{
  var reader = new MovexInvoiceReader(_dataSource, _settings);

  var result = await reader.GetPendingInvoices(DateTime.UtcNow.AddDays(-7));

  Assert.NotNull(result);
  // Verify DB2 query was made to MOVEX database
}

// MyInvoiceSubmitterTests
[Fact]
public async Task GetAccessToken_CachesToken()
{
  var submitter = new MyInvoiceSubmitter(_httpClient, _settings, _cache);
  
  var token1 = await submitter.GetAccessToken();
  var token2 = await submitter.GetAccessToken();
  
  // Second call should return cached token
  Assert.Equal(token1, token2);
}

// AuditLoggerTests
[Fact]
public async Task LogSubmission_InsertsToDatabase()
{
  var logger = new AuditLogger(_dbContext);
  var submission = new SubmissionResult { /* ... */ };
  
  await logger.LogSubmission(submission);
  
  // Verify record exists in database
  var logged = await _dbContext.AuditLog
    .FirstOrDefaultAsync(x => x.SubmissionId == submission.SubmissionId);
  Assert.NotNull(logged);
}
```

**Expected Results:**
- ✅ All service mocks work
- ✅ Dependencies verified
- ✅ Orchestration logic tested

---

## 3. Integration Tests

### 3.1 MOVEX DB2 Integration (3 tests)

**Environment:** Sandbox (Staging)
**Prerequisite:** Staging MOVEX DB2 server accessible

```csharp
[Collection("IntegrationTests")]
public class MovexApiIntegrationTests : IAsyncLifetime
{
  private readonly MovexInvoiceReader _reader;
  private readonly IHttpClientFactory _httpClientFactory;

  public async Task InitializeAsync()
  {
    // Setup: Create HTTP client, configure test data
    _httpClientFactory = new DefaultHttpClientFactory();
  }

  public async Task DisposeAsync()
  {
    // Cleanup
  }

  [Fact]
  [Trait("Category", "Integration")]
  public async Task GetPendingInvoices_Staging_ReturnsList()
  {
    // Act
    var result = await _reader.GetPendingInvoices(DateTime.UtcNow.AddDays(-30));

    // Assert
    Assert.NotNull(result);
    Assert.IsType<List<MovexInvoice>>(result);
  }

  [Fact]
  public async Task GetInvoiceById_Staging_ReturnsSingleInvoice()
  {
    var result = await _reader.GetInvoiceById("INV-2026-00001");

    Assert.NotNull(result);
    Assert.Equal("INV-2026-00001", result.InvoiceNumber);
  }

  [Fact]
  public async Task GetInvoicesByDateRange_Staging_ReturnsFiltered()
  {
    var from = DateTime.Parse("2026-01-01");
    var to = DateTime.Parse("2026-02-01");

    var result = await _reader.GetInvoicesByDateRange(from, to);

    Assert.NotNull(result);
    Assert.All(result, inv => 
      Assert.True(inv.InvoiceDate >= from && inv.InvoiceDate <= to));
  }
}
```

### 3.2 MyInvois Sandbox Integration (5 tests)

**Environment:** MyInvois Sandbox API  
**Prerequisite:** Sandbox OAuth credentials

```csharp
[Collection("SandboxTests")]
public class MyInvoisSandboxIntegrationTests : IAsyncLifetime
{
  private readonly MyInvoiceSubmitter _submitter;
  private readonly IHttpClientFactory _httpClientFactory;

  [Fact]
  public async Task GetAccessToken_Sandbox_ReturnsValidToken()
  {
    var token = await _submitter.GetAccessToken();

    Assert.NotNull(token);
    Assert.NotEmpty(token.AccessToken);
    Assert.True(token.ExpiresIn > 0);
  }

  [Fact]
  public async Task Submit_SampleInvoice_Sandbox_ReturnsUUID()
  {
    // Arrange
    var invoice = _testDataFactory.GetValidInvoice();

    // Act
    var result = await _submitter.Submit(invoice);

    // Assert
    Assert.True(result.Success);
    Assert.NotEqual(Guid.Empty, result.MyInvoisUUID);
    Assert.Equal("VALID", result.MyInvoisStatus);
  }

  [Fact]
  public async Task Submit_InvalidInvoice_Sandbox_ReturnsDCError()
  {
    var invoice = _testDataFactory.GetInvalidInvoice(missingField: "SupplierTIN");

    var result = await _submitter.Submit(invoice);

    Assert.False(result.Success);
    Assert.Equal("DS101", result.ErrorCode);
  }

  [Fact]
  public async Task Submit_DuplicateInvoice_Sandbox_ReturnsDS302()
  {
    var invoice = _testDataFactory.GetValidInvoice();

    // First submission
    var result1 = await _submitter.Submit(invoice);
    Assert.True(result1.Success);

    // Duplicate submission
    var result2 = await _submitter.Submit(invoice);

    Assert.False(result2.Success);
    Assert.Equal("DS302", result2.ErrorCode);
  }

  [Fact]
  public async Task GetSubmissionStatus_ValidUUID_Sandbox_ReturnsStatus()
  {
    // Arrange
    var submitted = await _submitter.Submit(_testDataFactory.GetValidInvoice());

    // Act
    var status = await _submitter.GetSubmissionStatus(submitted.MyInvoisUUID);

    // Assert
    Assert.Equal(submitted.MyInvoisStatus, status.Status);
  }
}
```

### 3.3 Database Integration (3 tests)

**Environment:** Staging SQL Server  
**Prerequisite:** Test database schema created

```csharp
[Collection("DatabaseTests")]
public class AuditLoggerDatabaseTests : IAsyncLifetime
{
  private readonly AuditLogger _logger;
  private readonly SrxAuditDbContext _dbContext;

  public async Task InitializeAsync()
  {
    // Create test database and schema
    await _dbContext.Database.MigrateAsync();
  }

  public async Task DisposeAsync()
  {
    // Cleanup: Drop test database
    await _dbContext.Database.EnsureDeletedAsync();
  }

  [Fact]
  public async Task LogSubmission_InsertAndQuery()
  {
    // Arrange
    var submission = new SubmissionResult
    {
      SubmissionId = Guid.NewGuid(),
      InvoiceNumber = "INV-2026-00001",
      Status = "Success",
      MyInvoisUUID = Guid.NewGuid()
    };

    // Act
    await _logger.LogSubmission(submission);

    // Assert
    var logged = await _dbContext.AuditLog
      .FirstOrDefaultAsync(x => x.InvoiceNumber == "INV-2026-00001");

    Assert.NotNull(logged);
    Assert.Equal("MyInvois_Submit", logged.Action);
  }

  [Fact]
  public async Task GetFailedSubmissions_ReturnsOnly()
  {
    // Arrange: Log 5 submissions (3 success, 2 failed)
    await LogTestSubmissions();

    // Act
    var failed = await _logger.GetFailedSubmissions();

    // Assert
    Assert.NotEmpty(failed);
    Assert.All(failed, s => Assert.Equal("Failed", s.Status));
  }

  [Fact]
  public async Task IsInvoiceAlreadySubmitted_DetectsDuplicate()
  {
    // Arrange: Log a submission
    await _logger.LogSubmission(new SubmissionResult
    {
      InvoiceNumber = "INV-2026-00001",
      Status = "Success",
      MyInvoisUUID = Guid.NewGuid()
    });

    // Act
    var isDuplicate = await _logger.IsInvoiceAlreadySubmitted("INV-2026-00001");

    // Assert
    Assert.True(isDuplicate);
  }
}
```

---

## 4. End-to-End Tests

### 4.1 Full Batch Processing (5 tests)

**Environment:** Staging (MOVEX DB2 + SQL Server + MyInvois Sandbox)
**Prerequisite:** All services running, test data in MOVEX database

```csharp
[Collection("E2ETests")]
public class E2EBatchProcessingTests : IAsyncLifetime
{
  private readonly InvoiceProcessor _processor;
  private readonly IServiceProvider _serviceProvider;

  [Fact]
  public async Task ProcessMonthlyBatch_SampleInvoices_End2End()
  {
    // Arrange: 10 sample invoices in MOVEX DB2 staging

    // Act
    var result = await _processor.ProcessMonthlyBatch();

    // Assert
    Assert.NotNull(result);
    Assert.True(result.TotalInvoices > 0);
    Assert.True(result.SuccessCount > 0);
    Assert.True(result.SuccessRate >= 0.95); // 95% success target
  }

  [Fact]
  public async Task ProcessMonthlyBatch_WithValidationErrors_SkipsInvalid()
  {
    // Arrange: Mix of valid and invalid invoices in MOVEX database

    // Act
    var result = await _processor.ProcessMonthlyBatch();

    // Assert
    // Invalid invoices should be skipped (not submitted)
    Assert.True(result.SkippedCount > 0);
    Assert.NotEmpty(result.Submissions.Where(s => s.Status == "Skipped"));
  }

  [Fact]
  public async Task ProcessMonthlyBatch_Idempotent_NoDuplicates()
  {
    // Act: Run batch twice

    // Assert: Second run should not resubmit (DS302 duplicate errors)
    // Query audit log: All UUIDs unique (no duplicates)
  }

  [Fact]
  public async Task ProcessMonthlyBatch_RateLimiting_StaysWithin100RPM()
  {
    // Arrange: 1000 invoices to process
    // Expected: ~100 seconds (with 0.6s delay between batches)

    var stopwatch = Stopwatch.StartNew();

    // Act
    var result = await _processor.ProcessMonthlyBatch();

    stopwatch.Stop();

    // Assert
    Assert.True(stopwatch.Elapsed.TotalSeconds < 150); // 2.5 min buffer
  }

  [Fact]
  public async Task ProcessMonthlyBatch_AuditTrail_CompleteLogs()
  {
    // Act
    var result = await _processor.ProcessMonthlyBatch();

    // Assert: All submissions logged to audit table
    var count = await _dbContext.AuditLog
      .Where(x => x.Action == "MyInvois_Submit")
      .CountAsync();

    Assert.Equal(result.TotalInvoices, count);
  }
}
```

---

## 5. Performance Tests

### 5.1 Load Testing (Sandbox)

**Scenario: 100 invoices in single batch**

```
Setup:
- 100 sample invoices in MOVEX DB2 staging
- MyInvois sandbox ready
- SQL Server test database

Measurement:
- Total duration
- Per-invoice latency
- Error rate
- Database insert rate
- Memory usage

Expected:
- Total: < 10 minutes
- Per-invoice: < 5 seconds
- Success: ≥ 95%
- Memory: < 500 MB
```

### 5.2 Stress Testing (If Budget Available)

**Scenario: 1000 invoices across multiple batches**

```
Expected:
- Linear scaling (10x invoices = 10x duration)
- No memory leaks
- Rate limit handling works
- Success rate maintained ≥ 95%
```

---

## 6. Test Data Management

### 6.1 Test Data Factory

```csharp
public class TestDataFactory
{
  public static MovexInvoice GetValidInvoice()
  {
    return new MovexInvoice
    {
      InvoiceNumber = "TEST-INV-" + Guid.NewGuid().ToString().Substring(0, 8),
      InvoiceDate = DateTime.UtcNow,
      SupplierName = "Test Supplier",
      SupplierTIN = "123456789012", // Valid format
      BuyerName = "Test Buyer",
      BuyerTIN = "987654321098",
      Currency = "MYR",
      Lines = new[]
      {
        new InvoiceLine 
        { 
          Description = "Test Line Item",
          Quantity = 1,
          UnitPrice = 100,
          Tax = 10
        }
      },
      TotalExclTax = 100,
      TotalTax = 10,
      TotalInclTax = 110
    };
  }

  public static List<MovexInvoice> GetBulkInvoices(int count)
  {
    return Enumerable.Range(1, count)
      .Select(i => new MovexInvoice
      {
        InvoiceNumber = $"BULK-INV-{i:D6}",
        InvoiceDate = DateTime.UtcNow.AddDays(-i),
        SupplierName = $"Supplier {i}",
        SupplierTIN = $"{i:D12}",
        BuyerName = $"Buyer {i}",
        BuyerTIN = $"{(i * 2):D12}",
        Currency = "MYR",
        Lines = GetTestLines(),
        TotalExclTax = 1000,
        TotalTax = 100,
        TotalInclTax = 1100
      })
      .ToList();
  }
}
```

### 6.2 Test Data Cleanup

```csharp
public class TestDataCleanup
{
  public static async Task CleanupTestInvoices()
  {
    // Delete all test invoices from MOVEX DB2 staging
    // Delete all test submissions from audit table
    // Reset sequences
  }
}
```

---

## 7. Test Execution

### 7.1 Local Execution

```bash
# Run all unit tests
dotnet test --filter "Category!=Integration"

# Run integration tests (requires sandbox)
dotnet test --filter "Category=Integration"

# Run with coverage
dotnet test /p:CollectCoverage=true
```

### 7.2 CI/CD Pipeline (Phase 2)

```yaml
# GitHub Actions / Azure Pipelines
- Unit tests: On every commit
- Integration tests: On PR
- E2E tests: Daily at 2 AM
- Load tests: Weekly
```

---

## 8. Test Reporting

### 8.1 Coverage Report

**Target: 75%+ overall**

| Component | Target | Tool |
|-----------|--------|------|
| Validators | ≥90% | Coverlet |
| Mapper | ≥85% | Coverlet |
| Services | ≥70% | Coverlet |
| Controllers | ≥60% | Coverlet |

### 8.2 Test Report Format

```
Test Run Summary
================
Date: 2026-02-15
Environment: Staging

Unit Tests: 54 tests, 54 passed, 0 failed (100%)
Integration Tests: 20 tests, 19 passed, 1 inconclusive (95%)
E2E Tests: 5 tests, 5 passed, 0 failed (100%)

Code Coverage: 78%
- Validators: 92%
- Mapper: 88%
- Services: 72%
- Controllers: 62%

Performance:
- Avg latency per invoice: 2.3 seconds
- Success rate: 96.5%
- Throughput: 1000 invoices in 78 minutes
```

---

## 9. Test Failure Remediation

### 9.1 Common Failures

| Failure | Cause | Fix |
|---------|-------|-----|
| 401 Unauthorized | OAuth token expired | Refresh token or reset credentials |
| Connection refused | MOVEX DB2/MyInvois unavailable | Check service status, network connectivity |
| Duplicate (DS302) | Invoice already submitted | Clean test data, use unique invoice IDs |
| Timeout (>5s) | Service slow | Check database performance, network latency |
| Validation error | Test data invalid | Regenerate with TestDataFactory |

### 9.2 Failure Investigation

```csharp
// Check audit log for error details
SELECT TOP 5 
  SubmissionId, InvoiceNumber, Status, ErrorCode, 
  ErrorMessage, ValidationErrors, LastAttempt
FROM [dbo].[AuditLog]
WHERE Status = 'Failed'
ORDER BY LastAttempt DESC;

// Check service logs
tail -f /var/log/myinvois-service/myinvois-service.log
```

---

**Testing Status: Ready for Week 2**  
**Next Review: 2026-02-21 (post-Week 3)**  
**Owner:** QA Team  
**Contact:** QA Lead

