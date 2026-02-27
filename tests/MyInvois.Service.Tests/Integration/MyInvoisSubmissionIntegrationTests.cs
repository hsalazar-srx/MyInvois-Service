using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using MyInvois.Service.Configuration;
using MyInvois.Service.Models;
using MyInvois.Service.Services;
using System.Net;
using System.Text.Json;

namespace MyInvois.Service.Tests.Integration;

/// <summary>
/// Integration tests for MyInvois API submission flow
/// Uses skill: architecture/resilience-patterns v1.8+ (retry logic)
/// Uses skill: integration/oauth-token-manager v1.0+ (token caching)
/// Uses skill: integration/xades-signer v1.0+ (signature)
///
/// Tests the full OAuth → Sign → Submit → Parse response flow
/// using mocked HTTP to simulate MyInvois Sandbox behavior.
/// </summary>
[Trait("Category", "Integration")]
public class MyInvoisSubmissionIntegrationTests
{
    private readonly MyInvoisApiSettings _settings;
    private readonly Mock<ILogger<MyInvoiceSubmitter>> _loggerMock;

    public MyInvoisSubmissionIntegrationTests()
    {
        _settings = new MyInvoisApiSettings
        {
            BaseUrl = "https://preprod-api.myinvois.hasil.gov.my",
            TokenEndpoint = "https://preprod-api.myinvois.hasil.gov.my/connect/token",
            SubmissionEndpoint = "https://preprod-api.myinvois.hasil.gov.my/api/v1.0/documentsubmissions",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };
        _loggerMock = new Mock<ILogger<MyInvoiceSubmitter>>();
    }

    [Fact]
    public async Task FullSubmissionFlow_ValidInvoice_ReturnsSuccessWithUUID()
    {
        // Arrange - Mock OAuth token response
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        // Token endpoint returns valid token
        SetupTokenResponse(httpMessageHandlerMock);

        // Submission endpoint returns success with UUID
        var submissionResponse = new
        {
            uuid = "DOC-UUID-67890",
            submissionDate = DateTime.UtcNow.ToString("o"),
            status = "Valid"
        };

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(submissionResponse))
            });

        var submitter = CreateSubmitter(httpMessageHandlerMock.Object);
        var document = TestDataFactory.CreateValidDocument("INV-001");

        // Act
        var result = await submitter.Submit(document);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Success");
        result.MyInvoisUUID.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task FullSubmissionFlow_DuplicateInvoice_ReturnsDS302()
    {
        // Arrange
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        SetupTokenResponse(httpMessageHandlerMock);

        // Submission returns 400 with DS302 (duplicate)
        var errorResponse = new
        {
            error = new
            {
                code = "DS302",
                message = "Document has already been submitted"
            }
        };

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(JsonSerializer.Serialize(errorResponse))
            });

        var submitter = CreateSubmitter(httpMessageHandlerMock.Object);
        var document = TestDataFactory.CreateValidDocument("INV-DUP-001");

        // Act
        var result = await submitter.Submit(document);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Failed");
        result.ErrorCode.Should().Contain("DS302");
    }

    [Fact]
    public async Task FullSubmissionFlow_InvalidSignature_ReturnsDS301()
    {
        // Arrange
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        SetupTokenResponse(httpMessageHandlerMock);

        var errorResponse = new
        {
            error = new
            {
                code = "DS301",
                message = "Invalid digital signature"
            }
        };

        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(JsonSerializer.Serialize(errorResponse))
            });

        var submitter = CreateSubmitter(httpMessageHandlerMock.Object);
        var document = TestDataFactory.CreateValidDocument("INV-SIG-001");

        // Act
        var result = await submitter.Submit(document);

        // Assert
        result.Status.Should().Be("Failed");
        result.ErrorCode.Should().Contain("DS301");
    }

    [Fact]
    public async Task OAuthTokenCaching_SecondCall_UsesCachedToken()
    {
        // Arrange
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        // Token endpoint
        var tokenCallCount = 0;
        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                tokenCallCount++;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        access_token = $"token-{tokenCallCount}",
                        token_type = "Bearer",
                        expires_in = 3600
                    }))
                };
            });

        var submitter = CreateSubmitter(httpMessageHandlerMock.Object);

        // Act - get token twice
        var token1 = await submitter.GetAccessToken();
        var token2 = await submitter.GetAccessToken();

        // Assert - only one token request should be made (cached)
        token1.Should().Be(token2);
        tokenCallCount.Should().Be(1);
    }

    [Fact]
    public async Task FullSubmissionFlow_RateLimitThenSuccess_RetriesAndSucceeds()
    {
        // Arrange
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        SetupTokenResponse(httpMessageHandlerMock);

        // First call: 429 (rate limit), Second call: 200 (success)
        var callCount = 0;
        httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new HttpResponseMessage { StatusCode = HttpStatusCode.TooManyRequests };
                }
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        uuid = "DOC-UUID-RETRY",
                        submissionDate = DateTime.UtcNow.ToString("o"),
                        status = "Valid"
                    }))
                };
            });

        var submitter = CreateSubmitter(httpMessageHandlerMock.Object);
        var document = TestDataFactory.CreateValidDocument("INV-RETRY");

        // Act
        var result = await submitter.Submit(document);

        // Assert
        result.Status.Should().Be("Success");
        callCount.Should().Be(2, "first call was 429, second succeeded");
    }

    #region Helpers

    private MyInvoiceSubmitter CreateSubmitter(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        return new MyInvoiceSubmitter(
            httpClientFactoryMock.Object,
            Options.Create(_settings),
            _loggerMock.Object);
    }

    private void SetupTokenResponse(Mock<HttpMessageHandler> handlerMock)
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
                    access_token = "test-access-token-12345",
                    token_type = "Bearer",
                    expires_in = 3600
                }))
            });
    }

    #endregion
}
