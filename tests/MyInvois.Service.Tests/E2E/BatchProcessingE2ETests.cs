using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using MyInvois.Service.Configuration;
using MyInvois.Service.Data;
using MyInvois.Service.DataAccess;
using MyInvois.Service.Models;
using MyInvois.Service.Services;
using MyInvois.Service.Tests.Integration;
using MyInvois.Service.Validators;
using System.Net;
using System.Text.Json;

namespace MyInvois.Service.Tests.E2E;

/// <summary>
/// End-to-end batch processing tests.
/// Uses skills: All integration skills combined.
///
/// Tests the full invoice lifecycle:
/// MOVEX Reader → Mapper → Validators → Submitter → AuditLogger
///
/// AuditLogger uses in-memory SQLite (ADR-014) — no SQL Server or mocked IDbConnection.
/// </summary>
[Trait("Category", "E2E")]
public class BatchProcessingE2ETests : IDisposable
{
    private readonly Mock<IInvoiceDataSource> _dataSourceMock;
    private readonly Mock<IPartyDataProvider> _partyProviderMock;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<AuditDbContext> _auditOptions;

    private readonly InvoiceProcessor _processor;

    public BatchProcessingE2ETests()
    {
        _dataSourceMock = new Mock<IInvoiceDataSource>();
        _partyProviderMock = new Mock<IPartyDataProvider>();
        _httpHandlerMock = new Mock<HttpMessageHandler>();

        // Set up named shared in-memory SQLite audit database (ADR-014)
        // Named + Cache=Shared: multiple connections share the same in-memory DB.
        // _keepAlive holds it open so AuditLogger's using-disposal doesn't destroy it.
        var connString = $"Data Source=audit_e2e_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connString);
        _keepAlive.Open();
        _auditOptions = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(connString)
            .Options;
        using var setupCtx = new AuditDbContext(_auditOptions);
        setupCtx.Database.EnsureCreated();

        // Wire up HTTP mocks (OAuth + submission)
        SetupOAuthTokenResponse();
        SetupSuccessfulSubmission();

        // Wire up party provider defaults
        SetupDefaultPartyProvider();

        // Build the full component stack
        var reader = new MovexInvoiceReader(
            _dataSourceMock.Object,
            _partyProviderMock.Object,
            Options.Create(new ForeignPartyDefaultsSettings()),
            new Mock<ILogger<MovexInvoiceReader>>().Object);

        var httpClient = new HttpClient(_httpHandlerMock.Object);
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var apiSettings = Options.Create(new MyInvoisApiSettings
        {
            BaseUrl = "https://preprod-api.myinvois.hasil.gov.my",
            TokenEndpoint = "https://preprod-api.myinvois.hasil.gov.my/connect/token",
            SubmissionEndpoint = "https://preprod-api.myinvois.hasil.gov.my/api/v1.0/documentsubmissions",
            ClientId = "test-client",
            ClientSecret = "test-secret"
        });

        var companySettings = Options.Create(new CompanySettings
        {
            Companies = new Dictionary<string, CompanyDetails>
            {
                ["100"] = new()
                {
                    TIN = "C20921865070",
                    Name = "SRX Engineering Sdn Bhd",
                    BRN = "202301012345",
                    IdScheme = "BRN",
                    Address = "123 Jalan Utama, KL"
                }
            }
        });

        var tinValidator = new TINValidator(
            new Mock<IMemoryCache>().Object,
            httpClientFactoryMock.Object,
            apiSettings,
            new Mock<ILogger<TINValidator>>().Object);

        var mapper = new MyInvoisMapper(
            new MandatoryFieldsValidator(),
            tinValidator,
            new DateValidator(),
            new CurrencyValidator(),
            new TotalsValidator(),
            companySettings,
            new Mock<ILogger<MyInvoisMapper>>().Object);

        var tokenService = new MyInvoisTokenService(
            httpClientFactoryMock.Object,
            apiSettings,
            new Mock<ILogger<MyInvoisTokenService>>().Object);

        var submitter = new MyInvoiceSubmitter(
            httpClientFactoryMock.Object,
            apiSettings,
            tokenService,
            new Mock<ILogger<MyInvoiceSubmitter>>().Object);

        // AuditLogger uses in-memory SQLite via factory (ADR-014)
        var auditLogger = new AuditLogger(
            new E2ETestDbContextFactory(_auditOptions),
            new Mock<ILogger<AuditLogger>>().Object);

        _processor = new InvoiceProcessor(
            reader, mapper, submitter, auditLogger,
            new Mock<ILogger<InvoiceProcessor>>().Object);
    }

    public void Dispose() => _keepAlive.Dispose();

    private AuditDbContext NewCtx() => new(_auditOptions);

    [Fact]
    public async Task E2E_HappyPath_AllInvoicesSubmittedSuccessfully()
    {
        // Arrange - 3 valid AR invoices from DB2
        var records = Enumerable.Range(1, 3).Select(i => CreateValidARRecord($"E2E-HP-{i:D3}")).ToList();

        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        // Act
        var result = await _processor.ProcessMonthlyBatch();

        // Assert
        result.TotalInvoices.Should().Be(3);
        result.SuccessCount.Should().Be(3);
        result.FailedCount.Should().Be(0);
        result.SkippedCount.Should().Be(0);
        result.CompletedAt.Should().NotBeNull();
        result.Submissions.Should().HaveCount(3);
        result.Submissions.Should().OnlyContain(s => s.Status == "Success");
    }

    [Fact]
    public async Task E2E_MixedBatch_ValidAndInvalid_ProcessesAllWithoutStopping()
    {
        // Arrange - 2 valid + 1 invoice that will fail validation (missing mandatory fields)
        var records = new List<RawInvoiceRecord>
        {
            CreateValidARRecord("E2E-MIX-001"),
            CreateInvalidRecord("E2E-MIX-002"), // Missing party data → validation failure
            CreateValidARRecord("E2E-MIX-003"),
        };

        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        // Act
        var result = await _processor.ProcessMonthlyBatch();

        // Assert - all 3 processed, some may fail validation
        result.TotalInvoices.Should().Be(3);
        result.Submissions.Should().HaveCount(3);
        result.SuccessCount.Should().BeGreaterThanOrEqualTo(1);
        (result.SuccessCount + result.FailedCount + result.SkippedCount).Should().Be(3);
    }

    [Fact]
    public async Task E2E_DuplicateDetection_SkipsAlreadySubmitted()
    {
        // Arrange — pre-insert a successful submission for the first invoice in SQLite
        using (var ctx = NewCtx())
        {
            ctx.AuditLogs.Add(new AuditLogEntity
            {
                Action = "MyInvois_Submit", Category = "Integration", Severity = "Info",
                ResourceType = "Invoice", ResourceId = "E2E-DUP-001",
                Status = "Success", InvoiceNumber = "E2E-DUP-001"
            });
            await ctx.SaveChangesAsync();
        }

        var records = new List<RawInvoiceRecord>
        {
            CreateValidARRecord("E2E-DUP-001"), // already in DB → should be skipped
            CreateValidARRecord("E2E-DUP-002"), // new → should be submitted
        };

        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        // Act
        var result = await _processor.ProcessMonthlyBatch();

        // Assert
        result.TotalInvoices.Should().Be(2);
        result.SkippedCount.Should().Be(1, "first invoice was already submitted");
        result.SuccessCount.Should().Be(1, "second invoice was new and submitted");
    }

    [Fact]
    public async Task E2E_AuditTrail_AllOutcomesLogged()
    {
        // Arrange
        var records = new List<RawInvoiceRecord>
        {
            CreateValidARRecord("E2E-AUDIT-001"),
        };

        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        // Act
        var result = await _processor.ProcessMonthlyBatch();

        // Assert - audit row was created in SQLite
        using var assertCtx = NewCtx();
        var auditCount = await assertCtx.AuditLogs.CountAsync();
        auditCount.Should().BeGreaterThanOrEqualTo(1, "at least one audit row per invoice processed");

        var entry = await assertCtx.AuditLogs
            .FirstOrDefaultAsync(x => x.InvoiceNumber == "E2E-AUDIT-001");
        entry.Should().NotBeNull();
        entry!.Action.Should().Be("MyInvois_Submit");
        entry.Category.Should().Be("Integration");
    }

    [Fact]
    public async Task E2E_EmptyBatch_CompletesGracefully()
    {
        // Arrange - no invoices
        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>());

        // Act
        var result = await _processor.ProcessMonthlyBatch();

        // Assert
        result.TotalInvoices.Should().Be(0);
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.CompletedAt.Should().NotBeNull();

        // No submission HTTP calls made
        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString().Contains("documentsubmissions")),
            ItExpr.IsAny<CancellationToken>());
    }

    #region Setup Helpers

    private RawInvoiceRecord CreateValidARRecord(string invoiceNumber)
    {
        return new RawInvoiceRecord
        {
            InvoiceNo = invoiceNumber,
            AccountingDate = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd")),
            InvoiceDate = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd")),
            InvoiceType = "AR",
            CompanyCode = "100",
            PartyId = "CUS-001",
            Currency = "MYR",
            FxRate = 1.0m,
            InvoiceAmount = 1060.00m,
            GstAmount = 60.00m,
            VoucherNumber = $"V-{invoiceNumber}",
            Lines = new List<RawInvoiceLineRecord>
            {
                new()
                {
                    LineNumber = 1,
                    ItemNumber = "SVC-001",
                    Description = "Engineering Consultancy Services",
                    ClassificationCode = "022",
                    Quantity = 10,
                    UnitOfMeasure = "EA",
                    UnitPrice = 100.00m,
                    LineTotal = 1000.00m,
                    TaxCode = "01",
                    TaxRate = 6.0m,
                    TaxAmount = 60.00m
                }
            }
        };
    }

    private RawInvoiceRecord CreateInvalidRecord(string invoiceNumber)
    {
        return new RawInvoiceRecord
        {
            InvoiceNo = invoiceNumber,
            AccountingDate = 0, // Invalid date
            InvoiceType = "AR",
            CompanyCode = "999", // Unknown company
            PartyId = "UNKNOWN",
            Currency = "MYR",
            FxRate = 1.0m,
            InvoiceAmount = 0m,
            GstAmount = 0m,
            VoucherNumber = ""
        };
    }

    private void SetupDefaultPartyProvider()
    {
        _partyProviderMock
            .Setup(pp => pp.GetCustomerDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId = "CUS-001",
                TIN = "C99999999999",
                Name = "Test Customer Sdn Bhd",
                BRN = "BRN-CUS-001",
                Address = "456 Jalan Test, KL",
                IdScheme = "BRN",
                CountryCode = "MY"
            });

        _partyProviderMock
            .Setup(pp => pp.GetSupplierDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId = "SUP-001",
                TIN = "C88888888888",
                Name = "Test Supplier Sdn Bhd",
                BRN = "BRN-SUP-001",
                Address = "789 Jalan Supplier, KL",
                IdScheme = "BRN",
                CountryCode = "MY"
            });
    }

    private void SetupOAuthTokenResponse()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    access_token = "e2e-test-token",
                    token_type = "Bearer",
                    expires_in = 3600
                }))
            });
    }

    private void SetupSuccessfulSubmission()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    uuid = $"DOC-{Guid.NewGuid():N}",
                    submissionDate = DateTime.UtcNow.ToString("o"),
                    status = "Valid"
                }))
            });
    }

    #endregion
}

/// <summary>
/// Test helper: creates a fresh AuditDbContext per call from shared options.
/// AuditLogger disposes each context via 'using' — options stay alive.
/// </summary>
file sealed class E2ETestDbContextFactory : IDbContextFactory<AuditDbContext>
{
    private readonly DbContextOptions<AuditDbContext> _options;
    public E2ETestDbContextFactory(DbContextOptions<AuditDbContext> options) => _options = options;
    public AuditDbContext CreateDbContext() => new(_options);
}
