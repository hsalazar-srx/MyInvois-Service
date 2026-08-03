namespace MyInvois.Service.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;
using Polly;
using Polly.Retry;
using System.Text.Json;
using System.Text.Json.Serialization;

public interface IMyInvoisTokenService
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class MyInvoisTokenService : IMyInvoisTokenService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MyInvoisApiSettings _settings;
    private readonly ILogger<MyInvoisTokenService> _logger;
    private readonly AsyncRetryPolicy _tokenFetchRetryPolicy;

    private CachedToken? _cached;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MyInvoisTokenService(
        IHttpClientFactory httpClientFactory,
        IOptions<MyInvoisApiSettings> settings,
        ILogger<MyInvoisTokenService> logger,
        Func<int, TimeSpan>? retrySleepProvider = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _settings          = settings?.Value    ?? throw new ArgumentNullException(nameof(settings));
        _logger            = logger             ?? throw new ArgumentNullException(nameof(logger));

        var sleepProvider = retrySleepProvider
            ?? (attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt) * 5)); // 5s, 10s, 20s

        _tokenFetchRetryPolicy = Policy
            .Handle<HttpRequestException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: sleepProvider,
                onRetry: (ex, delay, attempt, _) =>
                    _logger.LogWarning(
                        "Token fetch retry {Attempt}/3 after {Delay}s: {Message}",
                        attempt, delay.TotalSeconds, ex.Message));
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cached?.IsValid == true)
        {
            _logger.LogDebug("Using cached OAuth token (expires: {ExpiresAt})", _cached.ExpiresAt);
            return _cached.AccessToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached?.IsValid == true)
                return _cached.AccessToken;

            _logger.LogInformation("Fetching new OAuth token from MyInvois");

            // Per LHDN SDK FAQ: identity service is co-hosted with the API.
            // Pre-prod:   https://preprod-api.myinvois.hasil.gov.my/connect/token
            // Production: https://api.myinvois.hasil.gov.my/connect/token
            var identityBase   = string.IsNullOrEmpty(_settings.IdentityBaseUrl)
                ? _settings.BaseUrl
                : _settings.IdentityBaseUrl;
            var tokenEndpoint  = $"{identityBase}{_settings.TokenEndpoint}";

            var client = _httpClientFactory.CreateClient("MyInvois");

            var response = await _tokenFetchRetryPolicy.ExecuteAsync(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "grant_type",    "client_credentials" },
                        { "client_id",     _settings.ClientId },
                        { "client_secret", _settings.ClientSecret },
                        { "scope",         "InvoicingAPI" }
                    })
                };
                var r = await client.SendAsync(request, cancellationToken);
                r.EnsureSuccessStatusCode(); // throws HttpRequestException on 502/504 → triggers retry
                return r;
            });

            var content   = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = JsonSerializer.Deserialize<OAuthTokenResponse>(content);

            if (tokenData == null || string.IsNullOrWhiteSpace(tokenData.AccessToken))
                throw new InvalidOperationException("OAuth token response is invalid");

            _cached = new CachedToken(tokenData.AccessToken, DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn));

            _logger.LogInformation("OAuth token cached. Expires in {ExpiresIn}s", tokenData.ExpiresIn);

            return _cached.AccessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record CachedToken(string AccessToken, DateTime ExpiresAt)
    {
        public bool IsValid => !string.IsNullOrWhiteSpace(AccessToken) && ExpiresAt > DateTime.UtcNow;
    }

    private sealed class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("token_type")]   public string TokenType   { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")]   public int    ExpiresIn   { get; set; }
    }
}
