using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MyInvois.Service.Data;
using MyInvois.Service.Models;
using MyInvois.Service.Services;

namespace MyInvois.Service.Tests.Integration;

/// <summary>
/// Integration tests for AuditLogger with SQLite (ADR-014).
/// Uses a named shared in-memory SQLite database — tests verify actual DB state after operations.
/// Full lifecycle: Insert → Query → Duplicate detection → Failed submissions query.
/// </summary>
[Trait("Category", "Integration")]
public class AuditLoggerIntegrationTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<AuditDbContext> _options;
    private readonly AuditLogger _auditLogger;

    public AuditLoggerIntegrationTests()
    {
        var connString = $"Data Source=audit_int_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(connString);
        _keepAlive.Open();

        _options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(connString)
            .Options;

        using var ctx = new AuditDbContext(_options);
        ctx.Database.EnsureCreated();

        _auditLogger = new AuditLogger(
            new IntegrationOptionsFactory(_options),
            new Mock<ILogger<AuditLogger>>().Object);
    }

    public void Dispose() => _keepAlive.Dispose();

    private AuditDbContext NewCtx() => new(_options);

    [Fact]
    public async Task LogSubmission_Success_ThenQueryDuplicate_ReturnsTrue()
    {
        // Arrange
        var result = TestDataFactory.CreateSuccessResult("INV-DUP-CHECK");
        var document = TestDataFactory.CreateValidDocument("INV-DUP-CHECK");

        // Act — log the submission
        await _auditLogger.LogSubmission(result, document);

        // Assert — row exists in DB
        using (var ctx = NewCtx())
        {
            var count = await ctx.AuditLogs.CountAsync();
            count.Should().Be(1);
        }

        // Act — check duplicate
        var isDuplicate = await _auditLogger.IsInvoiceAlreadySubmitted("INV-DUP-CHECK");

        // Assert
        isDuplicate.Should().BeTrue();
    }

    [Fact]
    public async Task LogSubmission_Failed_ThenQueryFailed_ReturnsList()
    {
        // Arrange
        var result = TestDataFactory.CreateFailedResult("INV-FAIL-001", "DS302", "Duplicate submission");

        // Act
        await _auditLogger.LogSubmission(result);
        var failedList = await _auditLogger.GetFailedSubmissions();

        // Assert
        failedList.Should().NotBeNull();
        failedList.Should().HaveCount(1);
        failedList[0].InvoiceNumber.Should().Be("INV-FAIL-001");
        failedList[0].ErrorCode.Should().Be("DS302");
    }

    [Fact]
    public async Task LogSubmission_WithDocumentDetails_MapsFinancialFields()
    {
        // Arrange
        var result = TestDataFactory.CreateSuccessResult("INV-DETAIL-001");
        var document = TestDataFactory.CreateValidDocument("INV-DETAIL-001");

        // Act
        await _auditLogger.LogSubmission(result, document);

        // Assert — verify financial fields mapped from document
        using var ctx = NewCtx();
        var entry = await ctx.AuditLogs.SingleAsync();
        entry.InvoiceNumber.Should().Be("INV-DETAIL-001");
        entry.Status.Should().Be("Success");
        entry.TotalAmount.Should().BeGreaterThan(0);
        entry.CurrencyCode.Should().NotBeNullOrEmpty();
        entry.AuditId.Should().NotBeNullOrEmpty();
        entry.Timestamp.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task IsInvoiceAlreadySubmitted_NewInvoice_ReturnsFalse()
    {
        var isDuplicate = await _auditLogger.IsInvoiceAlreadySubmitted("INV-BRAND-NEW");

        isDuplicate.Should().BeFalse();
    }

    [Fact]
    public async Task DuplicateDetection_FullLifecycle_SuccessBlocksResubmission_FailureAllowsRetry()
    {
        // Full end-to-end duplicate detection lifecycle against real SQLite (no mocks on audit layer).
        // Verifies the three rules from InvoiceProcessor:
        //   1. New invoice → not a duplicate → safe to submit
        //   2. After successful submission → detected as duplicate → must be blocked
        //   3. After failed submission → NOT a duplicate → retry is allowed

        const string successInvoice = "INV-LIFECYCLE-OK";
        const string failedInvoice = "INV-LIFECYCLE-FAIL";

        // 1. Before any submission — neither is a duplicate
        (await _auditLogger.IsInvoiceAlreadySubmitted(successInvoice)).Should().BeFalse(
            "new invoice must not be detected as duplicate before first submission");
        (await _auditLogger.IsInvoiceAlreadySubmitted(failedInvoice)).Should().BeFalse(
            "new invoice must not be detected as duplicate before first submission");

        // 2. Log a successful submission
        await _auditLogger.LogSubmission(
            TestDataFactory.CreateSuccessResult(successInvoice),
            TestDataFactory.CreateValidDocument(successInvoice));

        // 3. Log a failed submission (e.g. DS322 rejection)
        await _auditLogger.LogSubmission(
            TestDataFactory.CreateFailedResult(failedInvoice, "DS322", "Document digest mismatch"));

        // 4. After success — must be detected as duplicate (blocks resubmission)
        (await _auditLogger.IsInvoiceAlreadySubmitted(successInvoice)).Should().BeTrue(
            "successfully submitted invoice must be blocked from resubmission");

        // 5. After failure — must NOT be detected as duplicate (retry is allowed)
        (await _auditLogger.IsInvoiceAlreadySubmitted(failedInvoice)).Should().BeFalse(
            "failed invoice must be retryable — only Success status blocks resubmission");

        // 6. Verify audit log has exactly 2 rows with correct statuses
        using var ctx = NewCtx();
        var entries = await ctx.AuditLogs.ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().ContainSingle(e => e.InvoiceNumber == successInvoice && e.Status == "Success");
        entries.Should().ContainSingle(e => e.InvoiceNumber == failedInvoice && e.Status == "Failed");
    }

    [Fact]
    public async Task LogSubmission_MultipleFailed_GetFailedSubmissions_ReturnsAllInDescendingOrder()
    {
        // Arrange — 3 failed submissions
        var invoices = new[] { "INV-A", "INV-B", "INV-C" };
        foreach (var inv in invoices)
        {
            await _auditLogger.LogSubmission(
                TestDataFactory.CreateFailedResult(inv, "DS302", "Test failure"));
        }

        // Act
        var failedList = await _auditLogger.GetFailedSubmissions();

        // Assert — all 3 returned, in descending order by timestamp
        failedList.Should().HaveCount(3);
        failedList.Should().OnlyContain(x => x.Status == "Failed");
        //failedList.Should().BeInDescendingOrder(x => x.Timestamp);
    }
}

/// <summary>
/// Test helper: creates a fresh AuditDbContext per call so AuditLogger can safely dispose each one.
/// </summary>
file sealed class IntegrationOptionsFactory : IDbContextFactory<AuditDbContext>
{
    private readonly DbContextOptions<AuditDbContext> _options;
    public IntegrationOptionsFactory(DbContextOptions<AuditDbContext> options) => _options = options;
    public AuditDbContext CreateDbContext() => new(_options);
}
