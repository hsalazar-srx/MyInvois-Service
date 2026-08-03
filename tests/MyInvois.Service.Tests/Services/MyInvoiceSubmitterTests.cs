namespace MyInvois.Service.Tests.Services;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Services;
using MyInvois.Service.Models;
using MyInvois.Service.Configuration;

/// <summary>
/// Unit tests for MyInvoiceSubmitter service
/// Uses skill: architecture/resilience-patterns v1.8+ (Polly retry logic)
/// Uses skill: integration/oauth-token-manager v1.0+ (token caching)
/// Uses skill: integration/xades-signer v1.0+ (signature generation)
/// </summary>
public class MyInvoiceSubmitterTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<MyInvoiceSubmitter>> _loggerMock;
    private readonly MyInvoisApiSettings _apiSettings;
    private readonly MyInvoiceSubmitter _sut;

    public MyInvoiceSubmitterTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<MyInvoiceSubmitter>>();

        _apiSettings = new MyInvoisApiSettings
        {
            BaseUrl = "https://sandbox.myinvois.hasil.gov.my",
            TokenEndpoint = "/connect/token",
            SubmissionEndpoint = "/api/v1.0/documentsubmissions",
            DetailsEndpoint = "/api/v1.0/documents/{uuid}/details",
            TimeoutSeconds = 30,
            Environment = "sandbox",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            TIN = "C12345678901"
        };

        var tokenService = new MyInvoisTokenService(
            _httpClientFactoryMock.Object,
            Options.Create(_apiSettings),
            new Mock<ILogger<MyInvoisTokenService>>().Object);

        _sut = new MyInvoiceSubmitter(
            _httpClientFactoryMock.Object,
            Options.Create(_apiSettings),
            tokenService,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullHttpClientFactory_ThrowsArgumentNullException()
    {
        var tokenService = new MyInvoisTokenService(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings),
            new Mock<ILogger<MyInvoisTokenService>>().Object);

        var action = () => new MyInvoiceSubmitter(
            null!, Options.Create(_apiSettings), tokenService, _loggerMock.Object);

        action.Should().Throw<ArgumentNullException>().WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        var tokenService = new MyInvoisTokenService(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings),
            new Mock<ILogger<MyInvoisTokenService>>().Object);

        var action = () => new MyInvoiceSubmitter(
            _httpClientFactoryMock.Object, null!, tokenService, _loggerMock.Object);

        action.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Constructor_WithNullTokenService_ThrowsArgumentNullException()
    {
        var action = () => new MyInvoiceSubmitter(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings), null!, _loggerMock.Object);

        action.Should().Throw<ArgumentNullException>().WithParameterName("tokenService");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var tokenService = new MyInvoisTokenService(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings),
            new Mock<ILogger<MyInvoisTokenService>>().Object);

        var action = () => new MyInvoiceSubmitter(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings), tokenService, null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region Submit Tests

    [Fact]
    public async Task Submit_Success_ReturnsSuccessResult()
    {
        // Arrange
        var document = CreateValidMyInvoiceDocument();

        var tokenResponse = new
        {
            access_token = "valid-token",
            token_type = "Bearer",
            expires_in = 3600
        };

        // LHDN envelope: accepted document carries the UUID inside acceptedDocuments[]
        var submissionResponse = new
        {
            submissionUid = "SUB-2026-00001",
            acceptedDocuments = new[]
            {
                new { uuid = "12345678-1234-1234-1234-123456789012", invoiceCodeNumber = document.InvoiceNumber }
            },
            rejectedDocuments = Array.Empty<object>()
        };

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        // Mock token endpoint
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("/connect/token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse)
            });

        // Mock submission endpoint
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("/documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(submissionResponse)
            });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri(_apiSettings.BaseUrl)
        };

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("MyInvois"))
            .Returns(httpClient);

        // Act
        var result = await _sut.Submit(document);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Success");
        result.MyInvoisUUID.Should().Be("12345678-1234-1234-1234-123456789012");
        result.ErrorCode.Should().BeNullOrEmpty();
        result.InvoiceNumber.Should().Be(document.InvoiceNumber);
    }

    [Fact]
    public async Task Submit_RateLimit_RetriesAndSucceedsOnSecondAttempt()
    {
        // Polly retries on HTTP 429. Production delays are 5s/10s/20s — injected as zero here
        // so the test completes instantly while still exercising the retry path.
        var document = CreateValidMyInvoiceDocument();

        var tokenResponse = new { access_token = "valid-token", token_type = "Bearer", expires_in = 3600 };
        var submissionResponse = new
        {
            submissionUid = "SUB-2026-RETRY",
            acceptedDocuments = new[]
            {
                new { uuid = "12345678-1234-1234-1234-123456789012", invoiceCodeNumber = document.InvoiceNumber }
            },
            rejectedDocuments = Array.Empty<object>()
        };

        var callCount = 0;
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/connect/token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = JsonContent.Create(tokenResponse) });

        // First submission call → 429; second → 200 with accepted document
        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new HttpResponseMessage { StatusCode = HttpStatusCode.TooManyRequests, Content = new StringContent("{\"error\":\"rate_limit_exceeded\"}") }
                    : new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = JsonContent.Create(submissionResponse) };
            });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object) { BaseAddress = new Uri(_apiSettings.BaseUrl) };
        _httpClientFactoryMock.Setup(f => f.CreateClient("MyInvois")).Returns(httpClient);

        // Build a submitter with zero-delay retry so the test doesn't sleep for 5 seconds
        var tokenService = new MyInvoisTokenService(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings),
            new Mock<ILogger<MyInvoisTokenService>>().Object);
        var sut = new MyInvoiceSubmitter(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings),
            tokenService, _loggerMock.Object,
            retrySleepProvider: _ => TimeSpan.Zero);

        // Act
        var result = await sut.Submit(document);

        // Assert — retry succeeded
        result.Status.Should().Be("Success");
        result.MyInvoisUUID.Should().Be("12345678-1234-1234-1234-123456789012");

        // 1 initial call + 1 retry = 2 total submission requests
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/documentsubmissions")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Submit_ServerError_RetriesUpTo3Times()
    {
        // Polly retries on HTTP 500 up to 3 times. Zero-delay injection keeps the test fast.
        var document = CreateValidMyInvoiceDocument();
        var tokenResponse = new { access_token = "valid-token", token_type = "Bearer", expires_in = 3600 };

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/connect/token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = JsonContent.Create(tokenResponse) });

        // Always returns 500 — Polly exhausts all 3 retries then gives up
        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("{\"error\":\"internal_server_error\"}")
            });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object) { BaseAddress = new Uri(_apiSettings.BaseUrl) };
        _httpClientFactoryMock.Setup(f => f.CreateClient("MyInvois")).Returns(httpClient);

        var tokenService = new MyInvoisTokenService(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings),
            new Mock<ILogger<MyInvoisTokenService>>().Object);
        var sut = new MyInvoiceSubmitter(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings),
            tokenService, _loggerMock.Object,
            retrySleepProvider: _ => TimeSpan.Zero);

        // Act
        var result = await sut.Submit(document);

        // Assert
        result.Status.Should().Be("Failed");
        result.ErrorCode.Should().NotBeNullOrEmpty();

        // 1 initial + 3 retries = 4 total submission calls
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(4),
            ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/documentsubmissions")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Submit_DuplicateError_NoRetry()
    {
        // Arrange
        var document = CreateValidMyInvoiceDocument();

        var tokenResponse = new
        {
            access_token = "valid-token",
            token_type = "Bearer",
            expires_in = 3600
        };

        var errorResponse = new
        {
            error = new
            {
                code = "DS302",
                message = "Duplicate invoice submission"
            }
        };

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        // Mock token endpoint
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("/connect/token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse)
            });

        // Mock submission endpoint - returns DS302 duplicate error
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("/documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest, // 400
                Content = JsonContent.Create(errorResponse)
            });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri(_apiSettings.BaseUrl)
        };

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("MyInvois"))
            .Returns(httpClient);

        // Act
        var result = await _sut.Submit(document);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Failed");
        result.ErrorCode.Should().Be("DS302");
        result.ErrorMessage.Should().Contain("Duplicate");

        // Verify NO retry (DS302 is non-retriable per ADR-008)
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString().Contains("/documentsubmissions")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Submit_InvalidSignature_NoRetry()
    {
        // Arrange
        var document = CreateValidMyInvoiceDocument();

        var tokenResponse = new
        {
            access_token = "valid-token",
            token_type = "Bearer",
            expires_in = 3600
        };

        var errorResponse = new
        {
            error = new
            {
                code = "DS301",
                message = "Invalid digital signature"
            }
        };

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        // Mock token endpoint
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("/connect/token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse)
            });

        // Mock submission endpoint - returns DS301 signature error
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("/documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest, // 400
                Content = JsonContent.Create(errorResponse)
            });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri(_apiSettings.BaseUrl)
        };

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("MyInvois"))
            .Returns(httpClient);

        // Act
        var result = await _sut.Submit(document);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Failed");
        result.ErrorCode.Should().Be("DS301");
        result.ErrorMessage.Should().Contain("signature");

        // Verify NO retry (DS301 is non-retriable per ADR-008)
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString().Contains("/documentsubmissions")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Submit_Http200WithRejectedDocuments_ReturnsFailed()
    {
        // LHDN returns HTTP 200 even when individual documents are rejected.
        // The rejection is signalled via the rejectedDocuments array in the response body.
        // This is the most common real-world rejection path (e.g. invalid TIN, CF321, DS3xx).
        var document = CreateValidMyInvoiceDocument();

        var tokenResponse = new { access_token = "valid-token", token_type = "Bearer", expires_in = 3600 };

        // HTTP 200 body with the submitted invoice in rejectedDocuments
        var submissionResponse = new
        {
            submissionUid = "SUB-TEST-001",
            acceptedDocuments = Array.Empty<object>(),
            rejectedDocuments = new[]
            {
                new
                {
                    invoiceCodeNumber = document.InvoiceNumber,
                    error = new
                    {
                        code = "CF3151",
                        message = "Buyer TIN is invalid",
                        details = new[]
                        {
                            new { code = "CF3151", message = "TIN 'INVALIDTIN' does not exist in LHDN registry", target = "buyerTin" }
                        }
                    }
                }
            }
        };

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/connect/token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse)
            });

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK, // 200 — LHDN always returns 200 for submission envelope
                Content = JsonContent.Create(submissionResponse)
            });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object) { BaseAddress = new Uri(_apiSettings.BaseUrl) };
        _httpClientFactoryMock.Setup(f => f.CreateClient("MyInvois")).Returns(httpClient);

        // Act
        var result = await _sut.Submit(document);

        // Assert
        result.Status.Should().Be("Failed");
        result.ErrorCode.Should().Be("CF3151");
        result.ErrorMessage.Should().Contain("Buyer TIN is invalid");
        result.ErrorMessage.Should().Contain("CF3151"); // detail code concatenated after " | "
        result.ErrorMessage.Should().Contain("does not exist in LHDN registry"); // detail message included
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "502")]
    [InlineData(HttpStatusCode.GatewayTimeout, "504")]
    public async Task Submit_GatewayError_RetriesAndSucceedsOnSecondAttempt(HttpStatusCode gatewayCode, string _label)
    {
        // 502/504 are LHDN Azure App Proxy transient errors — Polly must retry them.
        var document = CreateValidMyInvoiceDocument();
        var tokenResponse = new { access_token = "valid-token", token_type = "Bearer", expires_in = 3600 };
        var submissionResponse = new
        {
            submissionUid = "SUB-GATEWAY-RETRY",
            acceptedDocuments = new[]
            {
                new { uuid = "12345678-1234-1234-1234-aabbccddeeff", invoiceCodeNumber = document.InvoiceNumber }
            },
            rejectedDocuments = Array.Empty<object>()
        };

        var callCount = 0;
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/connect/token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = JsonContent.Create(tokenResponse) });

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new HttpResponseMessage { StatusCode = gatewayCode }
                    : new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = JsonContent.Create(submissionResponse) };
            });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object) { BaseAddress = new Uri(_apiSettings.BaseUrl) };
        _httpClientFactoryMock.Setup(f => f.CreateClient("MyInvois")).Returns(httpClient);

        var tokenService = new MyInvoisTokenService(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings),
            new Mock<ILogger<MyInvoisTokenService>>().Object);
        var sut = new MyInvoiceSubmitter(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings),
            tokenService, _loggerMock.Object,
            retrySleepProvider: _ => TimeSpan.Zero);

        var result = await sut.Submit(document);

        result.Status.Should().Be("Success");
        result.MyInvoisUUID.Should().Be("12345678-1234-1234-1234-aabbccddeeff");

        // 1 initial + 1 retry = 2 total submission calls
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync", Times.Exactly(2),
            ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/documentsubmissions")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Submit_GatewayError_ExhaustsAllRetriesAndReturnsFailed(HttpStatusCode gatewayCode)
    {
        // Sustained 502/504 — Polly exhausts all 3 retries then returns Failed.
        var document = CreateValidMyInvoiceDocument();
        var tokenResponse = new { access_token = "valid-token", token_type = "Bearer", expires_in = 3600 };

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/connect/token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = JsonContent.Create(tokenResponse) });

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = gatewayCode });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object) { BaseAddress = new Uri(_apiSettings.BaseUrl) };
        _httpClientFactoryMock.Setup(f => f.CreateClient("MyInvois")).Returns(httpClient);

        var tokenService = new MyInvoisTokenService(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings),
            new Mock<ILogger<MyInvoisTokenService>>().Object);
        var sut = new MyInvoiceSubmitter(
            _httpClientFactoryMock.Object, Options.Create(_apiSettings),
            tokenService, _loggerMock.Object,
            retrySleepProvider: _ => TimeSpan.Zero);

        var result = await sut.Submit(document);

        result.Status.Should().Be("Failed");

        // 1 initial + 3 retries = 4 total submission calls
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync", Times.Exactly(4),
            ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/documentsubmissions")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Submit_Http200WithRejectedDocuments_NoRetryAttempted()
    {
        // A rejection inside rejectedDocuments must NOT trigger Polly retry —
        // LHDN returned 200, so the HTTP layer is healthy; retrying would re-submit the same bad invoice.
        var document = CreateValidMyInvoiceDocument();

        var tokenResponse = new { access_token = "valid-token", token_type = "Bearer", expires_in = 3600 };
        var submissionResponse = new
        {
            submissionUid = "SUB-TEST-002",
            acceptedDocuments = Array.Empty<object>(),
            rejectedDocuments = new[]
            {
                new
                {
                    invoiceCodeNumber = document.InvoiceNumber,
                    error = new { code = "CF321", message = "Invoice date too old", details = Array.Empty<object>() }
                }
            }
        };

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/connect/token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = JsonContent.Create(tokenResponse) });

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = JsonContent.Create(submissionResponse) });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object) { BaseAddress = new Uri(_apiSettings.BaseUrl) };
        _httpClientFactoryMock.Setup(f => f.CreateClient("MyInvois")).Returns(httpClient);

        // Act
        var result = await _sut.Submit(document);

        // Assert — Failed with correct code
        result.Status.Should().Be("Failed");
        result.ErrorCode.Should().Be("CF321");

        // Submission endpoint called exactly once — no retry on HTTP 200 rejection
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/documentsubmissions")),
            ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    private static MyInvoiceDocument CreateValidMyInvoiceDocument()
    {
        return new MyInvoiceDocument
        {
            SourceInvoiceNumber = "INV-2026-00001",
            InvoiceNumber = "INV-2026-00001",
            IssueDate = "2026-02-17",
            IssueTime = "10:00:00",
            SupplierTIN = "C12345678901",
            SupplierName = "Test Supplier Sdn Bhd",
            SupplierBRN = "BRN123",
            SupplierIdScheme = "BRN",
            SupplierAddress = "Test Address",
            BuyerTIN = "C98765432109",
            BuyerName = "Test Buyer Sdn Bhd",
            BuyerAlternativeId = "BRN456",
            BuyerIdScheme = "BRN",
            BuyerAddress = "Buyer Address",
            CurrencyCode = "MYR",
            ExchangeRate = 1.0m,
            TotalExclTax = 1000.00m,
            TotalTax = 60.00m,
            TotalInclTax = 1060.00m,
            PayableAmount = 1060.00m,
            Lines =
            [
                new()
                {
                    LineNumber = 1,
                    ItemNumber = "ITEM001",
                    Description = "Test Product",
                    ClassificationCode = "001",
                    Quantity = 10,
                    UnitOfMeasure = "EA",
                    UnitPrice = 100.00m,
                    LineTotalExclTax = 1000.00m,
                    TaxCode = "SR",
                    TaxRate = 6.0m,
                    TaxAmount = 60.00m,
                    LineTotalInclTax = 1060.00m
                }
            ]
        };
    }

    #endregion

    #region GetSubmissionStatus Tests

    private HttpClient CreateHttpClientWithResponse(HttpStatusCode statusCode, object? body)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = body is null
                    ? new StringContent("")
                    : JsonContent.Create(body)
            });
        return new HttpClient(handler.Object) { BaseAddress = new Uri(_apiSettings.BaseUrl) };
    }

    private void SetupTokenAndDetailsClient(HttpClient tokenClient, HttpClient detailsClient)
    {
        // First call → token, subsequent calls → details
        var callCount = 0;
        _httpClientFactoryMock
            .Setup(f => f.CreateClient("MyInvois"))
            .Returns(() => callCount++ == 0 ? tokenClient : detailsClient);
    }

    private HttpClient CreateTokenClient()
        => CreateHttpClientWithResponse(HttpStatusCode.OK, new
        {
            access_token = "test-token",
            token_type = "Bearer",
            expires_in = 3600
        });

    [Fact]
    public async Task GetSubmissionStatus_ValidUUID_ReturnsStatusFromApi()
    {
        // Arrange
        var uuid = "WAFDWH4YEA7BEMFEF10X0GRK10";
        var detailsBody = new
        {
            uuid,
            submissionUid = "SUB001",
            internalId = "INV-001",
            status = "Valid",
            dateTimeValidated = "2026-05-14T12:00:00Z"
        };

        SetupTokenAndDetailsClient(CreateTokenClient(),
            CreateHttpClientWithResponse(HttpStatusCode.OK, detailsBody));

        // Act
        var status = await _sut.GetSubmissionStatus(uuid);

        // Assert
        status.Should().Be("Valid");
    }

    [Fact]
    public async Task GetSubmissionStatus_InvalidDocument_ReturnsInvalidStatus()
    {
        // Arrange
        var detailsBody = new { uuid = "BAD-UUID", status = "Invalid" };
        SetupTokenAndDetailsClient(CreateTokenClient(),
            CreateHttpClientWithResponse(HttpStatusCode.OK, detailsBody));

        // Act
        var status = await _sut.GetSubmissionStatus("BAD-UUID");

        // Assert
        status.Should().Be("Invalid");
    }

    [Fact]
    public async Task GetSubmissionStatus_StillProcessing_ReturnsSubmittedStatus()
    {
        // Arrange — Step 08 not yet complete
        var detailsBody = new { uuid = "PENDING-UUID", status = "Submitted" };
        SetupTokenAndDetailsClient(CreateTokenClient(),
            CreateHttpClientWithResponse(HttpStatusCode.OK, detailsBody));

        // Act
        var status = await _sut.GetSubmissionStatus("PENDING-UUID");

        // Assert
        status.Should().Be("Submitted");
    }

    [Fact]
    public async Task GetSubmissionStatus_ApiReturnsNotFound_ReturnsNull()
    {
        // Arrange
        SetupTokenAndDetailsClient(CreateTokenClient(),
            CreateHttpClientWithResponse(HttpStatusCode.NotFound, null));

        // Act
        var status = await _sut.GetSubmissionStatus("UNKNOWN-UUID");

        // Assert
        status.Should().BeNull();
    }

    [Fact]
    public async Task GetSubmissionStatus_ApiReturnsServerError_ReturnsNull()
    {
        // Arrange
        SetupTokenAndDetailsClient(CreateTokenClient(),
            CreateHttpClientWithResponse(HttpStatusCode.InternalServerError, null));

        // Act
        var status = await _sut.GetSubmissionStatus("ANY-UUID");

        // Assert
        status.Should().BeNull();
    }

    [Fact]
    public async Task GetSubmissionStatus_EmptyUUID_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _sut.GetSubmissionStatus(string.Empty);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("myInvoisUUID");
    }

    [Fact]
    public async Task GetSubmissionStatus_UrlContainsUUID()
    {
        // Arrange — capture the request URL
        var uuid = "WAFDWH4YEA7BEMFEF10X0GRK10";
        HttpRequestMessage? capturedRequest = null;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { uuid, status = "Valid" })
            });

        var detailsClient = new HttpClient(handler.Object) { BaseAddress = new Uri(_apiSettings.BaseUrl) };
        SetupTokenAndDetailsClient(CreateTokenClient(), detailsClient);

        // Act
        await _sut.GetSubmissionStatus(uuid);

        // Assert — URL must contain the UUID
        capturedRequest!.RequestUri!.ToString().Should().Contain(uuid);
    }

    #endregion
}
