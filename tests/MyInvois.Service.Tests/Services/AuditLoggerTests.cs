using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MyInvois.Service.Data;
using MyInvois.Service.Models;
using MyInvois.Service.Services;

namespace MyInvois.Service.Tests.Services;

/// <summary>
/// Unit tests for AuditLogger (SQLite via EF Core — ADR-014).
///
/// Uses a named shared in-memory SQLite database so that:
/// - AuditLogger can safely dispose each DbContext after use (via using)
/// - The test can assert against the same in-memory database
/// - The _keepAlive connection prevents SQLite from destroying the in-memory DB
/// </summary>
public class AuditLoggerTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<AuditDbContext> _options;
    private readonly AuditLogger _sut;
    private readonly Mock<ILogger<AuditLogger>> _loggerMock;

    public AuditLoggerTests()
    {
        // Named in-memory SQLite — multiple connections share the same data
        var connString = $"Data Source=audit_unit_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(connString);
        _keepAlive.Open(); // keeps the in-memory DB alive for the test duration

        _options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(connString)
            .Options;

        using var ctx = new AuditDbContext(_options);
        ctx.Database.EnsureCreated();

        _loggerMock = new Mock<ILogger<AuditLogger>>();
        _sut = new AuditLogger(new OptionsDbContextFactory(_options), _loggerMock.Object);
    }

    public void Dispose() => _keepAlive.Dispose();

    // Helper — create a fresh context for assertions (safe to dispose independently)
    private AuditDbContext NewCtx() => new(_options);

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullContextFactory_ThrowsArgumentNullException()
    {
        var action = () => new AuditLogger(null!, _loggerMock.Object);
        action.Should().Throw<ArgumentNullException>().WithParameterName("contextFactory");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var action = () => new AuditLogger(new OptionsDbContextFactory(_options), null!);
        action.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region LogSubmission Tests

    [Fact]
    public async Task LogSubmission_SuccessfulSubmission_InsertsRow()
    {
        var result = new SubmissionResult
        {
            InvoiceNumber = "INV-001",
            Status = "Success",
            MyInvoisUUID = "uuid-123",
            SubmittedAt = DateTime.UtcNow
        };

        await _sut.LogSubmission(result);

        using var ctx = Assert();
        var count = await ctx.AuditLogs.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task LogSubmission_SuccessfulSubmission_SetsCorrectStandardFields()
    {
        var result = new SubmissionResult
        {
            InvoiceNumber = "INV-002",
            Status = "Success",
            MyInvoisUUID = "uuid-456",
            SubmittedAt = DateTime.UtcNow
        };

        await _sut.LogSubmission(result);

        using var ctx = Assert();
        var entry = await ctx.AuditLogs.SingleAsync();
        entry.Action.Should().Be("MyInvois_Submit");
        entry.Category.Should().Be("Integration");
        entry.Severity.Should().Be("Info");
        entry.Status.Should().Be("Success");
        entry.InvoiceNumber.Should().Be("INV-002");
        entry.MyInvoisUUID.Should().Be("uuid-456");
        entry.ResourceType.Should().Be("Invoice");
        entry.ResourceId.Should().Be("INV-002");
    }

    [Fact]
    public async Task LogSubmission_FailedSubmission_SetsErrorSeverityAndErrorFields()
    {
        var result = new SubmissionResult
        {
            InvoiceNumber = "INV-003",
            Status = "Failed",
            ErrorCode = "DS302",
            ErrorMessage = "Duplicate submission",
            SubmittedAt = DateTime.UtcNow
        };

        await _sut.LogSubmission(result);

        using var ctx = Assert();
        var entry = await ctx.AuditLogs.SingleAsync();
        entry.Status.Should().Be("Failed");
        entry.Severity.Should().Be("Error");
        entry.StatusCode.Should().Be("DS302");
        entry.ErrorMessage.Should().Be("Duplicate submission");
    }

    [Fact]
    public async Task LogSubmission_WithDocument_MapsFinancialFields()
    {
        var result = new SubmissionResult
        {
            InvoiceNumber = "INV-004",
            Status = "Success",
            SubmittedAt = DateTime.UtcNow
        };
        var document = new MyInvoiceDocument
        {
            InvoiceNumber = "INV-004",
            DocumentTypeCode = "01",
            IssueDate = "2026-03-10",
            TotalInclTax = 1060.00m,
            TotalTax = 60.00m,
            CurrencyCode = "MYR"
        };

        await _sut.LogSubmission(result, document);

        using var ctx = Assert();
        var entry = await ctx.AuditLogs.SingleAsync();
        entry.TotalAmount.Should().Be(1060.00m);
        entry.TotalTax.Should().Be(60.00m);
        entry.CurrencyCode.Should().Be("MYR");
        entry.InvoiceDate.Should().Be("2026-03-10");
        entry.InvoiceType.Should().Be("Sales");
    }

    #endregion

    #region IsInvoiceAlreadySubmitted Tests

    [Fact]
    public async Task IsInvoiceAlreadySubmitted_AfterSuccessfulSubmission_ReturnsTrue()
    {
        using (var ctx = Assert())
        {
            ctx.AuditLogs.Add(new AuditLogEntity
            {
                Action = "MyInvois_Submit", Category = "Integration", Severity = "Info",
                ResourceType = "Invoice", ResourceId = "INV-DUP", Status = "Success",
                InvoiceNumber = "INV-DUP"
            });
            await ctx.SaveChangesAsync();
        }

        var result = await _sut.IsInvoiceAlreadySubmitted("INV-DUP");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsInvoiceAlreadySubmitted_NewInvoice_ReturnsFalse()
    {
        var result = await _sut.IsInvoiceAlreadySubmitted("INV-BRAND-NEW");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsInvoiceAlreadySubmitted_FailedSubmission_ReturnsFalse()
    {
        // A failed submission must NOT prevent re-submission
        using (var ctx = Assert())
        {
            ctx.AuditLogs.Add(new AuditLogEntity
            {
                Action = "MyInvois_Submit", Category = "Integration", Severity = "Error",
                ResourceType = "Invoice", ResourceId = "INV-FAIL", Status = "Failed",
                InvoiceNumber = "INV-FAIL"
            });
            await ctx.SaveChangesAsync();
        }

        var result = await _sut.IsInvoiceAlreadySubmitted("INV-FAIL");

        result.Should().BeFalse();
    }

    #endregion

    #region GetFailedSubmissions Tests

    [Fact]
    public async Task GetFailedSubmissions_NoFailures_ReturnsEmptyList()
    {
        var result = await _sut.GetFailedSubmissions();

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFailedSubmissions_WithFailures_ReturnsMappedResults()
    {
        using (var ctx = Assert())
        {
            ctx.AuditLogs.Add(new AuditLogEntity
            {
                Action = "MyInvois_Submit", Category = "Integration", Severity = "Error",
                ResourceType = "Invoice", ResourceId = "INV-FAIL-01",
                Status = "Failed", InvoiceNumber = "INV-FAIL-01",
                StatusCode = "DS302", ErrorMessage = "Duplicate submission",
                Timestamp = DateTime.UtcNow.ToString("O")
            });
            await ctx.SaveChangesAsync();
        }

        var result = await _sut.GetFailedSubmissions();

        result.Should().HaveCount(1);
        result[0].InvoiceNumber.Should().Be("INV-FAIL-01");
        result[0].Status.Should().Be("Failed");
        result[0].ErrorCode.Should().Be("DS302");
        result[0].ErrorMessage.Should().Be("Duplicate submission");
    }

    [Fact]
    public async Task GetFailedSubmissions_RespectsMaxResults()
    {
        using (var ctx = Assert())
        {
            for (int i = 1; i <= 5; i++)
            {
                ctx.AuditLogs.Add(new AuditLogEntity
                {
                    Action = "MyInvois_Submit", Category = "Integration", Severity = "Error",
                    ResourceType = "Invoice", ResourceId = $"INV-{i:D3}",
                    Status = "Failed", InvoiceNumber = $"INV-{i:D3}",
                    Timestamp = DateTime.UtcNow.AddMinutes(-i).ToString("O")
                });
            }
            await ctx.SaveChangesAsync();
        }

        var result = await _sut.GetFailedSubmissions(maxResults: 3);

        result.Should().HaveCount(3);
    }

    #endregion
}

/// <summary>
/// Test helper: creates a fresh AuditDbContext from shared options each call.
/// This lets AuditLogger safely dispose each context (via using), while the
/// named in-memory SQLite database remains alive via the _keepAlive connection.
/// </summary>
file sealed class OptionsDbContextFactory : IDbContextFactory<AuditDbContext>
{
    private readonly DbContextOptions<AuditDbContext> _options;
    public OptionsDbContextFactory(DbContextOptions<AuditDbContext> options) => _options = options;
    public AuditDbContext CreateDbContext() => new(_options);
}
