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
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace MyInvois.Service.Tests.Performance;

/// <summary>
/// Performance baseline tests for the invoice submission pipeline.
/// All I/O (DB2, LHDN API, SQLite) is mocked — measures CPU/memory pipeline throughput only.
///
/// SLAs (generous for CI — designed to catch severe regressions, not micro-optimise):
///   Single invoice:   < 500 ms  (baseline target: < 5 s in production with real I/O)
///   10-invoice batch: < 2 s
///   100-invoice batch: < 15 s
///
/// These are NOT load tests. They run in-process on a single thread.
/// </summary>
[Trait("Category", "Performance")]
public class BatchPerformanceTests : IDisposable
{
    private readonly Mock<IInvoiceDataSource> _dataSourceMock;
    private readonly Mock<IPartyDataProvider> _partyProviderMock;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<AuditDbContext> _auditOptions;
    private readonly InvoiceProcessor _processor;

    public BatchPerformanceTests()
    {
        _dataSourceMock    = new Mock<IInvoiceDataSource>();
        _partyProviderMock = new Mock<IPartyDataProvider>();
        _httpHandlerMock   = new Mock<HttpMessageHandler>();

        var connString = $"Data Source=audit_perf_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive     = new SqliteConnection(connString);
        _keepAlive.Open();
        _auditOptions  = new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(connString).Options;
        using var ctx  = new AuditDbContext(_auditOptions);
        ctx.Database.EnsureCreated();

        SetupHttpMocks();
        SetupPartyProvider();

        var httpClient = new HttpClient(_httpHandlerMock.Object);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var apiSettings = Options.Create(new MyInvoisApiSettings
        {
            BaseUrl            = "https://preprod-api.myinvois.hasil.gov.my",
            TokenEndpoint      = "https://preprod-api.myinvois.hasil.gov.my/connect/token",
            SubmissionEndpoint = "https://preprod-api.myinvois.hasil.gov.my/api/v1.0/documentsubmissions",
            ClientId           = "perf-test-client",
            ClientSecret       = "perf-test-secret"
        });

        var companySettings = Options.Create(new CompanySettings
        {
            Companies = new Dictionary<string, CompanyDetails>
            {
                ["100"] = new()
                {
                    TIN      = "C20921865070",
                    Name     = "SRX Engineering Sdn Bhd",
                    BRN      = "202301012345",
                    IdScheme = "BRN",
                    Address  = "123 Jalan Utama, KL"
                }
            }
        });

        var reader = new MovexInvoiceReader(
            _dataSourceMock.Object,
            _partyProviderMock.Object,
            Options.Create(new ForeignPartyDefaultsSettings()),
            new Mock<ILogger<MovexInvoiceReader>>().Object);

        var mapper = new MyInvoisMapper(
            new MandatoryFieldsValidator(),
            new TINValidator(new Mock<IMemoryCache>().Object, httpClientFactory.Object, apiSettings, new Mock<ILogger<TINValidator>>().Object),
            new DateValidator(),
            new CurrencyValidator(),
            new TotalsValidator(),
            companySettings,
            new Mock<ILogger<MyInvoisMapper>>().Object);

        var submitter = new MyInvoiceSubmitter(
            httpClientFactory.Object,
            apiSettings,
            new Mock<ILogger<MyInvoiceSubmitter>>().Object);

        var auditLogger = new AuditLogger(
            new PerfTestDbContextFactory(_auditOptions),
            new Mock<ILogger<AuditLogger>>().Object);

        _processor = new InvoiceProcessor(
            reader, mapper, submitter, auditLogger,
            new Mock<ILogger<InvoiceProcessor>>().Object);
    }

    public void Dispose() => _keepAlive.Dispose();

    // ── SLA tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SingleInvoice_ProcessesWithin500ms()
    {
        var records = new List<RawInvoiceRecord> { CreateValidARRecord("PERF-001") };
        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        // Warm up — first call acquires OAuth token; don't count that in the SLA.
        await _processor.ProcessDateRangeBatch(DateTime.Today.AddDays(-1), DateTime.Today);

        _dataSourceMock.Reset();
        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var sw = Stopwatch.StartNew();
        var result = await _processor.ProcessDateRangeBatch(DateTime.Today.AddDays(-1), DateTime.Today);
        sw.Stop();

        result.TotalInvoices.Should().Be(1);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500),
            $"single invoice pipeline must complete under 500 ms (actual: {sw.ElapsedMilliseconds} ms)");
    }

    [Fact]
    public async Task TenInvoiceBatch_ProcessesWithin2Seconds()
    {
        var warmupRecords = new List<RawInvoiceRecord> { CreateValidARRecord("PERF-WARMUP-10") };
        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(warmupRecords);
        await _processor.ProcessDateRangeBatch(DateTime.Today.AddDays(-2), DateTime.Today.AddDays(-1));

        var records = Enumerable.Range(1, 10)
            .Select(i => CreateValidARRecord($"PERF-10-{i:D3}"))
            .ToList();

        _dataSourceMock.Reset();
        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var sw = Stopwatch.StartNew();
        var result = await _processor.ProcessDateRangeBatch(DateTime.Today.AddDays(-1), DateTime.Today);
        sw.Stop();

        result.TotalInvoices.Should().Be(10);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            $"10-invoice batch must complete under 2 s (actual: {sw.Elapsed.TotalSeconds:F2} s)");
    }

    [Fact]
    public async Task HundredInvoiceBatch_ProcessesWithin15Seconds()
    {
        // Warm-up uses distinct invoice numbers so the measured run doesn't see them as duplicates.
        var warmupRecords = new List<RawInvoiceRecord> { CreateValidARRecord("PERF-WARMUP-100") };
        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(warmupRecords);
        await _processor.ProcessDateRangeBatch(DateTime.Today.AddDays(-2), DateTime.Today.AddDays(-1));

        var records = Enumerable.Range(1, 100)
            .Select(i => CreateValidARRecord($"PERF-100-{i:D3}"))
            .ToList();

        _dataSourceMock.Reset();
        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var sw = Stopwatch.StartNew();
        var result = await _processor.ProcessDateRangeBatch(DateTime.Today.AddDays(-1), DateTime.Today);
        sw.Stop();

        result.TotalInvoices.Should().Be(100);
        result.SuccessCount.Should().Be(100, "all 100 mocked submissions should succeed");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15),
            $"100-invoice batch must complete under 15 s (actual: {sw.Elapsed.TotalSeconds:F2} s)");
    }

    [Fact]
    public async Task HundredInvoiceBatch_ReportsCorrectThroughput()
    {
        var warmupRecords = new List<RawInvoiceRecord> { CreateValidARRecord("PERF-WARMUP-TPUT") };
        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(warmupRecords);
        await _processor.ProcessDateRangeBatch(DateTime.Today.AddDays(-2), DateTime.Today.AddDays(-1));

        var records = Enumerable.Range(1, 100)
            .Select(i => CreateValidARRecord($"PERF-TPUT-{i:D3}"))
            .ToList();

        _dataSourceMock.Reset();
        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var sw = Stopwatch.StartNew();
        var result = await _processor.ProcessDateRangeBatch(DateTime.Today.AddDays(-1), DateTime.Today);
        sw.Stop();

        var throughput = result.TotalInvoices / sw.Elapsed.TotalSeconds;

        // Minimum bar: at least 10 invoices/second through the mocked pipeline.
        throughput.Should().BeGreaterThan(10,
            $"pipeline throughput must exceed 10 invoices/s with mocked I/O (actual: {throughput:F1} inv/s)");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RawInvoiceRecord CreateValidARRecord(string invoiceNumber) =>
        new()
        {
            InvoiceNo      = invoiceNumber,
            AccountingDate = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd")),
            InvoiceDate    = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd")),
            InvoiceType    = "AR",
            CompanyCode    = "100",
            PartyId        = "CUS-001",
            Currency       = "MYR",
            FxRate         = 1.0m,
            InvoiceAmount  = 1060.00m,
            GstAmount      = 60.00m,
            VoucherNumber  = $"V-{invoiceNumber}",
            Lines          =
            [
                new RawInvoiceLineRecord
                {
                    LineNumber         = 1,
                    ItemNumber         = "SVC-001",
                    Description        = "Engineering Consultancy Services",
                    ClassificationCode = "022",
                    Quantity           = 10,
                    UnitOfMeasure      = "EA",
                    UnitPrice          = 100.00m,
                    LineTotal          = 1000.00m,
                    TaxCode            = "01",
                    TaxRate            = 6.0m,
                    TaxAmount          = 60.00m
                }
            ]
        };

    private void SetupPartyProvider()
    {
        _partyProviderMock
            .Setup(pp => pp.GetCustomerDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId     = "CUS-001",
                TIN         = "C99999999999",
                Name        = "Test Customer Sdn Bhd",
                BRN         = "BRN-CUS-001",
                Address     = "456 Jalan Test, KL",
                IdScheme    = "BRN",
                CountryCode = "MY"
            });

        _partyProviderMock
            .Setup(pp => pp.GetSupplierDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId     = "SUP-001",
                TIN         = "C88888888888",
                Name        = "Test Supplier Sdn Bhd",
                BRN         = "BRN-SUP-001",
                Address     = "789 Jalan Supplier, KL",
                IdScheme    = "BRN",
                CountryCode = "MY"
            });
    }

    private void SetupHttpMocks()
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content    = new StringContent(JsonSerializer.Serialize(new
                {
                    access_token = "perf-test-token",
                    token_type   = "Bearer",
                    expires_in   = 3600
                }))
            });

        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content    = new StringContent(JsonSerializer.Serialize(new
                {
                    submissionUid     = Guid.NewGuid().ToString(),
                    acceptedDocuments = new[] { new { invoiceCodeNumber = "PERF", uuid = Guid.NewGuid().ToString() } },
                    rejectedDocuments = Array.Empty<object>()
                }))
            });
    }
}

file sealed class PerfTestDbContextFactory : IDbContextFactory<AuditDbContext>
{
    private readonly DbContextOptions<AuditDbContext> _options;
    public PerfTestDbContextFactory(DbContextOptions<AuditDbContext> options) => _options = options;
    public AuditDbContext CreateDbContext() => new(_options);
}
