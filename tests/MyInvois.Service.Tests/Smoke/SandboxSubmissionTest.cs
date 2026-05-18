using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;
using MyInvois.Service.DataAccess;
using MyInvois.Service.Services;
using MyInvois.Service.Validators;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace MyInvois.Service.Tests.Smoke;

/// <summary>
/// Sandbox submission test — uses ALL REAL components including MyInvois sandbox API.
/// No mocks. Real DB2 → Real mapper → Real validators → Real signing → Real HTTP submission.
///
/// Prerequisites:
///   - User secrets: MovexDb:ConnectionString, MyInvoisApi:ClientId, MyInvoisApi:ClientSecret,
///     MyInvoisApi:CertificatePassword, MyInvoisApi:CertificatePath
///   - Network access to MyInvois sandbox (https://sandbox.myinvois.hasil.gov.my)
///   - IBM i Access ODBC Driver installed
///   - Certificate file (.p12) at configured path
///
/// Run: dotnet test --filter "Category=Sandbox" --logger "console;verbosity=detailed"
/// </summary>
[Trait("Category", "Sandbox")]
[Trait("Category", "RequiresDb2")]
public class SandboxSubmissionTest
{
    private readonly ITestOutputHelper _output;
    private readonly IConfiguration _configuration;

    public SandboxSubmissionTest(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));

        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<SandboxSubmissionTest>(optional: true)
            .Build();
    }

    [Fact(DisplayName = "Sandbox: OAuth token acquisition from MyInvois")]
    public async Task Sandbox_OAuthToken_ShouldAuthenticate()
    {
        // Arrange
        var apiSettings = GetApiSettings();
        VerifyCredentials(apiSettings);

        var httpClientFactory = CreateRealHttpClientFactory();
        var submitter = new MyInvoiceSubmitter(
            httpClientFactory,
            Options.Create(apiSettings),
            new LoggerFactory().CreateLogger<MyInvoiceSubmitter>());

        var identityBase = string.IsNullOrEmpty(apiSettings.IdentityBaseUrl)
            ? apiSettings.BaseUrl
            : apiSettings.IdentityBaseUrl;
        _output.WriteLine($"Token endpoint: {identityBase}{apiSettings.TokenEndpoint}");
        _output.WriteLine($"ClientId: {apiSettings.ClientId[..4]}...{apiSettings.ClientId[^4..]}");

        // Act — first do a raw HTTP call to see exact response
        var rawClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });
        var tokenUrl = $"{identityBase}{apiSettings.TokenEndpoint}";
        var rawRequest = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "client_credentials" },
                { "client_id", apiSettings.ClientId },
                { "client_secret", apiSettings.ClientSecret },
                { "scope", "InvoicingAPI" }
            })
        };
        var rawResponse = await rawClient.SendAsync(rawRequest);
        var rawBody = await rawResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"HTTP {(int)rawResponse.StatusCode} {rawResponse.StatusCode}");
        _output.WriteLine($"Content-Type: {rawResponse.Content.Headers.ContentType}");
        _output.WriteLine($"Response body (first 500 chars): {rawBody[..Math.Min(rawBody.Length, 500)]}");

        rawResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
            $"Token endpoint should return 200. Got: {rawBody[..Math.Min(rawBody.Length, 200)]}");

        // Now test via submitter
        var token = await submitter.GetAccessToken(CancellationToken.None);

        // Assert
        _output.WriteLine($"✅ OAuth token acquired: {token[..20]}...");
        token.Should().NotBeNullOrWhiteSpace("OAuth token should be returned from sandbox");
    }

    [Fact(DisplayName = "Sandbox: Certificate loading and signing")]
    public async Task Sandbox_Certificate_ShouldLoadAndSign()
    {
        // Arrange
        var apiSettings = GetApiSettings();

        _output.WriteLine($"Certificate: {apiSettings.CertificatePath}");
        _output.WriteLine($"Password: {"*".PadRight(apiSettings.CertificatePassword.Length, '*')}");

        var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
            apiSettings.CertificatePath,
            apiSettings.CertificatePassword,
            System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable);

        // Assert certificate properties
        _output.WriteLine($"✅ Certificate loaded");
        _output.WriteLine($"   Subject: {cert.Subject}");
        _output.WriteLine($"   Issuer: {cert.Issuer}");
        _output.WriteLine($"   Valid: {cert.NotBefore:yyyy-MM-dd} to {cert.NotAfter:yyyy-MM-dd}");
        _output.WriteLine($"   HasPrivateKey: {cert.HasPrivateKey}");
        _output.WriteLine($"   Thumbprint: {cert.Thumbprint}");
        _output.WriteLine($"   SignatureAlgorithm: {cert.SignatureAlgorithm.FriendlyName}");

        cert.HasPrivateKey.Should().BeTrue("PKCS#12 certificate must contain private key for signing");
        cert.NotAfter.Should().BeAfter(DateTime.UtcNow, "Certificate must not be expired");

        await Task.CompletedTask;
    }

    [Fact(DisplayName = "Sandbox: End-to-end — fetch from DB2, sign, submit to MyInvois sandbox")]
    public async Task Sandbox_EndToEnd_FetchSignSubmit()
    {
        // ====================================================================
        // ARRANGE — ALL REAL COMPONENTS, NO MOCKS
        // ====================================================================

        _output.WriteLine("=== SANDBOX END-TO-END SUBMISSION TEST ===");
        _output.WriteLine($"Execution Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine("");

        var movexDbSettings = _configuration.GetSection("MovexDb").Get<MovexDbSettings>()
            ?? throw new InvalidOperationException("MovexDb configuration missing");

        var apiSettings = GetApiSettings();
        VerifyCredentials(apiSettings);

        var companySettings = _configuration.GetSection("Companies").Get<Dictionary<string, CompanyDetails>>()
            ?? throw new InvalidOperationException("Companies configuration missing");

        _output.WriteLine($"DB2 Schema: {movexDbSettings.SchemaCmp100}");
        _output.WriteLine($"MyInvois: {apiSettings.Environment} ({apiSettings.BaseUrl})");
        _output.WriteLine($"Certificate: {Path.GetFileName(apiSettings.CertificatePath)}");
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
            Options.Create(_configuration.GetSection("ForeignPartyDefaults").Get<ForeignPartyDefaultsSettings>()
                ?? new ForeignPartyDefaultsSettings()),
            new LoggerFactory().CreateLogger<MovexInvoiceReader>());

        // --- REAL Validators ---
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpClientFactory = CreateRealHttpClientFactory();

        var tinValidator = new TINValidator(
            memoryCache,
            httpClientFactory,
            Options.Create(apiSettings),
            new LoggerFactory().CreateLogger<TINValidator>());

        var mapper = new MyInvoisMapper(
            new MandatoryFieldsValidator(),
            tinValidator,
            new DateValidator(),
            new CurrencyValidator(),
            new TotalsValidator(),
            Options.Create(new CompanySettings { Companies = companySettings }),
            new LoggerFactory().CreateLogger<MyInvoisMapper>());

        // --- REAL Submitter (hits actual sandbox API) ---
        var submitter = new MyInvoiceSubmitter(
            httpClientFactory,
            Options.Create(apiSettings),
            new LoggerFactory().CreateLogger<MyInvoiceSubmitter>());

        // ====================================================================
        // ACT — Fetch invoices, pick first valid one, submit to sandbox
        // ====================================================================

        // Step 1: Fetch fully-enriched invoices from MOVEX via date range
        // Use GetInvoicesByDateRange (same path as FullPipelineSmokeTest) to get MovexInvoice
        // objects with line items already loaded — avoids the single-ID AR line-fetch issue.
        _output.WriteLine("--- Step 1: Fetching invoices from MOVEX DB2 ---");
        var fromDate = DateTime.UtcNow.AddMonths(-3); // Look back 3 months for data
        var toDate = DateTime.UtcNow;
        var enrichedInvoices = await reader.GetInvoicesByDateRange(fromDate, toDate, CancellationToken.None);

        _output.WriteLine($"   Found {enrichedInvoices.Count} enriched invoices");
        enrichedInvoices.Count.Should().BeGreaterThan(0, "MOVEX should contain invoices for sandbox testing");

        var arWithLines = enrichedInvoices.Where(i => i.InvoiceType == "Sales" && i.Lines.Count > 0).ToList();
        var apWithLines = enrichedInvoices.Where(i => i.InvoiceType == "Purchase" && i.Lines.Count > 0).ToList();
        _output.WriteLine($"   AR (Sales) with lines: {arWithLines.Count}");
        _output.WriteLine($"   AP (Purchase) with lines: {apWithLines.Count}");

        // Step 2: Prefer AR (Sales, type 01) — our TIN is the supplier (matches OAuth token)
        var candidateInvoices = arWithLines.Count > 0 ? arWithLines : apWithLines;
        _output.WriteLine($"   Using {'A' + (arWithLines.Count > 0 ? "R" : "P")} invoices for test");
        _output.WriteLine("");

        // Step 3: Process each through the full pipeline (submit max 3)
        _output.WriteLine("--- Step 2: Processing through full pipeline ---");
        var results = new List<(string InvoiceNo, string Status, string Detail)>();
        var submittedCount = 0;

        foreach (var invoice in candidateInvoices)
        {
            if (submittedCount >= 3) break;

            var invoiceNo = invoice.InvoiceNumber;
            _output.WriteLine($"\nInvoice: {invoiceNo} ({invoice.InvoiceType})");

            try
            {
                _output.WriteLine($"   Lines: {invoice.Lines.Count}");

                // Map to MyInvois format
                var myInvoiceDoc = mapper.Transform(invoice);

                // Validate
                var isValid = mapper.ValidateDocument(myInvoiceDoc, out var validationErrors);
                if (!isValid)
                {
                    var errorSummary = string.Join("; ", validationErrors.Select(e => e.Message).Take(3));
                    _output.WriteLine($"   ❌ Validation failed: {errorSummary}");
                    results.Add((invoiceNo, "ValidationFailed", errorSummary));
                    continue;
                }

                _output.WriteLine("   ✅ Validation passed");
                _output.WriteLine($"   TypeCode: {myInvoiceDoc.DocumentTypeCode}");
                _output.WriteLine($"   SupplierTIN: {myInvoiceDoc.SupplierTIN}");
                _output.WriteLine($"   BuyerTIN: {myInvoiceDoc.BuyerTIN}");
                _output.WriteLine($"   TotalExclTax: {myInvoiceDoc.TotalExclTax}");
                _output.WriteLine($"   Lines sum: {myInvoiceDoc.Lines.Sum(l => l.LineTotalExclTax):F2}");

                // Print supplier TIN section of UBL for first invoice only (debug)
                if (submittedCount == 0)
                {
                    var ublDoc = MyInvois.Service.Services.UblDocumentBuilder.BuildUnsigned(myInvoiceDoc);
                    var ublJson = MyInvois.Service.Services.UblDocumentBuilder.Minify(ublDoc);
                    var supplierIdx = ublJson.IndexOf("AccountingSupplierParty", StringComparison.Ordinal);
                    if (supplierIdx >= 0)
                        _output.WriteLine($"   Supplier section: {ublJson[supplierIdx..Math.Min(ublJson.Length, supplierIdx + 800)]}");
                }

                submittedCount++;
                // Submit to pre-prod
                var result = await submitter.Submit(myInvoiceDoc, CancellationToken.None);

                _output.WriteLine($"   Status: {result.Status}");
                _output.WriteLine($"   Duration: {result.DurationMs}ms");

                if (result.Status == "Success")
                {
                    _output.WriteLine($"   ✅ UUID: {result.MyInvoisUUID}");
                    results.Add((invoiceNo, "Success", $"UUID: {result.MyInvoisUUID}"));
                }
                else
                {
                    _output.WriteLine($"   ❌ Error: {result.ErrorCode} — {result.ErrorMessage}");
                    _output.WriteLine($"   Raw response: {result.RawResponse}");
                    results.Add((invoiceNo, result.ErrorCode ?? "Failed", result.ErrorMessage ?? "Unknown"));
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"   💥 Exception: {ex.Message}");
                results.Add((invoiceNo, "Exception", ex.Message));
            }
        }

        // ====================================================================
        // REPORT — Structured output
        // ====================================================================

        _output.WriteLine("");
        _output.WriteLine("=== SANDBOX SUBMISSION RESULTS ===");
        _output.WriteLine($"| {"Invoice",-25} | {"Status",-20} | Detail |");
        _output.WriteLine($"|{new string('-', 27)}|{new string('-', 22)}|--------|");

        foreach (var (invoiceNo, status, detail) in results)
        {
            _output.WriteLine($"| {invoiceNo,-25} | {status,-20} | {detail} |");
        }

        var successCount = results.Count(r => r.Status == "Success");
        var validationFailCount = results.Count(r => r.Status == "ValidationFailed");
        var submissionFailCount = results.Count(r => r.Status != "Success" && r.Status != "ValidationFailed" && r.Status != "Skipped");

        _output.WriteLine("");
        _output.WriteLine($"Success: {successCount}/{results.Count}");
        _output.WriteLine($"Validation failures: {validationFailCount}");
        _output.WriteLine($"Submission failures: {submissionFailCount}");

        if (results.Any(r => r.Status == "DS301"))
        {
            _output.WriteLine("");
            _output.WriteLine("⚠️ DS301 (Invalid Signature) — certificate may not be trusted by sandbox.");
            _output.WriteLine("   Check: Is this a sandbox-registered certificate?");
        }

        if (results.Any(r => r.Status == "DS302"))
        {
            _output.WriteLine("");
            _output.WriteLine("ℹ️ DS302 (Duplicate) — invoice was already submitted in a previous test run.");
        }

        _output.WriteLine("");
        _output.WriteLine("=== END OF SANDBOX TEST ===");

        results.Should().NotBeEmpty("At least one invoice should have been processed");

        var validationFails = results.Where(r => r.Status == "ValidationFailed").ToList();
        validationFails.Should().BeEmpty(
            $"No invoices should fail validation. Failures: {string.Join(", ", validationFails.Select(r => $"{r.InvoiceNo}: {r.Detail}"))}");

        var submissionFails = results.Where(r => r.Status != "Success" && r.Status != "ValidationFailed" && r.Status != "Skipped").ToList();
        submissionFails.Should().BeEmpty(
            $"No invoices should fail submission. Failures: {string.Join(", ", submissionFails.Select(r => $"{r.InvoiceNo}: {r.Status} — {r.Detail}"))}");

        _output.WriteLine("");
        _output.WriteLine($"✅ All {results.Count} invoices submitted successfully to LHDN pre-prod.");
    }

    [Fact(DisplayName = "Sandbox: AP (Purchase) — fetch from DB2, sign as self-billed type 11, submit to pre-prod")]
    public async Task Sandbox_AP_FetchSignSubmit()
    {
        _output.WriteLine("=== AP PURCHASE INVOICE SUBMISSION TEST ===");
        _output.WriteLine($"Execution Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine("");

        var movexDbSettings = _configuration.GetSection("MovexDb").Get<MovexDbSettings>()
            ?? throw new InvalidOperationException("MovexDb configuration missing");
        var apiSettings = GetApiSettings();
        VerifyCredentials(apiSettings);
        var companySettings = _configuration.GetSection("Companies").Get<Dictionary<string, CompanyDetails>>()
            ?? throw new InvalidOperationException("Companies configuration missing");

        var dataSource = new DirectQueryDataSource(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<DirectQueryDataSource>());
        var partyProvider = new MovexMasterPartyDataProvider(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<MovexMasterPartyDataProvider>());
        var reader = new MovexInvoiceReader(
            dataSource, partyProvider,
            Options.Create(_configuration.GetSection("ForeignPartyDefaults").Get<ForeignPartyDefaultsSettings>()
                ?? new ForeignPartyDefaultsSettings()),
            new LoggerFactory().CreateLogger<MovexInvoiceReader>());

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpClientFactory = CreateRealHttpClientFactory();
        var tinValidator = new TINValidator(memoryCache, httpClientFactory,
            Options.Create(apiSettings), new LoggerFactory().CreateLogger<TINValidator>());
        var mapper = new MyInvoisMapper(
            new MandatoryFieldsValidator(), tinValidator, new DateValidator(),
            new CurrencyValidator(), new TotalsValidator(),
            Options.Create(new CompanySettings { Companies = companySettings }),
            new LoggerFactory().CreateLogger<MyInvoisMapper>());
        var submitter = new MyInvoiceSubmitter(
            httpClientFactory, Options.Create(apiSettings),
            new LoggerFactory().CreateLogger<MyInvoiceSubmitter>());

        // Step 1 — Fetch and filter to AP only
        _output.WriteLine("--- Step 1: Fetching AP invoices from MOVEX DB2 ---");
        var fromDate = DateTime.UtcNow.AddMonths(-3);
        var enriched = await reader.GetInvoicesByDateRange(fromDate, DateTime.UtcNow, CancellationToken.None);

        var apWithLines = enriched.Where(i => i.InvoiceType == "Purchase" && i.Lines.Count > 0).ToList();
        _output.WriteLine($"   Total enriched:          {enriched.Count}");
        _output.WriteLine($"   AP (Purchase) with lines: {apWithLines.Count}");

        apWithLines.Should().NotBeEmpty(
            "MOVEX should contain AP (Purchase) invoices with lines for the last 3 months. " +
            "Check: (1) eptrcd=10 in AP WHERE clause, (2) foreign supplier data exists, (3) FGINLI line item join.");

        // Step 2 — Inspect first few AP invoices before submitting
        _output.WriteLine("");
        _output.WriteLine("--- Step 2: AP invoice inspection ---");
        foreach (var inv in apWithLines.Take(5))
        {
            _output.WriteLine($"   Invoice: {inv.InvoiceNumber}  Supplier: {inv.Supplier?.Name ?? "?"}  " +
                              $"Lines: {inv.Lines.Count}  Amount: {inv.TotalInclTax:F2} {inv.CurrencyCode}");
        }

        // Step 3 — Map, validate and submit up to 2 AP invoices
        _output.WriteLine("");
        _output.WriteLine("--- Step 3: Map → Validate → Submit (max 2 AP invoices) ---");
        var results = new List<(string InvoiceNo, string Status, string Detail)>();
        var submitted = 0;

        foreach (var invoice in apWithLines)
        {
            if (submitted >= 2) break;

            _output.WriteLine($"\n   Invoice: {invoice.InvoiceNumber}");

            try
            {
                var doc = mapper.Transform(invoice);

                // Diagnostic: show what the mapper produced
                _output.WriteLine($"   TypeCode:     {doc.DocumentTypeCode}  (expected: 11 for self-billed)");
                _output.WriteLine($"   SupplierTIN:  {doc.SupplierTIN}  (foreign supplier)");
                _output.WriteLine($"   BuyerTIN:     {doc.BuyerTIN}   (our company — matches OAuth token)");
                _output.WriteLine($"   Currency:     {doc.CurrencyCode}");
                _output.WriteLine($"   TotalExclTax: {doc.TotalExclTax:F2}");
                _output.WriteLine($"   TotalTax:     {doc.TotalTax:F2}");
                _output.WriteLine($"   TotalInclTax: {doc.TotalInclTax:F2}");
                _output.WriteLine($"   Lines:        {doc.Lines.Count}");
                foreach (var line in doc.Lines.Take(3))
                    _output.WriteLine($"     Line {line.LineNumber}: {line.Description[..Math.Min(line.Description.Length, 40)]}  " +
                                      $"qty={line.Quantity}  price={line.UnitPrice}  total={line.LineTotalExclTax}");

                var isValid = mapper.ValidateDocument(doc, out var errors);
                if (!isValid)
                {
                    var summary = string.Join("; ", errors.Select(e => e.Message).Take(5));
                    _output.WriteLine($"   ❌ Validation failed: {summary}");
                    results.Add((invoice.InvoiceNumber, "ValidationFailed", summary));
                    continue;
                }
                _output.WriteLine("   ✅ Validation passed");

                doc.DocumentTypeCode.Should().Be("11",
                    "AP invoices must be self-billed (type 11) per LHDN SDK v1.5");

                submitted++;
                var result = await submitter.Submit(doc, CancellationToken.None);
                _output.WriteLine($"   Status:   {result.Status}");
                _output.WriteLine($"   Duration: {result.DurationMs}ms");

                if (result.Status == "Success")
                {
                    _output.WriteLine($"   ✅ UUID: {result.MyInvoisUUID}");
                    _output.WriteLine("   ⏳ Portal Step 08 runs in ~2-5 min — check LHDN portal for Valid/Invalid");
                    results.Add((invoice.InvoiceNumber, "Success", $"UUID: {result.MyInvoisUUID}"));
                }
                else
                {
                    _output.WriteLine($"   ❌ {result.ErrorCode}: {result.ErrorMessage}");
                    _output.WriteLine($"   Raw: {result.RawResponse}");
                    results.Add((invoice.InvoiceNumber, result.ErrorCode ?? "Failed", result.ErrorMessage ?? "Unknown"));
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"   💥 Exception: {ex.Message}");
                results.Add((invoice.InvoiceNumber, "Exception", ex.Message));
            }
        }

        // Step 4 — Summary
        _output.WriteLine("");
        _output.WriteLine("=== AP SUBMISSION RESULTS ===");
        _output.WriteLine($"| {"Invoice",-25} | {"Status",-20} | Detail |");
        _output.WriteLine($"|{new string('-', 27)}|{new string('-', 22)}|--------|");
        foreach (var (no, status, detail) in results)
            _output.WriteLine($"| {no,-25} | {status,-20} | {detail} |");

        _output.WriteLine("");
        _output.WriteLine($"Submitted:           {submitted}");
        _output.WriteLine($"Validation failures: {results.Count(r => r.Status == "ValidationFailed")}");
        _output.WriteLine($"Submission failures: {results.Count(r => r.Status != "Success" && r.Status != "ValidationFailed")}");
        _output.WriteLine("=== END AP TEST ===");

        results.Should().NotBeEmpty("At least one AP invoice should have been processed");

        var validationFails = results.Where(r => r.Status == "ValidationFailed").ToList();
        validationFails.Should().BeEmpty(
            $"AP invoices should pass validation. Failures: {string.Join(", ", validationFails.Select(r => $"{r.InvoiceNo}: {r.Detail}"))}");
    }

    [Fact(DisplayName = "Sandbox: Poll status for a known-good UUID — validates GetSubmissionStatus against live LHDN API")]
    public async Task Sandbox_PollStatus_KnownGoodUUID_ReturnsValid()
    {
        // Known-good AR invoice UUID confirmed Valid in LHDN pre-prod portal (2026-05-13)
        // after DecimalNormalizer fix. Using this as a stable fixture so the polling path
        // can be validated without a new submission or a 3-minute Step 08 wait.
        const string knownValidUUID = "WAFDWH4YEA7BEMFEF10X0GRK10";

        _output.WriteLine("=== STATUS POLLING SMOKE TEST ===");
        _output.WriteLine($"Target UUID: {knownValidUUID}");
        _output.WriteLine($"Execution time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine("");

        var apiSettings = GetApiSettings();
        VerifyCredentials(apiSettings);

        var httpClientFactory = CreateRealHttpClientFactory();
        var submitter = new MyInvoiceSubmitter(
            httpClientFactory,
            Options.Create(apiSettings),
            new LoggerFactory().CreateLogger<MyInvoiceSubmitter>());

        _output.WriteLine($"Endpoint: {apiSettings.BaseUrl}{apiSettings.DetailsEndpoint?.Replace("{uuid}", knownValidUUID)}");
        _output.WriteLine("");

        // Act
        _output.WriteLine("--- Calling GetSubmissionStatus ---");
        var status = await submitter.GetSubmissionStatus(knownValidUUID, CancellationToken.None);

        // Report
        _output.WriteLine($"Returned status: {status ?? "(null)"}");
        _output.WriteLine("");

        if (status == "Valid")
        {
            _output.WriteLine("✅ Status is Valid — GetSubmissionStatus correctly reads document status from LHDN API.");
        }
        else if (status == null)
        {
            _output.WriteLine("❌ Null returned — likely an HTTP error or deserialisation failure. Check logs.");
        }
        else
        {
            _output.WriteLine($"⚠️  Unexpected status '{status}'. Valid is expected for this UUID.");
            _output.WriteLine("   Possible causes: pre-prod environment reset, or UUID aged out of the portal index.");
        }

        // Assert
        status.Should().Be("Valid",
            $"UUID {knownValidUUID} was portal-confirmed Valid on 2026-05-13. " +
            "If this fails, the pre-prod environment may have been reset — re-submit a fresh invoice " +
            "and update knownValidUUID with the new UUID.");
    }

    /// <summary>
    /// LHDN Diagnostic Submission — captures every artifact LHDN support requested:
    ///   1. Original submitted document (decoded JSON from base64)
    ///   2. Submission payload (full request body)
    ///   3. Response payload (full response body)
    ///   4. UUID + SubmissionUID
    ///   5. Access token JSON
    ///
    /// All artifacts written to: tests/lhdn-diagnostic/YYYYMMDD-HHmmss/
    /// Run: dotnet test --filter "Category=Sandbox&FullyQualifiedName~LhdnDiagnostic" --logger "console;verbosity=detailed"
    /// </summary>
    [Fact(DisplayName = "LHDN Diagnostic: Capture all submission artifacts for support")]
    public async Task LhdnDiagnostic_CaptureSubmissionArtifacts()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var outputDir = Path.Combine(
            Directory.GetCurrentDirectory(), "..", "..", "..", "..", "lhdn-diagnostic", timestamp);
        Directory.CreateDirectory(outputDir);

        _output.WriteLine("=== LHDN DIAGNOSTIC SUBMISSION ===");
        _output.WriteLine($"Artifacts will be written to: {Path.GetFullPath(outputDir)}");
        _output.WriteLine("");

        var apiSettings = GetApiSettings();
        VerifyCredentials(apiSettings);

        var movexDbSettings = _configuration.GetSection("MovexDb").Get<MovexDbSettings>()
            ?? throw new InvalidOperationException("MovexDb configuration missing");
        var companySettings = _configuration.GetSection("Companies").Get<Dictionary<string, CompanyDetails>>()
            ?? throw new InvalidOperationException("Companies configuration missing");

        // --- Capture handler intercepts all HTTP traffic ---
        var captureHandler = new CapturingHttpMessageHandler();

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddHttpClient("MyInvois")
            .ConfigurePrimaryHttpMessageHandler(() => captureHandler);
        services.AddHttpClient();
        var httpClientFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        // --- Step 1: Get access token ---
        _output.WriteLine("--- Step 1: Acquiring OAuth token ---");
        var submitter = new MyInvoiceSubmitter(
            httpClientFactory,
            Options.Create(apiSettings),
            new LoggerFactory().CreateLogger<MyInvoiceSubmitter>());

        captureHandler.Label = "token";
        var token = await submitter.GetAccessToken(CancellationToken.None);
        _output.WriteLine($"   Token acquired: {token[..20]}...");

        // Write token JSON response
        if (captureHandler.LastResponseBody != null)
        {
            var tokenPath = Path.Combine(outputDir, "01-token-response.json");
            await File.WriteAllTextAsync(tokenPath, PrettyPrint(captureHandler.LastResponseBody));
            _output.WriteLine($"   ✅ Token response → {Path.GetFileName(tokenPath)}");
        }

        // --- Step 2: Fetch invoice from MOVEX ---
        _output.WriteLine("");
        _output.WriteLine("--- Step 2: Fetching AR invoice from MOVEX ---");
        var dataSource = new DirectQueryDataSource(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<DirectQueryDataSource>());
        var partyProvider = new MovexMasterPartyDataProvider(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<MovexMasterPartyDataProvider>());
        var reader = new MovexInvoiceReader(
            dataSource, partyProvider,
            Options.Create(_configuration.GetSection("ForeignPartyDefaults").Get<ForeignPartyDefaultsSettings>()
                ?? new ForeignPartyDefaultsSettings()),
            new LoggerFactory().CreateLogger<MovexInvoiceReader>());

        var invoices = await reader.GetInvoicesByDateRange(
            DateTime.UtcNow.AddMonths(-3), DateTime.UtcNow, CancellationToken.None);
        var arInvoices = invoices.Where(i => i.InvoiceType == "Sales" && i.Lines.Count > 0).ToList();
        arInvoices.Count.Should().BeGreaterThan(0, "Need at least one AR invoice with lines");

        var invoice = arInvoices.First();
        _output.WriteLine($"   Selected invoice: {invoice.InvoiceNumber} ({invoice.Lines.Count} lines)");

        // --- Step 3: Map and sign ---
        _output.WriteLine("");
        _output.WriteLine("--- Step 3: Mapping and signing ---");
        var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var tinValidator = new TINValidator(memoryCache, httpClientFactory,
            Options.Create(apiSettings), new LoggerFactory().CreateLogger<TINValidator>());
        var mapper = new MyInvoisMapper(
            new MandatoryFieldsValidator(), tinValidator, new DateValidator(),
            new CurrencyValidator(), new TotalsValidator(),
            Options.Create(new CompanySettings { Companies = companySettings }),
            new LoggerFactory().CreateLogger<MyInvoisMapper>());

        var myInvoiceDoc = mapper.Transform(invoice);
        myInvoiceDoc.ValidationErrors.Should().BeEmpty("invoice must pass validation");
        _output.WriteLine($"   ✅ Validation passed");

        // Build signed UBL and capture the raw document JSON
        var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
            apiSettings.CertificatePath, apiSettings.CertificatePassword,
            System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable);

        var signedDoc = UblDocumentBuilder.BuildSigned(myInvoiceDoc, certificate);
        var signedJsonMinified = UblDocumentBuilder.Minify(signedDoc);

        // Artifact 1: original submitted document (the decoded JSON LHDN receives)
        var docPath = Path.Combine(outputDir, "02-submitted-document.json");
        await File.WriteAllTextAsync(docPath, PrettyPrint(signedJsonMinified));
        _output.WriteLine($"   ✅ Signed document → {Path.GetFileName(docPath)}");

        // Build submission payload (what we POST to /documentsubmissions)
        var hashBytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(signedJsonMinified));
        var documentHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        var documentBase64 = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(signedJsonMinified));

        var submissionPayload = new
        {
            documents = new[]
            {
                new
                {
                    format = "JSON",
                    documentHash,
                    codeNumber = myInvoiceDoc.InvoiceNumber,
                    document = documentBase64
                }
            }
        };
        var submissionPayloadJson = System.Text.Json.JsonSerializer.Serialize(
            submissionPayload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        // Artifact 2: submission payload (request body)
        var payloadPath = Path.Combine(outputDir, "03-submission-payload.json");
        await File.WriteAllTextAsync(payloadPath, submissionPayloadJson);
        _output.WriteLine($"   ✅ Submission payload → {Path.GetFileName(payloadPath)}");

        // --- Step 4: Submit and capture response ---
        _output.WriteLine("");
        _output.WriteLine("--- Step 4: Submitting to LHDN pre-prod ---");
        captureHandler.Label = "submission";
        var result = await submitter.Submit(myInvoiceDoc, CancellationToken.None);

        _output.WriteLine($"   Status: {result.Status}");
        _output.WriteLine($"   UUID: {result.MyInvoisUUID}");

        // Artifact 3: full response body
        var responsePath = Path.Combine(outputDir, "04-submission-response.json");
        var responseBody = captureHandler.LastResponseBody ?? result.RawResponse ?? "{}";
        await File.WriteAllTextAsync(responsePath, PrettyPrint(responseBody));
        _output.WriteLine($"   ✅ Response payload → {Path.GetFileName(responsePath)}");

        // Artifact 4: summary with UUID + SubmissionUID
        var summaryLines = new System.Text.StringBuilder();
        summaryLines.AppendLine("=== LHDN DIAGNOSTIC SUMMARY ===");
        summaryLines.AppendLine($"Generated:       {DateTime.Now:yyyy-MM-dd HH:mm:ss} (local)");
        summaryLines.AppendLine($"Environment:     {apiSettings.Environment}");
        summaryLines.AppendLine($"Endpoint:        {apiSettings.BaseUrl}");
        summaryLines.AppendLine($"Invoice:         {myInvoiceDoc.InvoiceNumber}");
        summaryLines.AppendLine($"SupplierTIN:     {myInvoiceDoc.SupplierTIN}");
        summaryLines.AppendLine($"ClientId:        {apiSettings.ClientId}");
        summaryLines.AppendLine($"Certificate:     {Path.GetFileName(apiSettings.CertificatePath)}");
        summaryLines.AppendLine($"Cert thumbprint: {certificate.Thumbprint}");
        summaryLines.AppendLine($"Cert valid:      {certificate.NotBefore:yyyy-MM-dd} → {certificate.NotAfter:yyyy-MM-dd}");
        summaryLines.AppendLine("");
        summaryLines.AppendLine($"Submission status:    {result.Status}");
        summaryLines.AppendLine($"Document UUID:        {result.MyInvoisUUID}");
        summaryLines.AppendLine($"Submission UID:       {ExtractSubmissionUid(responseBody)}");
        summaryLines.AppendLine($"Duration:             {result.DurationMs}ms");
        if (!string.IsNullOrEmpty(result.ErrorCode))
        {
            summaryLines.AppendLine($"Error code:           {result.ErrorCode}");
            summaryLines.AppendLine($"Error message:        {result.ErrorMessage}");
        }
        summaryLines.AppendLine("");
        summaryLines.AppendLine("=== FILES FOR LHDN SUPPORT ===");
        summaryLines.AppendLine($"01-token-response.json       — Access token JSON");
        summaryLines.AppendLine($"02-submitted-document.json   — Original submitted document (decoded from base64)");
        summaryLines.AppendLine($"03-submission-payload.json   — Submission payload (POST request body)");
        summaryLines.AppendLine($"04-submission-response.json  — Response payload");
        summaryLines.AppendLine($"05-summary.txt               — This file (UUID, SubmissionUID, metadata)");

        var summaryPath = Path.Combine(outputDir, "05-summary.txt");
        await File.WriteAllTextAsync(summaryPath, summaryLines.ToString());

        // Print everything to test output too
        _output.WriteLine("");
        _output.WriteLine(summaryLines.ToString());
        _output.WriteLine($"=== ALL FILES WRITTEN TO: {Path.GetFullPath(outputDir)} ===");

        // Soft assert — if submission itself failed, still write files but report it
        if (result.Status != "Success")
            _output.WriteLine($"⚠️  Submission returned non-Success status. Check 04-submission-response.json for details.");

        result.Status.Should().Be("Success",
            $"Diagnostic submission must succeed. Error: {result.ErrorCode} — {result.ErrorMessage}");
    }

    #region Helpers

    private static string PrettyPrint(string json)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(
                doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    private static string ExtractSubmissionUid(string responseJson)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("submissionUid", out var uid))
                return uid.GetString() ?? "not found";
            if (doc.RootElement.TryGetProperty("SubmissionUID", out var uid2))
                return uid2.GetString() ?? "not found";
        }
        catch { }
        return "not found";
    }

    /// <summary>
    /// Captures raw request and response bodies from all HTTP calls made through it.
    /// Implemented as DelegatingHandler so it chains on top of the real HttpClientHandler.
    /// </summary>
    private sealed class CapturingHttpMessageHandler : DelegatingHandler
    {
        public string Label { get; set; } = "";
        public string? LastResponseBody { get; private set; }
        public string? LastRequestBody { get; private set; }

        public CapturingHttpMessageHandler() : base(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        }) { }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            var response = await base.SendAsync(request, cancellationToken);

            // Buffer the response so it can be read twice (once here, once by caller)
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            LastResponseBody = System.Text.Encoding.UTF8.GetString(bytes);
            response.Content = new System.Net.Http.ByteArrayContent(bytes);
            response.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            return response;
        }
    }

    private MyInvoisApiSettings GetApiSettings()
    {
        var settings = _configuration.GetSection("MyInvoisApi").Get<MyInvoisApiSettings>()
            ?? throw new InvalidOperationException("MyInvoisApi configuration missing");

        // Also load certificate settings from config
        if (string.IsNullOrEmpty(settings.CertificatePath))
        {
            settings.CertificatePath = _configuration["MyInvoisApi:CertificatePath"] ?? string.Empty;
        }
        if (string.IsNullOrEmpty(settings.CertificatePassword))
        {
            settings.CertificatePassword = _configuration["MyInvoisApi:CertificatePassword"]
                ?? _configuration["certificate:password"] ?? string.Empty;
        }

        return settings;
    }

    private void VerifyCredentials(MyInvoisApiSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ClientId))
            throw new InvalidOperationException("MyInvoisApi:ClientId not configured in User Secrets");
        if (string.IsNullOrEmpty(settings.ClientSecret))
            throw new InvalidOperationException("MyInvoisApi:ClientSecret not configured in User Secrets");

        _output.WriteLine("✅ OAuth credentials configured");
    }

    private static IHttpClientFactory CreateRealHttpClientFactory()
    {
        // Create a real HttpClientFactory that returns actual HttpClients (no mocks)
        // NOTE: SSL validation bypass is for SANDBOX ONLY — the MyInvois sandbox SSL
        // certificate has a known hostname mismatch (RemoteCertificateNameMismatch).
        // Production code must NOT bypass SSL validation.
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddHttpClient("MyInvois")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });
        services.AddHttpClient(); // default client
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IHttpClientFactory>();
    }

    #endregion
}
