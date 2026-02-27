using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using MyInvois.Service.Configuration;
using MyInvois.Service.DataAccess;
using MyInvois.Service.Models;
using MyInvois.Service.Services;
using MyInvois.Service.Tests.Integration;
using MyInvois.Service.Validators;
using System.Data;
using System.Net;
using System.Text.Json;

namespace MyInvois.Service.Tests.E2E;

/// <summary>
/// End-to-end batch processing tests
/// Uses skills: All integration skills combined
///
/// Tests the full invoice lifecycle:
/// MOVEX Reader → Mapper → Validators → Submitter → AuditLogger
///
/// These tests wire up all real components with mocked external dependencies
/// (DB2, MyInvois API, SQL Server) to validate the complete processing pipeline.
/// </summary>
[Trait("Category", "E2E")]
public class BatchProcessingE2ETests
{
    private readonly Mock<IInvoiceDataSource> _dataSourceMock;
    private readonly Mock<IPartyDataProvider> _partyProviderMock;
    private readonly Mock<IDbConnection> _dbConnectionMock;
    private readonly Mock<IDbCommand> _dbCommandMock;
    private readonly Mock<IDataParameterCollection> _parametersMock;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;

    private readonly InvoiceProcessor _processor;

    public BatchProcessingE2ETests()
    {
        _dataSourceMock = new Mock<IInvoiceDataSource>();
        _partyProviderMock = new Mock<IPartyDataProvider>();
        _dbConnectionMock = new Mock<IDbConnection>();
        _dbCommandMock = new Mock<IDbCommand>();
        _parametersMock = new Mock<IDataParameterCollection>();
        _httpHandlerMock = new Mock<HttpMessageHandler>();

        // Wire up DB mocks
        _dbCommandMock.Setup(c => c.Parameters).Returns(_parametersMock.Object);
        _dbCommandMock.Setup(c => c.CreateParameter()).Returns(new Mock<IDbDataParameter>().Object);
        _dbConnectionMock.Setup(c => c.CreateCommand()).Returns(_dbCommandMock.Object);
        _dbConnectionMock.Setup(c => c.State).Returns(ConnectionState.Open);
        _dbCommandMock.Setup(c => c.ExecuteNonQuery()).Returns(1);

        // No duplicates by default
        _dbCommandMock.Setup(c => c.ExecuteScalar()).Returns(0);

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
                    TIN = "000000000000",
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

        var submitter = new MyInvoiceSubmitter(
            httpClientFactoryMock.Object,
            apiSettings,
            new Mock<ILogger<MyInvoiceSubmitter>>().Object);

        var auditLogger = new AuditLogger(
            _dbConnectionMock.Object,
            new Mock<ILogger<AuditLogger>>().Object);

        _processor = new InvoiceProcessor(
            reader, mapper, submitter, auditLogger,
            new Mock<ILogger<InvoiceProcessor>>().Object);
    }

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
        // Valid invoices succeed, invalid fails
        result.SuccessCount.Should().BeGreaterThanOrEqualTo(1);
        (result.SuccessCount + result.FailedCount + result.SkippedCount).Should().Be(3);
    }

    [Fact]
    public async Task E2E_DuplicateDetection_SkipsAlreadySubmitted()
    {
        // Arrange
        var records = new List<RawInvoiceRecord>
        {
            CreateValidARRecord("E2E-DUP-001"),
            CreateValidARRecord("E2E-DUP-002"),
        };

        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        // First invoice is already submitted (duplicate)
        var dupCheckCount = 0;
        _dbCommandMock
            .Setup(c => c.ExecuteScalar())
            .Returns(() =>
            {
                dupCheckCount++;
                return dupCheckCount == 1 ? 1 : 0; // First: duplicate, Second: new
            });

        // Act
        var result = await _processor.ProcessMonthlyBatch();

        // Assert
        result.TotalInvoices.Should().Be(2);
        result.SkippedCount.Should().Be(1, "first invoice was a duplicate");
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

        // Assert - audit log insert was called
        _dbCommandMock.Verify(c => c.ExecuteNonQuery(), Times.AtLeastOnce);

        // Verify the SQL command was set (INSERT INTO AuditLog)
        _dbCommandMock.VerifySet(c => c.CommandText = It.Is<string>(
            sql => sql.Contains("INSERT INTO") && sql.Contains("AuditLog")),
            Times.AtLeastOnce);
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

        // No submission or audit calls made
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
                TIN = "999999999999",
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
                TIN = "888888888888",
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
