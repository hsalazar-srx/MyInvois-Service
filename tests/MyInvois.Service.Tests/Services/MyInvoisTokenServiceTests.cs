namespace MyInvois.Service.Tests.Services;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using MyInvois.Service.Configuration;
using MyInvois.Service.Services;
using System.Net;
using System.Net.Http.Json;

[Trait("Category", "Unit")]
public class MyInvoisTokenServiceTests
{
    private readonly MyInvoisApiSettings _settings = new()
    {
        BaseUrl           = "https://sandbox.myinvois.hasil.gov.my",
        TokenEndpoint     = "/connect/token",
        ClientId          = "test-client-id",
        ClientSecret      = "test-client-secret"
    };

    private MyInvoisTokenService Build(IHttpClientFactory factory) =>
        new(factory, Options.Create(_settings), new Mock<ILogger<MyInvoisTokenService>>().Object);

    private MyInvoisTokenService BuildFast(IHttpClientFactory factory) =>
        new(factory, Options.Create(_settings), new Mock<ILogger<MyInvoisTokenService>>().Object,
            retrySleepProvider: _ => TimeSpan.Zero);

    private static Mock<HttpMessageHandler> HandlerReturning(object body)
    {
        var mock = new Mock<HttpMessageHandler>();
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content    = JsonContent.Create(body)
            });
        return mock;
    }

    private IHttpClientFactory FactoryFor(HttpMessageHandler handler)
    {
        var client  = new HttpClient(handler) { BaseAddress = new Uri(_settings.BaseUrl) };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("MyInvois")).Returns(client);
        return factory.Object;
    }

    [Fact]
    public async Task GetAccessTokenAsync_FirstCall_FetchesNewToken()
    {
        var handlerMock = HandlerReturning(new { access_token = "new-token", token_type = "Bearer", expires_in = 3600 });
        var sut         = Build(FactoryFor(handlerMock.Object));

        var result = await sut.GetAccessTokenAsync();

        result.Should().Be("new-token");
        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachedValid_ReturnsCachedTokenWithoutHttpCall()
    {
        var handlerMock = HandlerReturning(new { access_token = "cached-token", token_type = "Bearer", expires_in = 3600 });
        var sut         = Build(FactoryFor(handlerMock.Object));

        var first  = await sut.GetAccessTokenAsync();
        var second = await sut.GetAccessTokenAsync();

        first.Should().Be("cached-token");
        second.Should().Be("cached-token");

        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachedExpired_FetchesNewToken()
    {
        var callCount   = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                var token = callCount == 1
                    ? new { access_token = "expired-token",   token_type = "Bearer", expires_in = -1 }
                    : new { access_token = "refreshed-token", token_type = "Bearer", expires_in = 3600 };
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content    = JsonContent.Create(token)
                };
            });

        var sut    = Build(FactoryFor(handlerMock.Object));
        var first  = await sut.GetAccessTokenAsync();
        var second = await sut.GetAccessTokenAsync();

        first.Should().Be("expired-token");
        second.Should().Be("refreshed-token");

        handlerMock.Protected().Verify("SendAsync", Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task GetAccessTokenAsync_TransientGatewayError_RetriesAndSucceeds(HttpStatusCode failCode)
    {
        // Token endpoint returns 502/504 once then recovers — service must retry and cache the token.
        var callCount = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return new HttpResponseMessage { StatusCode = failCode };
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content    = JsonContent.Create(new { access_token = "recovered-token", token_type = "Bearer", expires_in = 3600 })
                };
            });

        var sut = BuildFast(FactoryFor(handlerMock.Object));

        var token = await sut.GetAccessTokenAsync();

        token.Should().Be("recovered-token");
        handlerMock.Protected().Verify("SendAsync", Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task GetAccessTokenAsync_SustainedGatewayError_ThrowsAfterRetries(HttpStatusCode failCode)
    {
        // Token endpoint always returns 502/504 — after 3 retries the exception must propagate.
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = failCode });

        var sut = BuildFast(FactoryFor(handlerMock.Object));

        var act = async () => await sut.GetAccessTokenAsync();

        await act.Should().ThrowAsync<HttpRequestException>();

        // 1 initial + 3 retries = 4 total HTTP calls
        handlerMock.Protected().Verify("SendAsync", Times.Exactly(4),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public void Constructor_NullHttpClientFactory_Throws()
    {
        var act = () => new MyInvoisTokenService(
            null!, Options.Create(_settings), new Mock<ILogger<MyInvoisTokenService>>().Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_NullSettings_Throws()
    {
        var factory = new Mock<IHttpClientFactory>();
        var act = () => new MyInvoisTokenService(
            factory.Object, null!, new Mock<ILogger<MyInvoisTokenService>>().Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }
}
