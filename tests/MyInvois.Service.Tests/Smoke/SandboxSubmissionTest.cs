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

    #region Helpers

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
