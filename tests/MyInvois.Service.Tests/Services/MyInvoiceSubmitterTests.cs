
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

        _sut = new MyInvoiceSubmitter(
            _httpClientFactoryMock.Object,
            Options.Create(_apiSettings),
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullHttpClientFactory_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var action = () => new MyInvoiceSubmitter(
            null!,
            Options.Create(_apiSettings),
            _loggerMock.Object);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var action = () => new MyInvoiceSubmitter(
            _httpClientFactoryMock.Object,
            null!,
            _loggerMock.Object);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("settings");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var action = () => new MyInvoiceSubmitter(
            _httpClientFactoryMock.Object,
            Options.Create(_apiSettings),
            null!);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region GetAccessToken Tests

    [Fact]
    public async Task GetAccessToken_FirstCall_FetchesNewToken()
    {
        // Arrange
        var tokenResponse = new
        {
            access_token = "new-access-token",
            token_type = "Bearer",
            expires_in = 3600
        };

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains("/connect/token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse)
            });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri(_apiSettings.BaseUrl)
        };

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("MyInvois"))
            .Returns(httpClient);

        // Act
        var result = await _sut.GetAccessToken();

        // Assert
        result.Should().Be("new-access-token");
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetAccessToken_CachedValid_ReturnsCachedToken()
    {
        // Arrange
        var tokenResponse = new
        {
            access_token = "cached-token",
            token_type = "Bearer",
            expires_in = 3600
        };

        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(tokenResponse)
            });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri(_apiSettings.BaseUrl)
        };

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("MyInvois"))
            .Returns(httpClient);

        // Act - First call
        var firstToken = await _sut.GetAccessToken();

        // Act - Second call (should use cache)
        var secondToken = await _sut.GetAccessToken();

        // Assert
        firstToken.Should().Be("cached-token");
        secondToken.Should().Be("cached-token");
        secondToken.Should().BeSameAs(firstToken);

        // Verify HTTP call was made only once (cached for second call)
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetAccessToken_CachedExpired_FetchesNewToken()
    {
        // Arrange - First token with very short expiry (already expired)
        var firstTokenResponse = new
        {
            access_token = "expired-token",
            token_type = "Bearer",
            expires_in = -1 // Already expired
        };

        var secondTokenResponse = new
        {
            access_token = "refreshed-token",
            token_type = "Bearer",
            expires_in = 3600
        };

        var callCount = 0;
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                var response = callCount == 1 ? firstTokenResponse : secondTokenResponse;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = JsonContent.Create(response)
                };
            });

        var httpClient = new HttpClient(httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri(_apiSettings.BaseUrl)
        };

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("MyInvois"))
            .Returns(httpClient);

        // Act
        var firstToken = await _sut.GetAccessToken();
        var secondToken = await _sut.GetAccessToken(); // Should fetch new token

        // Assert
        firstToken.Should().Be("expired-token");
        secondToken.Should().Be("refreshed-token");

        // Verify HTTP calls were made twice (token expired)
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
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

        var submissionResponse = new
        {
            uuid = "12345678-1234-1234-1234-123456789012",
            submissionDate = "2026-02-17T10:00:00Z",
            status = "Valid"
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
    public async Task Submit_RateLimit_RetriesWithBackoff()
    {
        // Arrange
        var document = CreateValidMyInvoiceDocument();

        var tokenResponse = new
        {
            access_token = "valid-token",
            token_type = "Bearer",
            expires_in = 3600
        };

        var submissionResponse = new
        {
            uuid = "12345678-1234-1234-1234-123456789012",
            submissionDate = "2026-02-17T10:00:00Z",
            status = "Valid"
        };

        var callCount = 0;
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

        // Mock submission endpoint - first call returns 429, second succeeds
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("/documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.TooManyRequests, // 429
                        Content = new StringContent("{\"error\":\"rate_limit_exceeded\"}")
                    };
                }

                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = JsonContent.Create(submissionResponse)
                };
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

        // Verify retry happened (2 submission calls total)
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString().Contains("/documentsubmissions")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Submit_ServerError_RetriesUpTo3Times()
    {
        // Arrange
        var document = CreateValidMyInvoiceDocument();

        var tokenResponse = new
        {
            access_token = "valid-token",
            token_type = "Bearer",
            expires_in = 3600
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

        // Mock submission endpoint - always returns 500
        httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("/documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError, // 500
                Content = new StringContent("{\"error\":\"internal_server_error\"}")
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
        result.ErrorCode.Should().NotBeNullOrEmpty();

        // Verify 1 initial + 3 retries = 4 total calls (per resilience-patterns skill)
        httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(4),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.ToString().Contains("/documentsubmissions")),
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

    #endregion

    #region Helper Methods

    private MyInvoiceDocument CreateValidMyInvoiceDocument()
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
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine
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
            }
        };
    }

    #endregion
}
