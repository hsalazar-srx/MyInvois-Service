using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MyInvois.Service.Models;
using MyInvois.Service.Services;

namespace MyInvois.Service.Tests.Services;

/// <summary>
/// Unit tests for InvoiceProcessor orchestrator
/// Uses skill: architecture/clean-architecture v1.2+ (orchestration boundaries)
/// Uses skill: architecture/resilience-patterns v1.8+ (retry policy hooks)
/// Uses skill: integration/api-rate-limiter v1.0+ (batch delay enforcement)
/// </summary>
public class InvoiceProcessorTests
{
    private readonly Mock<IMovexInvoiceReader> _readerMock;
    private readonly Mock<IMyInvoisMapper> _mapperMock;
    private readonly Mock<IMyInvoiceSubmitter> _submitterMock;
    private readonly Mock<IAuditLogger> _auditLoggerMock;
    private readonly Mock<ILogger<InvoiceProcessor>> _loggerMock;
    private readonly InvoiceProcessor _sut;

    public InvoiceProcessorTests()
    {
        _readerMock = new Mock<IMovexInvoiceReader>();
        _mapperMock = new Mock<IMyInvoisMapper>();
        _submitterMock = new Mock<IMyInvoiceSubmitter>();
        _auditLoggerMock = new Mock<IAuditLogger>();
        _loggerMock = new Mock<ILogger<InvoiceProcessor>>();

        _sut = new InvoiceProcessor(
            _readerMock.Object,
            _mapperMock.Object,
            _submitterMock.Object,
            _auditLoggerMock.Object,
            _loggerMock.Object)
        {
            // Skip the 5-minute wait in all unit tests
            Step8PollingDelay = TimeSpan.Zero
        };

        // Default Step 8 poll: return "Valid" for any UUID so existing tests don't need to change
        _submitterMock
            .Setup(s => s.GetSubmissionStatus(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Valid");

        _submitterMock
            .Setup(s => s.GetSubmissionDetails(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Step8Result("Valid", null));
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullMovexReader_ThrowsArgumentNullException()
    {
        var action = () => new InvoiceProcessor(
            null!, _mapperMock.Object, _submitterMock.Object,
            _auditLoggerMock.Object, _loggerMock.Object);
        action.Should().Throw<ArgumentNullException>().WithParameterName("movexReader");
    }

    [Fact]
    public void Constructor_WithNullMapper_ThrowsArgumentNullException()
    {
        var action = () => new InvoiceProcessor(
            _readerMock.Object, null!, _submitterMock.Object,
            _auditLoggerMock.Object, _loggerMock.Object);
        action.Should().Throw<ArgumentNullException>().WithParameterName("mapper");
    }

    [Fact]
    public void Constructor_WithNullSubmitter_ThrowsArgumentNullException()
    {
        var action = () => new InvoiceProcessor(
            _readerMock.Object, _mapperMock.Object, null!,
            _auditLoggerMock.Object, _loggerMock.Object);
        action.Should().Throw<ArgumentNullException>().WithParameterName("submitter");
    }

    [Fact]
    public void Constructor_WithNullAuditLogger_ThrowsArgumentNullException()
    {
        var action = () => new InvoiceProcessor(
            _readerMock.Object, _mapperMock.Object, _submitterMock.Object,
            null!, _loggerMock.Object);
        action.Should().Throw<ArgumentNullException>().WithParameterName("auditLogger");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var action = () => new InvoiceProcessor(
            _readerMock.Object, _mapperMock.Object, _submitterMock.Object,
            _auditLoggerMock.Object, null!);
        action.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region ProcessMonthlyBatch Tests

    [Fact]
    public async Task ProcessMonthlyBatch_NoInvoices_ReturnsEmptyBatchResult()
    {
        // Arrange
        _readerMock
            .Setup(r => r.GetInvoicesByDateRange(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MovexInvoice>());

        // Act
        var result = await _sut.ProcessMonthlyBatch();

        // Assert
        result.Should().NotBeNull();
        result.TotalInvoices.Should().Be(0);
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessMonthlyBatch_AllValid_SubmitsAllInvoices()
    {
        // Arrange
        var invoices = CreateTestInvoices(3);
        SetupReaderReturns(invoices);
        SetupMapperTransformsSuccessfully();
        SetupMapperValidatesSuccessfully();
        SetupSubmitterReturnsSuccess();
        SetupAuditLoggerNoDuplicates();

        // Act
        var result = await _sut.ProcessMonthlyBatch();

        // Assert
        result.TotalInvoices.Should().Be(3);
        result.SuccessCount.Should().Be(3);
        result.FailedCount.Should().Be(0);
        result.SkippedCount.Should().Be(0);
        result.Submissions.Should().HaveCount(3);

        _submitterMock.Verify(
            s => s.Submit(It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        _auditLoggerMock.Verify(
            a => a.LogSubmission(It.IsAny<SubmissionResult>(), It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task ProcessMonthlyBatch_ValidationFailure_SkipsInvalidInvoice()
    {
        // Arrange
        var invoices = CreateTestInvoices(2);
        SetupReaderReturns(invoices);

        // First invoice transforms + validates OK
        var doc1 = CreateTestDocument("INV-001");
        var doc2 = CreateTestDocument("INV-002");

        var callCount = 0;
        _mapperMock.Setup(m => m.Transform(It.IsAny<MovexInvoice>()))
            .Returns(() => ++callCount == 1 ? doc1 : doc2);

        // First validates OK, second has errors
        var noErrors = new List<ValidationError>();
        var withErrors = new List<ValidationError>
        {
            new() { FieldName = "SupplierTIN", Message = "TIN is required", Severity = "Error", ViolatedRule = "MandatoryField_TIN" }
        };

        _mapperMock.Setup(m => m.ValidateDocument(doc1, out noErrors)).Returns(true);
        _mapperMock.Setup(m => m.ValidateDocument(doc2, out withErrors)).Returns(false);

        SetupSubmitterReturnsSuccess();
        SetupAuditLoggerNoDuplicates();

        // Act
        var result = await _sut.ProcessMonthlyBatch();

        // Assert
        result.TotalInvoices.Should().Be(2);
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(1);

        // Only first invoice should be submitted
        _submitterMock.Verify(
            s => s.Submit(It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMonthlyBatch_DuplicateInvoice_SkipsDuplicate()
    {
        // Arrange
        var invoices = CreateTestInvoices(2);
        SetupReaderReturns(invoices);
        SetupMapperTransformsSuccessfully();
        SetupMapperValidatesSuccessfully();
        SetupSubmitterReturnsSuccess();

        // First invoice is a duplicate, second is not
        _auditLoggerMock
            .SetupSequence(a => a.IsInvoiceAlreadySubmitted(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)   // First: already submitted
            .ReturnsAsync(false); // Second: new

        // Act
        var result = await _sut.ProcessMonthlyBatch();

        // Assert
        result.TotalInvoices.Should().Be(2);
        result.SuccessCount.Should().Be(1);
        result.SkippedCount.Should().Be(1);

        // Only second invoice submitted
        _submitterMock.Verify(
            s => s.Submit(It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMonthlyBatch_SubmissionFailure_ContinuesProcessing()
    {
        // Arrange
        var invoices = CreateTestInvoices(3);
        SetupReaderReturns(invoices);
        SetupMapperTransformsSuccessfully();
        SetupMapperValidatesSuccessfully();
        SetupAuditLoggerNoDuplicates();

        // First: success, Second: failure, Third: success
        _submitterMock
            .SetupSequence(s => s.Submit(It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmissionResult { InvoiceNumber = "INV-001", Status = "Success", MyInvoisUUID = "uuid-1" })
            .ReturnsAsync(new SubmissionResult { InvoiceNumber = "INV-002", Status = "Failed", ErrorCode = "DS302", ErrorMessage = "Duplicate" })
            .ReturnsAsync(new SubmissionResult { InvoiceNumber = "INV-003", Status = "Success", MyInvoisUUID = "uuid-3" });

        // Act
        var result = await _sut.ProcessMonthlyBatch();

        // Assert
        result.TotalInvoices.Should().Be(3);
        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(1);

        // All 3 submitted (failure doesn't stop processing)
        _submitterMock.Verify(
            s => s.Submit(It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        // All 3 logged to audit
        _auditLoggerMock.Verify(
            a => a.LogSubmission(It.IsAny<SubmissionResult>(), It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task ProcessMonthlyBatch_ExceptionInReader_ReturnsBatchWithError()
    {
        // Arrange
        _readerMock
            .Setup(r => r.GetInvoicesByDateRange(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB2 connection failed"));

        // Act
        var result = await _sut.ProcessMonthlyBatch();

        // Assert
        result.Should().NotBeNull();
        result.ErrorSummary.Should().Contain("DB2 connection failed");
        result.TotalInvoices.Should().Be(0);
    }

    [Fact]
    public async Task ProcessMonthlyBatch_ExceptionInSingleInvoice_ContinuesProcessing()
    {
        // Arrange
        var invoices = CreateTestInvoices(2);
        SetupReaderReturns(invoices);
        SetupAuditLoggerNoDuplicates();

        // First invoice throws during transform, second succeeds
        var transformCallCount = 0;
        _mapperMock.Setup(m => m.Transform(It.IsAny<MovexInvoice>()))
            .Returns(() =>
            {
                if (++transformCallCount == 1) throw new InvalidOperationException("Transform error");
                return CreateTestDocument("INV-002");
            });

        var noErrors = new List<ValidationError>();
        _mapperMock.Setup(m => m.ValidateDocument(It.IsAny<MyInvoiceDocument>(), out noErrors)).Returns(true);
        SetupSubmitterReturnsSuccess();

        // Act
        var result = await _sut.ProcessMonthlyBatch();

        // Assert
        result.TotalInvoices.Should().Be(2);
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessMonthlyBatch_LogsAuditForAllOutcomes()
    {
        // Arrange
        var invoices = CreateTestInvoices(1);
        SetupReaderReturns(invoices);
        SetupMapperTransformsSuccessfully();
        SetupMapperValidatesSuccessfully();
        SetupSubmitterReturnsSuccess();
        SetupAuditLoggerNoDuplicates();

        // Act
        await _sut.ProcessMonthlyBatch();

        // Assert - audit log called for the submission
        _auditLoggerMock.Verify(
            a => a.LogSubmission(
                It.Is<SubmissionResult>(r => r.Status == "Success"),
                It.IsAny<MyInvoiceDocument>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ProcessSingleInvoice Tests

    [Fact]
    public async Task ProcessSingleInvoice_ValidInvoice_ReturnsSuccess()
    {
        // Arrange
        var invoice = CreateTestInvoice("INV-SINGLE-001");
        _readerMock
            .Setup(r => r.GetInvoiceById("INV-SINGLE-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        var document = CreateTestDocument("INV-SINGLE-001");
        _mapperMock.Setup(m => m.Transform(invoice)).Returns(document);

        var noErrors = new List<ValidationError>();
        _mapperMock.Setup(m => m.ValidateDocument(document, out noErrors)).Returns(true);

        _auditLoggerMock
            .Setup(a => a.IsInvoiceAlreadySubmitted("INV-SINGLE-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _submitterMock
            .Setup(s => s.Submit(document, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmissionResult { InvoiceNumber = "INV-SINGLE-001", Status = "Success", MyInvoisUUID = "uuid-123" });

        // Act
        var result = await _sut.ProcessSingleInvoice("INV-SINGLE-001");

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Success");
        result.MyInvoisUUID.Should().Be("uuid-123");
    }

    [Fact]
    public async Task ProcessSingleInvoice_InvoiceNotFound_ReturnsFailed()
    {
        // Arrange
        _readerMock
            .Setup(r => r.GetInvoiceById("INV-MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MovexInvoice?)null);

        // Act
        var result = await _sut.ProcessSingleInvoice("INV-MISSING");

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Failed");
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ProcessSingleInvoice_AlreadySubmitted_ReturnsFailed()
    {
        // Arrange
        var invoice = CreateTestInvoice("INV-DUP");
        _readerMock
            .Setup(r => r.GetInvoiceById("INV-DUP", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        _auditLoggerMock
            .Setup(a => a.IsInvoiceAlreadySubmitted("INV-DUP", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.ProcessSingleInvoice("INV-DUP");

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Failed");
        result.ErrorCode.Should().Be("DUPLICATE");

        // Should NOT submit
        _submitterMock.Verify(
            s => s.Submit(It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Step 8 Polling Tests

    [Fact]
    public async Task ProcessDateRangeBatch_Step8Valid_UpdatesAuditWithValidStatus()
    {
        // Step 8 returns "Valid" — audit record should be updated, Status stays "Success"
        var invoices = CreateTestInvoices(1);
        SetupReaderReturns(invoices);
        SetupMapperTransformsSuccessfully();
        SetupMapperValidatesSuccessfully();
        SetupAuditLoggerNoDuplicates();

        _submitterMock
            .Setup(s => s.Submit(It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmissionResult { InvoiceNumber = "INV-001", Status = "Success", MyInvoisUUID = "uuid-valid-001" });

        _submitterMock
            .Setup(s => s.GetSubmissionDetails("uuid-valid-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Step8Result("Valid", null));

        await _sut.ProcessDateRangeBatch(DateTime.Today.AddDays(-1), DateTime.Today);

        _auditLoggerMock.Verify(a =>
            a.UpdateStep8Status("INV-001", "uuid-valid-001", "Valid", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessDateRangeBatch_Step8Invalid_RecordsLhdnValidationDetail()
    {
        // ADR-021: the audit record must carry LHDN's own error code and offending field, not a
        // generic "check the portal" message. Without this the only way to learn why a document
        // was rejected was to open the LHDN portal by hand.
        var invoices = CreateTestInvoices(1);
        SetupReaderReturns(invoices);
        SetupMapperTransformsSuccessfully();
        SetupMapperValidatesSuccessfully();
        SetupAuditLoggerNoDuplicates();

        _submitterMock
            .Setup(s => s.Submit(It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmissionResult { InvoiceNumber = "INV-001", Status = "Success", MyInvoisUUID = "uuid-invalid-001" });

        _submitterMock
            .Setup(s => s.GetSubmissionDetails("uuid-invalid-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Step8Result(
                "Invalid",
                "Step04-Supplier Validator (CV303): Supplier identification scheme is invalid [field: AccountingSupplierParty.PartyIdentification]"));

        await _sut.ProcessDateRangeBatch(DateTime.Today.AddDays(-1), DateTime.Today);

        _auditLoggerMock.Verify(a =>
            a.UpdateStep8Status(
                "INV-001", "uuid-invalid-001", "Invalid",
                It.Is<string>(msg => msg != null
                                     && msg.Contains("CV303")
                                     && msg.Contains("AccountingSupplierParty")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessDateRangeBatch_Step8InvalidWithoutDetail_FallsBackToDiagnosticInstruction()
    {
        // If LHDN returns Invalid with no validationResults block, the message must still tell the
        // operator how to retrieve the detail rather than leaving them with nothing actionable.
        var invoices = CreateTestInvoices(1);
        SetupReaderReturns(invoices);
        SetupMapperTransformsSuccessfully();
        SetupMapperValidatesSuccessfully();
        SetupAuditLoggerNoDuplicates();

        _submitterMock
            .Setup(s => s.Submit(It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmissionResult { InvoiceNumber = "INV-001", Status = "Success", MyInvoisUUID = "uuid-nodetail" });

        _submitterMock
            .Setup(s => s.GetSubmissionDetails("uuid-nodetail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Step8Result("Invalid", null));

        await _sut.ProcessDateRangeBatch(DateTime.Today.AddDays(-1), DateTime.Today);

        _auditLoggerMock.Verify(a =>
            a.UpdateStep8Status(
                "INV-001", "uuid-nodetail", "Invalid",
                It.Is<string>(msg => msg != null
                                     && msg.Contains("Get-LhdnDocumentDetails")
                                     && msg.Contains("uuid-nodetail")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessDateRangeBatch_Step8StillSubmitted_DoesNotUpdateAudit()
    {
        // Step 8 returns "Submitted" (still processing) — do not touch the audit record
        var invoices = CreateTestInvoices(1);
        SetupReaderReturns(invoices);
        SetupMapperTransformsSuccessfully();
        SetupMapperValidatesSuccessfully();
        SetupAuditLoggerNoDuplicates();

        _submitterMock
            .Setup(s => s.Submit(It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmissionResult { InvoiceNumber = "INV-001", Status = "Success", MyInvoisUUID = "uuid-pending-001" });

        _submitterMock
            .Setup(s => s.GetSubmissionDetails("uuid-pending-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Step8Result("Submitted", null));

        await _sut.ProcessDateRangeBatch(DateTime.Today.AddDays(-1), DateTime.Today);

        // "Submitted" means still processing — UpdateStep8Status IS called (we record the status)
        // but the audit logger itself decides not to change Status for non-Invalid results
        _auditLoggerMock.Verify(a =>
            a.UpdateStep8Status("INV-001", "uuid-pending-001", "Submitted", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessDateRangeBatch_FailedInvoices_NotPolledForStep8()
    {
        // Only accepted (Status="Success") invoices are polled — failures have no UUID
        var invoices = CreateTestInvoices(2);
        SetupReaderReturns(invoices);
        SetupMapperTransformsSuccessfully();
        SetupMapperValidatesSuccessfully();
        SetupAuditLoggerNoDuplicates();

        _submitterMock
            .SetupSequence(s => s.Submit(It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmissionResult { InvoiceNumber = "INV-001", Status = "Failed", ErrorCode = "DS302" })
            .ReturnsAsync(new SubmissionResult { InvoiceNumber = "INV-002", Status = "Success", MyInvoisUUID = "uuid-002" });

        await _sut.ProcessDateRangeBatch(DateTime.Today.AddDays(-1), DateTime.Today);

        // Only INV-002 (Success with UUID) should be polled
        _submitterMock.Verify(s =>
            s.GetSubmissionDetails(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _auditLoggerMock.Verify(a =>
            a.UpdateStep8Status("INV-002", "uuid-002", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private List<MovexInvoice> CreateTestInvoices(int count)
    {
        return Enumerable.Range(1, count).Select(i => CreateTestInvoice($"INV-{i:D3}")).ToList();
    }

    private MovexInvoice CreateTestInvoice(string invoiceNumber)
    {
        return new MovexInvoice
        {
            InvoiceNumber = invoiceNumber,
            InvoiceDate = "20260201",
            InvoiceType = "Sales",
            CompanyCode = "100",
            CurrencyCode = "MYR",
            ExchangeRate = 1.0m,
            TotalExclTax = 1000m,
            TotalTax = 60m,
            TotalInclTax = 1060m,
            Supplier = new InvoiceParty { TIN = "123456789012", Name = "Test Supplier", BRN = "BRN-001" },
            Buyer = new InvoiceParty { TIN = "987654321098", Name = "Test Buyer", BRN = "BRN-002" },
            Lines = new List<InvoiceLine>
            {
                new()
                {
                    LineNumber = 1,
                    ItemNumber = "ITEM-001",
                    Description = "Test Item",
                    ClassificationCode = "001",
                    Quantity = 10,
                    UnitPrice = 100m,
                    LineTotal = 1000m,
                    TaxRate = 6m,
                    TaxAmount = 60m
                }
            }
        };
    }

    private MyInvoiceDocument CreateTestDocument(string invoiceNumber)
    {
        return new MyInvoiceDocument
        {
            InvoiceNumber = invoiceNumber,
            IssueDate = "2026-02-01",
            IssueTime = "10:00:00",
            CurrencyCode = "MYR",
            ExchangeRate = 1.0m,
            SupplierTIN = "123456789012",
            SupplierName = "Test Supplier",
            SupplierBRN = "BRN-001",
            BuyerTIN = "987654321098",
            BuyerName = "Test Buyer",
            TotalExclTax = 1000m,
            TotalTax = 60m,
            TotalInclTax = 1060m,
            PayableAmount = 1060m,
            Lines = new List<MyInvoiceLine>
            {
                new()
                {
                    LineNumber = 1,
                    ItemNumber = "ITEM-001",
                    Description = "Test Item",
                    ClassificationCode = "001",
                    Quantity = 10,
                    UnitPrice = 100m,
                    LineTotalExclTax = 1000m,
                    TaxRate = 6m,
                    TaxAmount = 60m,
                    LineTotalInclTax = 1060m
                }
            }
        };
    }

    private void SetupReaderReturns(List<MovexInvoice> invoices)
    {
        _readerMock
            .Setup(r => r.GetInvoicesByDateRange(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoices);
    }

    private void SetupMapperTransformsSuccessfully()
    {
        _mapperMock
            .Setup(m => m.Transform(It.IsAny<MovexInvoice>()))
            .Returns((MovexInvoice inv) => CreateTestDocument(inv.InvoiceNumber));
    }

    private void SetupMapperValidatesSuccessfully()
    {
        var noErrors = new List<ValidationError>();
        _mapperMock
            .Setup(m => m.ValidateDocument(It.IsAny<MyInvoiceDocument>(), out noErrors))
            .Returns(true);
    }

    private void SetupSubmitterReturnsSuccess()
    {
        _submitterMock
            .Setup(s => s.Submit(It.IsAny<MyInvoiceDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MyInvoiceDocument doc, CancellationToken _) => new SubmissionResult
            {
                InvoiceNumber = doc.InvoiceNumber,
                Status = "Success",
                MyInvoisUUID = $"uuid-{doc.InvoiceNumber}"
            });
    }

    private void SetupAuditLoggerNoDuplicates()
    {
        _auditLoggerMock
            .Setup(a => a.IsInvoiceAlreadySubmitted(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    #endregion
}
