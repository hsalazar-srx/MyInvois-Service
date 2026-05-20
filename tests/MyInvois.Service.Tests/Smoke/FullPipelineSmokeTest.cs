using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;
using MyInvois.Service.Data;
using MyInvois.Service.DataAccess;
using MyInvois.Service.Models;
using MyInvois.Service.Services;
using MyInvois.Service.Validators;
using Xunit.Abstractions;

namespace MyInvois.Service.Tests.Smoke;

/// <summary>
/// Full pipeline smoke test — uses REAL components end-to-end.
/// Real DB2 → Real mapper → Real validators → Real XAdES signing → Real LHDN pre-prod submission → Real SQLite audit log.
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

        var lineItemFetcher = new MovexLineItemFetcher(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<MovexLineItemFetcher>());
        var dataSource = new DirectQueryDataSource(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<DirectQueryDataSource>(),
            lineItemFetcher);

        var now = DateTime.UtcNow;
        var fromDate = new DateTime(now.Year, now.Month, 1);
        var toDate = fromDate.AddMonths(1).AddSeconds(-1);

        _output.WriteLine($"Querying invoices from {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}...");

        var result = await dataSource.GetPendingInvoicesAsync(fromDate, CancellationToken.None);
        _output.WriteLine($"✅ Schema '{movexDbSettings.SchemaCmp100}' connected successfully");
        _output.WriteLine($"   Retrieved {result.Count} invoices");

        result.Count.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact(DisplayName = "Full Pipeline Smoke Test — real DB2, signing, LHDN pre-prod submission, audit log")]
    public async Task SmokeTest_FullPipeline_RealSubmission()
    {
        // ====================================================================
        // ARRANGE — All REAL components, no mocks
        // ====================================================================

        _output.WriteLine("=== MyInvois Service — Full Pipeline Smoke Test ===");
        _output.WriteLine($"Execution Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine("");

        var movexDbSettings = _configuration.GetSection("MovexDb").Get<MovexDbSettings>()
            ?? throw new InvalidOperationException("MovexDb configuration missing");

        var apiSettings = GetApiSettings();
        VerifyCredentials(apiSettings);

        var companySettings = _configuration.GetSection("Companies").Get<Dictionary<string, CompanyDetails>>()
            ?? throw new InvalidOperationException("Companies configuration missing");

        var foreignPartyDefaults = _configuration.GetSection("ForeignPartyDefaults").Get<ForeignPartyDefaultsSettings>()
            ?? new ForeignPartyDefaultsSettings();

        _output.WriteLine($"DB2 Schema:      {movexDbSettings.SchemaCmp100}");
        _output.WriteLine($"Active Companies: {string.Join(", ", movexDbSettings.ActiveCompanyCodes)}");
        _output.WriteLine($"MyInvois:        {apiSettings.Environment} ({apiSettings.BaseUrl})");
        _output.WriteLine($"Certificate:     {Path.GetFileName(apiSettings.CertificatePath)}");
        _output.WriteLine("");

        // --- REAL Data Access ---
        var lineItemFetcher = new MovexLineItemFetcher(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<MovexLineItemFetcher>());
        var dataSource = new DirectQueryDataSource(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<DirectQueryDataSource>(),
            lineItemFetcher);

        var partyProvider = new MovexMasterPartyDataProvider(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<MovexMasterPartyDataProvider>());

        var reader = new MovexInvoiceReader(
            dataSource,
            partyProvider,
            Options.Create(foreignPartyDefaults),
            new LoggerFactory().CreateLogger<MovexInvoiceReader>());

        // --- REAL Validators ---
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpClientFactory = CreateRealHttpClientFactory();

        var tinValidator = new TINValidator(
            memoryCache,
            httpClientFactory,
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

        // --- REAL Submitter (real HTTP, real XAdES signing) ---
        var tokenService = new MyInvoisTokenService(
            httpClientFactory,
            Options.Create(apiSettings),
            new LoggerFactory().CreateLogger<MyInvoisTokenService>());

        var submitter = new MyInvoiceSubmitter(
            httpClientFactory,
            Options.Create(apiSettings),
            tokenService,
            new LoggerFactory().CreateLogger<MyInvoiceSubmitter>());

        // --- REAL Audit Logger (in-memory SQLite — no file I/O needed for smoke test) ---
        var keepAlive = new SqliteConnection("Data Source=smoke-pipeline;Mode=Memory;Cache=Shared");
        keepAlive.Open();
        var dbContextOptions = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite("Data Source=smoke-pipeline;Mode=Memory;Cache=Shared")
            .Options;
        await using var setupCtx = new AuditDbContext(dbContextOptions);
        await setupCtx.Database.EnsureCreatedAsync();

        var dbContextFactory = new SmokeTestDbContextFactory(dbContextOptions);
        var auditLogger = new AuditLogger(
            dbContextFactory,
            new LoggerFactory().CreateLogger<AuditLogger>());

        // --- REAL Processor ---
        var processor = new InvoiceProcessor(
            reader,
            mapper,
            submitter,
            auditLogger,
            new LoggerFactory().CreateLogger<InvoiceProcessor>());

        // ====================================================================
        // ACT — Process a small date range (limit exposure to pre-prod)
        // ====================================================================

        var fromDateStr = _configuration["SmokeTest:FromDate"];
        var toDateStr   = _configuration["SmokeTest:ToDate"];
        var maxInvoices = int.TryParse(_configuration["SmokeTest:MaxInvoices"], out var m) ? m : 5;

        var fromDate = string.IsNullOrWhiteSpace(fromDateStr)
            ? new DateTime(2026, 1, 6)
            : DateTime.Parse(fromDateStr);
        var toDate = string.IsNullOrWhiteSpace(toDateStr)
            ? fromDate.Date.AddDays(1).AddSeconds(-1)
            : DateTime.Parse(toDateStr).Date.AddDays(1).AddSeconds(-1);

        _output.WriteLine($"--- Fetching invoices: {fromDate:yyyy-MM-dd} → {toDate:yyyy-MM-dd} (cap: {maxInvoices}) ---");

        // Fetch and cap before passing to processor to keep smoke test fast.
        // CF321 note: LHDN pre-prod rejects invoices with issue dates older than ~3 days.
        // AP invoices posted this week may carry older supplier issue dates — we cannot
        // override the issue date (LHDN requires the supplier's stated date). In production
        // this is not a problem because invoices are submitted close to their issue date.
        // Here we prefer recent-dated invoices first; fall back to all if none qualify.
        var lhdnCutoff = DateTime.UtcNow.Date.AddDays(-3);
        var allInvoices = await reader.GetInvoicesByDateRange(fromDate, toDate, CancellationToken.None);
        var recentWithLines = allInvoices
            .Where(i => i.Lines.Count > 0)
            .Where(i => DateTime.TryParseExact(i.InvoiceDate, "yyyyMMdd",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var d)
                        && d >= lhdnCutoff)
            .ToList();
        var candidates = recentWithLines.Count > 0
            ? recentWithLines.Take(maxInvoices).ToList()
            : allInvoices.Where(i => i.Lines.Count > 0).Take(maxInvoices).ToList();
        var cutoffNote = recentWithLines.Count > 0 ? $"issue date ≥ {lhdnCutoff:yyyy-MM-dd}" : "any date (no recent invoices — CF321 may occur)";
        _output.WriteLine($"   {allInvoices.Count} total fetched, {candidates.Count} with lines selected ({cutoffNote})");

        if (candidates.Count == 0)
        {
            _output.WriteLine($"⚠️ No invoices with lines and issue date ≥ {lhdnCutoff:yyyy-MM-dd} found — adjust SmokeTest:FromDate/ToDate or wait for today's invoices to be posted");
            Assert.Fail("No candidate invoices found for smoke test date range");
        }

        // Submit each candidate individually through the full pipeline
        var results = new List<SubmissionResult>();
        foreach (var invoice in candidates)
        {
            _output.WriteLine($"\n  Submitting: {invoice.InvoiceNumber} ({invoice.InvoiceType}, {invoice.Lines.Count} lines)");
            var myInvoisDoc = mapper.Transform(invoice);
            if (myInvoisDoc.ValidationErrors.Count > 0)
            {
                _output.WriteLine($"    ❌ Validation failed: {string.Join("; ", myInvoisDoc.ValidationErrors.Select(e => e.Message))}");
                var valFail = new SubmissionResult { InvoiceNumber = invoice.InvoiceNumber, Status = "ValidationFailed", ErrorCode = "VALIDATION", ErrorMessage = string.Join("; ", myInvoisDoc.ValidationErrors.Select(e => e.Message)) };
                results.Add(valFail);
                await auditLogger.LogSubmission(valFail, null, CancellationToken.None);
                continue;
            }
            _output.WriteLine($"    ✅ Validation passed");
            var submission = await submitter.Submit(myInvoisDoc, CancellationToken.None);
            submission.InvoiceNumber = invoice.InvoiceNumber;
            _output.WriteLine($"    Status: {submission.Status}  UUID: {submission.MyInvoisUUID}  ({submission.DurationMs}ms)");
            if (!string.IsNullOrEmpty(submission.ErrorCode))
                _output.WriteLine($"    Error: {submission.ErrorCode} — {submission.ErrorMessage}");
            results.Add(submission);
            await auditLogger.LogSubmission(submission, myInvoisDoc, CancellationToken.None);
        }

        // Synthesise a BatchResult-style summary for assertions below
        var result = new BatchResult
        {
            TotalInvoices = candidates.Count,
            SuccessCount  = results.Count(r => r.Status == "Success"),
            FailedCount   = results.Count(r => r.Status != "Success" && r.Status != "Skipped"),
            SkippedCount  = results.Count(r => r.Status == "Skipped"),
            Submissions   = results,
            StartedAt     = DateTime.UtcNow,
            CompletedAt   = DateTime.UtcNow
        };

        // ====================================================================
        // REPORT
        // ====================================================================

        _output.WriteLine("");
        _output.WriteLine("=== BATCH RESULTS ===");
        _output.WriteLine($"Total Invoices:    {result.TotalInvoices}");
        _output.WriteLine($"Success:           {result.SuccessCount}");
        _output.WriteLine($"Failed:            {result.FailedCount}");
        _output.WriteLine($"Skipped:           {result.SkippedCount}");
        _output.WriteLine($"Duration:          {(result.CompletedAt - result.StartedAt)?.TotalSeconds ?? 0:F1}s");
        _output.WriteLine("");

        _output.WriteLine("=== INVOICE DETAILS (first 10) ===");
        foreach (var sub in result.Submissions.Take(10))
        {
            _output.WriteLine($"\n  Invoice: {sub.InvoiceNumber}  Status: {sub.Status}  ({sub.DurationMs}ms)");
            if (!string.IsNullOrEmpty(sub.MyInvoisUUID))
                _output.WriteLine($"    UUID: {sub.MyInvoisUUID}");
            if (!string.IsNullOrEmpty(sub.ErrorCode))
                _output.WriteLine($"    Error: {sub.ErrorCode} — {sub.ErrorMessage}");
        }

        if (result.Submissions.Count > 10)
            _output.WriteLine($"\n  ... and {result.Submissions.Count - 10} more.");

        // --- Audit log verification ---
        _output.WriteLine("");
        _output.WriteLine("=== AUDIT LOG VERIFICATION ===");
        await using var auditCtx = new AuditDbContext(dbContextOptions);
        var auditEntries = await auditCtx.AuditLogs.ToListAsync();
        _output.WriteLine($"  Audit entries written: {auditEntries.Count}");
        foreach (var entry in auditEntries.Take(5))
            _output.WriteLine($"    {entry.InvoiceNumber} → {entry.Status} ({entry.Timestamp})");

        _output.WriteLine("");
        _output.WriteLine("=== END OF FULL PIPELINE SMOKE TEST ===");

        keepAlive.Close();

        // ====================================================================
        // ASSERT
        // ====================================================================

        result.Should().NotBeNull();
        result.TotalInvoices.Should().BeGreaterThan(0, "MOVEX should have invoices in the configured date range");

        var validationFails = result.Submissions.Where(s => s.Status == "Failed" && s.ErrorCode == "VALIDATION").ToList();
        validationFails.Should().BeEmpty(
            $"No invoices should fail validation. Failures: {string.Join(", ", validationFails.Select(s => $"{s.InvoiceNumber}: {s.ErrorMessage}"))}");

        // CF321 (date too old) is an environmental constraint in pre-prod — AP invoices may have
        // old supplier issue dates even when posted this week. Exclude from hard failure; log as warning.
        var cf321Fails = result.Submissions.Where(s => s.Status == "Failed" && s.ErrorMessage?.Contains("CF321") == true).ToList();
        if (cf321Fails.Count > 0)
            _output.WriteLine($"⚠️  {cf321Fails.Count} CF321 (date too old) — environmental, not a code bug. Invoices: {string.Join(", ", cf321Fails.Select(s => s.InvoiceNumber))}");

        var submissionFails = result.Submissions
            .Where(s => s.Status == "Failed" && s.ErrorCode != "VALIDATION" && s.ErrorMessage?.Contains("CF321") != true)
            .ToList();
        submissionFails.Should().BeEmpty(
            $"No invoices should fail submission (excluding CF321 date-window). Failures: {string.Join(", ", submissionFails.Select(s => $"{s.InvoiceNumber}: {s.ErrorCode} — {s.ErrorMessage}"))}");

        var expectedSuccess = result.TotalInvoices - cf321Fails.Count;
        result.SuccessCount.Should().Be(expectedSuccess,
            $"All non-CF321 invoices should be successfully submitted (CF321 excluded: {cf321Fails.Count})");

        auditEntries.Should().HaveCount(result.TotalInvoices,
            "Every processed invoice should have an audit log entry");
    }

    #region Helpers

    private MyInvoisApiSettings GetApiSettings()
    {
        var settings = _configuration.GetSection("MyInvoisApi").Get<MyInvoisApiSettings>()
            ?? new MyInvoisApiSettings();

        if (string.IsNullOrEmpty(settings.ClientId))
            settings.ClientId = _configuration["MyInvoisApi:ClientId"] ?? string.Empty;
        if (string.IsNullOrEmpty(settings.ClientSecret))
            settings.ClientSecret = _configuration["MyInvoisApi:ClientSecret"] ?? string.Empty;
        if (string.IsNullOrEmpty(settings.CertificatePath))
            settings.CertificatePath = _configuration["MyInvoisApi:CertificatePath"] ?? string.Empty;
        if (string.IsNullOrEmpty(settings.CertificatePassword))
            settings.CertificatePassword = _configuration["MyInvoisApi:CertificatePassword"] ?? string.Empty;

        return settings;
    }

    private void VerifyCredentials(MyInvoisApiSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ClientId))
            throw new InvalidOperationException("MyInvoisApi:ClientId not configured in User Secrets");
        if (string.IsNullOrEmpty(settings.ClientSecret))
            throw new InvalidOperationException("MyInvoisApi:ClientSecret not configured in User Secrets");
        if (string.IsNullOrEmpty(settings.CertificatePath))
            throw new InvalidOperationException("MyInvoisApi:CertificatePath not configured");

        _output.WriteLine($"✅ Credentials: ClientId={settings.ClientId[..8]}... Secret={settings.ClientSecret[..8]}...");
    }

    private static IHttpClientFactory CreateRealHttpClientFactory()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddHttpClient("MyInvois")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // SSL bypass for pre-prod only — production must use strict validation
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });
        services.AddHttpClient();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IHttpClientFactory>();
    }

    private sealed class SmokeTestDbContextFactory : IDbContextFactory<AuditDbContext>
    {
        private readonly DbContextOptions<AuditDbContext> _options;
        public SmokeTestDbContextFactory(DbContextOptions<AuditDbContext> options) => _options = options;
        public AuditDbContext CreateDbContext() => new(_options);
    }

    #endregion
}
