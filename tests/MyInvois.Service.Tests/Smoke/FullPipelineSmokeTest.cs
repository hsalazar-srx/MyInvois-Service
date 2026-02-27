using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using MyInvois.Service.Configuration;
using MyInvois.Service.DataAccess;
using MyInvois.Service.Services;
using MyInvois.Service.Validators;
using System.Net;
using System.Text.Json;
using Xunit.Abstractions;

namespace MyInvois.Service.Tests.Smoke;

/// <summary>
/// Full pipeline smoke test — uses REAL DB2, REAL party data provider, REAL validators.
/// Mocks: MyInvois HTTP API, AuditLogger (no SQL Server required).
///
/// Purpose: Demonstrate to stakeholders that real MOVEX invoices flow through the pipeline.
/// Shows validation results and identifies remaining gaps (TIN/BRN columns, XAdES signing).
///
/// Run: dotnet test --filter "Category=Smoke" --logger "console;verbosity=detailed"
/// </summary>
[Trait("Category", "Smoke")]
[Trait("Category", "RequiresDb2")]
public class FullPipelineSmokeTest
{
    private readonly ITestOutputHelper _output;
    private readonly IConfiguration _configuration;

    public FullPipelineSmokeTest(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));

        // Load configuration from appsettings.json + User Secrets
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<FullPipelineSmokeTest>(optional: true)
            .Build();
    }

    [Fact(DisplayName = "Diagnostic: Verify MOVEX Schema Connection")]
    public async Task DiagnosticTest_ConfirmMvxcdtaSchema()
    {
        var movexDbSettings = _configuration.GetSection("MovexDb").Get<MovexDbSettings>()
            ?? throw new InvalidOperationException("MovexDb configuration missing");

        _output.WriteLine($"Configured Schema: {movexDbSettings.SchemaCmp100}");
        _output.WriteLine($"Active Companies: {string.Join(", ", movexDbSettings.ActiveCompanyCodes)}");
        _output.WriteLine($"Connection String: {(string.IsNullOrEmpty(movexDbSettings.ConnectionString) ? "NOT SET" : "SET (masked)")}");
        _output.WriteLine("");

        var dataSource = new DirectQueryDataSource(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<DirectQueryDataSource>());

        try
        {
            // Test with current month's date range
            var fromDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var toDate = fromDate.AddMonths(1).AddSeconds(-1);
            
            _output.WriteLine($"Querying invoices from {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}...");

            var result = await dataSource.GetPendingInvoicesAsync(fromDate, CancellationToken.None);
            _output.WriteLine($"✅ Schema '{movexDbSettings.SchemaCmp100}' connected successfully");
            _output.WriteLine($"📊 Retrieved {result.Count} invoices");

            if (result.Count > 0)
            {
                _output.WriteLine($"   Sample invoice: {result.FirstOrDefault()?.InvoiceNo}");
            }

            result.Count.Should().BeGreaterThanOrEqualTo(0);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"❌ Failed to query {movexDbSettings.SchemaCmp100}");
            _output.WriteLine($"Error: {ex.Message}");
            if (ex.InnerException != null)
                _output.WriteLine($"Inner Error: {ex.InnerException.Message}");
            throw;
        }
    }

    [Fact(DisplayName = "Full Pipeline Smoke Test with REAL DB2 and Validators")]
    public async Task SmokeTest_RealDb2_FetchAndMapInvoices_ShowValidationResults()
    {
        // ====================================================================
        // ARRANGE — Wire up REAL components (except HTTP + Audit Logger)
        // ====================================================================

        _output.WriteLine("=== MyInvois Service — Full Pipeline Smoke Test ===");
        _output.WriteLine($"Execution Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine("");

        // --- Configuration ---
        var movexDbSettings = _configuration.GetSection("MovexDb").Get<MovexDbSettings>()
            ?? throw new InvalidOperationException("MovexDb configuration missing");

        var apiSettings = _configuration.GetSection("MyInvoisApi").Get<MyInvoisApiSettings>()
            ?? throw new InvalidOperationException("MyInvoisApi configuration missing");

        var companySettings = _configuration.GetSection("Companies").Get<Dictionary<string, CompanyDetails>>()
            ?? throw new InvalidOperationException("Companies configuration missing");

        _output.WriteLine($"DB2 Strategy: {movexDbSettings.DataSourceStrategy}");
        _output.WriteLine($"Party Data Source: {movexDbSettings.PartyDataSource}");
        _output.WriteLine($"Active Companies: {string.Join(", ", movexDbSettings.ActiveCompanyCodes)}");
        _output.WriteLine($"MyInvois Environment: {apiSettings.Environment} ({apiSettings.BaseUrl})");
        _output.WriteLine("");

        // --- REAL Data Access Layer ---
        var dataSource = new DirectQueryDataSource(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<DirectQueryDataSource>());

        var partyProvider = new MovexMasterPartyDataProvider(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<MovexMasterPartyDataProvider>());

        var reader = new MovexInvoiceReader(
            dataSource,
            partyProvider,
            Options.Create(new ForeignPartyDefaultsSettings()),
            new LoggerFactory().CreateLogger<MovexInvoiceReader>());

        // --- REAL Validators ---
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpHandlerMock = new Mock<HttpMessageHandler>();
        SetupMockOAuthResponse(httpHandlerMock);
        SetupMockSubmissionResponse(httpHandlerMock);

        var httpClient = new HttpClient(httpHandlerMock.Object);
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var tinValidator = new TINValidator(
            memoryCache,
            httpClientFactoryMock.Object,
            Options.Create(apiSettings),
            new LoggerFactory().CreateLogger<TINValidator>());

        var mandatoryValidator = new MandatoryFieldsValidator();
        var dateValidator = new DateValidator();
        var currencyValidator = new CurrencyValidator();
        var totalsValidator = new TotalsValidator();

        // --- REAL Mapper ---
        var mapper = new MyInvoisMapper(
            mandatoryValidator,
            tinValidator,
            dateValidator,
            currencyValidator,
            totalsValidator,
            Options.Create(new CompanySettings { Companies = companySettings }),
            new LoggerFactory().CreateLogger<MyInvoisMapper>());

        // --- REAL Submitter ---
        var submitter = new MyInvoiceSubmitter(
            httpClientFactoryMock.Object,
            Options.Create(apiSettings),
            new LoggerFactory().CreateLogger<MyInvoiceSubmitter>());

        // --- MOCK Audit Logger (no SQL Server needed) ---
        var auditLoggerMock = new Mock<IAuditLogger>();
        auditLoggerMock.Setup(a => a.IsInvoiceAlreadySubmitted(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // --- REAL Processor ---
        var processor = new InvoiceProcessor(
            reader,
            mapper,
            submitter,
            auditLoggerMock.Object,
            new LoggerFactory().CreateLogger<InvoiceProcessor>());

        // ====================================================================
        // ACT — Process invoices for the configured date range
        // ====================================================================

        // SmokeTest:FromDate / ToDate in appsettings.json (or User Secrets)
        // Format: "yyyy-MM-dd"  e.g. "2026-01-01"
        // Leave empty to default to the current calendar month.
        var fromDateStr = _configuration["SmokeTest:FromDate"];
        var toDateStr   = _configuration["SmokeTest:ToDate"];

        var now = DateTime.UtcNow;
        var fromDate = string.IsNullOrWhiteSpace(fromDateStr)
            ? new DateTime(now.Year, now.Month, 1)
            : DateTime.Parse(fromDateStr);
        var toDate = string.IsNullOrWhiteSpace(toDateStr)
            ? fromDate.AddMonths(1).AddSeconds(-1)
            : DateTime.Parse(toDateStr).Date.AddDays(1).AddSeconds(-1); // inclusive end-of-day

        _output.WriteLine($"--- FETCHING INVOICES FROM MOVEX DB2: {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd} ---");

        var result = await processor.ProcessDateRangeBatch(fromDate, toDate);

        // ====================================================================
        // ASSERT & REPORT — Structured output for stakeholders
        // ====================================================================

        _output.WriteLine("");
        _output.WriteLine("=== BATCH PROCESSING RESULTS ===");
        _output.WriteLine($"Total Invoices Found: {result.TotalInvoices}");
        _output.WriteLine($"Success Count: {result.SuccessCount}");
        _output.WriteLine($"Failed Count: {result.FailedCount}");
        _output.WriteLine($"Skipped Count: {result.SkippedCount}");
        _output.WriteLine($"Duration: {result.CompletedAt - result.StartedAt}");
        _output.WriteLine("");

        if (result.TotalInvoices == 0)
        {
            _output.WriteLine("⚠️ No invoices found in MOVEX for current month.");
            _output.WriteLine("   Check: MovexDb:ActiveCompanyCodes and current month data in CMP300.");
            return;
        }

        // --- Detailed Submission Report ---
        _output.WriteLine("=== INVOICE DETAILS ===");
        foreach (var submission in result.Submissions.Take(10)) // Limit to first 10 for readability
        {
            _output.WriteLine($"\nInvoice: {submission.InvoiceNumber}");
            _output.WriteLine($"  Status: {submission.Status}");
            if (!string.IsNullOrEmpty(submission.MyInvoisUUID))
                _output.WriteLine($"  MyInvois UUID: {submission.MyInvoisUUID}");
            if (!string.IsNullOrEmpty(submission.ErrorCode))
                _output.WriteLine($"  Error Code: {submission.ErrorCode}");
            if (!string.IsNullOrEmpty(submission.ErrorMessage))
                _output.WriteLine($"  Error Message: {submission.ErrorMessage}");
            _output.WriteLine($"  Duration: {submission.DurationMs}ms");
        }

        if (result.Submissions.Count > 10)
        {
            _output.WriteLine($"\n... and {result.Submissions.Count - 10} more invoices.");
        }

        // --- Validation Gap Analysis ---
        _output.WriteLine("");
        _output.WriteLine("=== VALIDATION GAP ANALYSIS ===");

        var validationFailures = result.Submissions
            .Where(s => s.Status == "Failed" && s.ErrorCode == "VALIDATION")
            .ToList();

        if (validationFailures.Count > 0)
        {
            _output.WriteLine($"Validation Failures: {validationFailures.Count}/{result.TotalInvoices}");
            _output.WriteLine("");
            _output.WriteLine("Common validation errors (indicates missing party data):");

            var errorSummary = validationFailures
                .SelectMany(s => ExtractValidationErrors(s.ErrorMessage ?? ""))
                .GroupBy(e => e)
                .OrderByDescending(g => g.Count())
                .Take(5);

            foreach (var errorGroup in errorSummary)
            {
                _output.WriteLine($"  - {errorGroup.Key}: {errorGroup.Count()} occurrences");
            }

            _output.WriteLine("");
            _output.WriteLine("💡 Next Steps:");
            if (string.IsNullOrEmpty(movexDbSettings.SupplierTinColumn) || string.IsNullOrEmpty(movexDbSettings.CustomerTinColumn))
            {
                _output.WriteLine("   1. Configure TIN/BRN columns in MovexDbSettings (waiting on Finance team)");
                _output.WriteLine("      - MovexDb:SupplierTinColumn (e.g., 'IDCFC1')");
                _output.WriteLine("      - MovexDb:CustomerTinColumn (e.g., 'OKCFC1')");
            }
        }
        else
        {
            _output.WriteLine("✅ All invoices passed validation!");
        }

        // --- MyInvois Submission Analysis ---
        var submissionFailures = result.Submissions
            .Where(s => s.Status == "Failed" && s.ErrorCode != "VALIDATION")
            .ToList();

        if (submissionFailures.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine($"MyInvois Submission Failures: {submissionFailures.Count}");
            var submissionErrors = submissionFailures
                .GroupBy(s => s.ErrorCode)
                .OrderByDescending(g => g.Count());

            foreach (var errorGroup in submissionErrors)
            {
                _output.WriteLine($"  - {errorGroup.Key}: {errorGroup.Count()} occurrences");
            }

            if (submissionFailures.Any(s => s.ErrorCode == "DS301"))
            {
                _output.WriteLine("");
                _output.WriteLine("💡 DS301 (Invalid Signature) detected:");
                _output.WriteLine("   - XAdES v1.1 signing is placeholder — need to implement UBL 2.1 serialization + signing");
                _output.WriteLine("   - This is expected until MyInvois SDK is integrated");
            }
        }

        _output.WriteLine("");
        _output.WriteLine("=== END OF SMOKE TEST ===");

        // Basic assertion — test should not throw exceptions
        result.Should().NotBeNull();
        result.TotalInvoices.Should().BeGreaterThanOrEqualTo(0);
    }

    #region Helper Methods

    private void SetupMockOAuthResponse(Mock<HttpMessageHandler> handlerMock)
    {
        handlerMock.Protected()
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
                    access_token = "smoke-test-token",
                    token_type = "Bearer",
                    expires_in = 3600
                }))
            });
    }

    private void SetupMockSubmissionResponse(Mock<HttpMessageHandler> handlerMock)
    {
        handlerMock.Protected()
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
                    uuid = $"SMOKE-{Guid.NewGuid():N}",
                    submissionDate = DateTime.UtcNow.ToString("o"),
                    status = "Valid"
                }))
            });
    }

    private static List<string> ExtractValidationErrors(string errorMessage)
    {
        // Extract validation error messages from the error string
        var errors = new List<string>();
        if (string.IsNullOrEmpty(errorMessage))
            return errors;

        var lines = errorMessage.Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("TIN") || trimmed.Contains("BRN") || trimmed.Contains("required"))
            {
                errors.Add(trimmed);
            }
        }

        return errors;
    }

    #endregion
}
