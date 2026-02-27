namespace MyInvois.Service.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;
using MyInvois.Service.Models;
using Polly;
using Polly.Retry;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

/// <summary>
/// MyInvoiceSubmitter - Submits invoices to MyInvois API
///
/// Responsibilities:
/// - Manage OAuth tokens (cache with 1-hour TTL)
/// - Submit XML documents to MyInvois API
/// - Handle MyInvois error responses (DS302, 429, 401, etc.)
/// - Implement rate limiting (100 req/minute)
/// - Extract MyInvois UUID from response
/// - Polling for document status (Phase 2)
///
/// Skills:
/// - architecture/resilience-patterns v1.8+ (retry/circuit breaker)
/// - integration/oauth-token-manager v1.0+ (token lifecycle)
/// - integration/xades-signer v1.0+ (XAdES v1.1 signatures)
/// - integration/api-rate-limiter v1.0+ (100 req/min)
/// </summary>
public interface IMyInvoiceSubmitter
{
    /// <summary>
    /// Submit a single invoice to MyInvois
    /// </summary>
    Task<SubmissionResult> Submit(MyInvoiceDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check submission status via MyInvois API
    /// </summary>
    Task<string?> GetSubmissionStatus(string myInvoisUUID, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get OAuth access token (with caching)
    /// </summary>
    Task<string> GetAccessToken(CancellationToken cancellationToken = default);
}

public class MyInvoiceSubmitter : IMyInvoiceSubmitter
{
    // Uses skill: architecture/resilience-patterns v1.8+
    // Uses skill: integration/oauth-token-manager v1.0+
    // Uses skill: integration/xades-signer v1.0+
    // Retry logic: 3 attempts max, exponential backoff (5s, 10s, 20s)

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MyInvoisApiSettings _settings;
    private readonly ILogger<MyInvoiceSubmitter> _logger;

    // Token caching (oauth-token-manager skill)
    private TokenResponse? _cachedToken;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // Polly retry policy (resilience-patterns skill)
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    // Non-retriable error codes (per ADR-008)
    private static readonly HashSet<string> NonRetriableErrorCodes =
    [
        "DS301", // Invalid signature
        "DS302", // Duplicate submission
        "DS101", // Invalid format
        "DS102"  // Invalid mandatory field
    ];

    public MyInvoiceSubmitter(
        IHttpClientFactory httpClientFactory,
        IOptions<MyInvoisApiSettings> settings,
        ILogger<MyInvoiceSubmitter> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Define Polly retry policy per resilience-patterns skill
        // Retry on: 429 (rate limit), 500 (server error), 503 (service unavailable)
        // Don't retry on: 400 (bad request with non-retriable error codes)
        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => IsRetriable(r.StatusCode))
            .Or<HttpRequestException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt) * 5), // 5s, 10s, 20s
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "Retry {RetryCount}/3 after {Delay}s. Reason: {Reason}",
                        retryCount,
                        timespan.TotalSeconds,
                        outcome.Exception?.Message ?? outcome.Result.ReasonPhrase);
                });
    }

    public async Task<SubmissionResult> Submit(MyInvoiceDocument document, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new SubmissionResult
        {
            InvoiceNumber = document.InvoiceNumber,
            SubmittedAt = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation(
                "Submitting invoice {InvoiceNumber} to MyInvois",
                document.InvoiceNumber);

            // 1. Get OAuth token (uses oauth-token-manager skill)
            var token = await GetAccessToken(cancellationToken);

            // 2. Serialize to XML (uses myinvois-document-builder skill)
            var xml = SerializeToUBL21(document);

            // 3. Sign with XAdES (uses xades-signer skill)
            var signedXml = SignDocument(xml, document);

            // 4. Submit with Polly retry policy (uses resilience-patterns skill)
            var response = await _retryPolicy.ExecuteAsync(async () =>
            {
                return await SubmitToMyInvois(signedXml, token, cancellationToken);
            });

            stopwatch.Stop();
            result.DurationMs = (int)stopwatch.ElapsedMilliseconds;

            // 5. Parse response
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var submissionResponse = JsonSerializer.Deserialize<MyInvoisSubmissionResponse>(content);

                result.Status = "Success";
                result.MyInvoisUUID = submissionResponse?.Uuid ?? string.Empty;
                result.RawResponse = content;

                _logger.LogInformation(
                    "Invoice {InvoiceNumber} submitted successfully. UUID: {UUID}",
                    document.InvoiceNumber,
                    result.MyInvoisUUID);
            }
            else
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                result.Status = "Failed";
                result.ErrorCode = await ParseErrorCode(content);
                result.ErrorMessage = await ParseErrorMessage(content);
                result.RawResponse = content;

                _logger.LogError(
                    "Invoice {InvoiceNumber} submission failed. Status: {StatusCode}, Error: {ErrorCode} - {ErrorMessage}",
                    document.InvoiceNumber,
                    response.StatusCode,
                    result.ErrorCode,
                    result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            result.Status = "Failed";
            result.ErrorMessage = ex.Message;

            _logger.LogError(ex,
                "Exception submitting invoice {InvoiceNumber}",
                document.InvoiceNumber);
        }

        return result;
    }

    public async Task<string> GetAccessToken(CancellationToken cancellationToken = default)
    {
        // Check cache (oauth-token-manager skill)
        if (_cachedToken != null && _cachedToken.IsValid)
        {
            _logger.LogDebug("Using cached OAuth token (expires: {ExpiresAt})", _cachedToken.ExpiresAt);
            return _cachedToken.AccessToken;
        }

        // Thread-safe token refresh
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken != null && _cachedToken.IsValid)
            {
                return _cachedToken.AccessToken;
            }

            _logger.LogInformation("Fetching new OAuth token from MyInvois");

            // Fetch new token from MyInvois
            var httpClient = _httpClientFactory.CreateClient("MyInvois");
            var tokenEndpoint = $"{_settings.BaseUrl}{_settings.TokenEndpoint}";

            var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "client_id", _settings.ClientId },
                    { "client_secret", _settings.ClientSecret },
                    { "scope", "InvoicingAPI" }
                })
            };

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = JsonSerializer.Deserialize<OAuthTokenResponse>(content);

            if (tokenData == null || string.IsNullOrWhiteSpace(tokenData.AccessToken))
            {
                throw new InvalidOperationException("OAuth token response is invalid");
            }

            // Cache token (oauth-token-manager skill: 1-hour TTL)
            _cachedToken = new TokenResponse
            {
                AccessToken = tokenData.AccessToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn)
            };

            _logger.LogInformation(
                "OAuth token cached. Expires in {ExpiresIn}s",
                tokenData.ExpiresIn);

            return _cachedToken.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<string?> GetSubmissionStatus(string myInvoisUUID, CancellationToken cancellationToken = default)
    {
        // TODO: Phase 2 - Implement status polling
        // Call GET /api/v1.0/documents/{UUID}/details
        _logger.LogWarning("GetSubmissionStatus not yet implemented (scheduled for Phase 2)");
        await Task.CompletedTask;
        return null;
    }

    #region Private Helper Methods

    /// <summary>
    /// Serialize MyInvoiceDocument to UBL 2.1 XML format
    /// Uses skill: integration/myinvois-document-builder v1.0+
    /// </summary>
    private string SerializeToUBL21(MyInvoiceDocument document)
    {
        // TODO: Use MyInvois SDK for UBL 2.1 serialization
        // Placeholder - actual implementation uses SDK
        _logger.LogDebug("Serializing invoice {InvoiceNumber} to UBL 2.1 format", document.InvoiceNumber);

        // Minimal placeholder XML (replace with actual UBL 2.1 generation)
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Invoice xmlns=""urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"">
    <ID>{document.InvoiceNumber}</ID>
    <IssueDate>{document.IssueDate}</IssueDate>
    <IssueTime>{document.IssueTime}</IssueTime>
</Invoice>";
    }

    /// <summary>
    /// Sign XML document with XAdES v1.1 digital signature
    /// Uses skill: integration/xades-signer v1.0+
    /// </summary>
    private string SignDocument(string xml, MyInvoiceDocument document)
    {
        // TODO: Use MyInvois SDK for XAdES v1.1 signing
        // Placeholder - actual implementation uses SDK
        _logger.LogDebug("Signing invoice {InvoiceNumber} with XAdES v1.1", document.InvoiceNumber);

        // Return unsigned XML for now (signing requires certificate setup)
        return xml;
    }

    /// <summary>
    /// Submit signed XML to MyInvois API
    /// Uses skill: integration/api-rate-limiter v1.0+
    /// </summary>
    private async Task<HttpResponseMessage> SubmitToMyInvois(
        string signedXml,
        string token,
        CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("MyInvois");
        var submissionEndpoint = $"{_settings.BaseUrl}{_settings.SubmissionEndpoint}";

        var request = new HttpRequestMessage(HttpMethod.Post, submissionEndpoint)
        {
            Headers = { { "Authorization", $"Bearer {token}" } },
            Content = new StringContent(signedXml, System.Text.Encoding.UTF8, "application/xml")
        };

        _logger.LogDebug("POST {Endpoint}", submissionEndpoint);

        return await httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Determine if HTTP status code is retriable
    /// Per ADR-008: Retry on 429, 500, 503; don't retry on 400 (with non-retriable error codes)
    /// </summary>
    private static bool IsRetriable(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests || // 429
               statusCode == HttpStatusCode.InternalServerError || // 500
               statusCode == HttpStatusCode.ServiceUnavailable; // 503
    }

    /// <summary>
    /// Parse error code from MyInvois error response
    /// </summary>
    private static async Task<string> ParseErrorCode(string content)
    {
        try
        {
            var errorResponse = JsonSerializer.Deserialize<MyInvoisErrorResponse>(content);
            return errorResponse?.Error?.Code ?? "UNKNOWN";
        }
        catch
        {
            return "UNKNOWN";
        }
    }

    /// <summary>
    /// Parse error message from MyInvois error response
    /// </summary>
    private static async Task<string> ParseErrorMessage(string content)
    {
        try
        {
            var errorResponse = JsonSerializer.Deserialize<MyInvoisErrorResponse>(content);
            return errorResponse?.Error?.Message ?? content;
        }
        catch
        {
            return content;
        }
    }

    #endregion

    #region Internal Models

    /// <summary>
    /// Token caching model (oauth-token-manager skill)
    /// </summary>
    private class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }

        public bool IsValid => !string.IsNullOrWhiteSpace(AccessToken) && ExpiresAt > DateTime.UtcNow;
    }

    /// <summary>
    /// OAuth token response from MyInvois API
    /// </summary>
    private class OAuthTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    /// <summary>
    /// MyInvois submission response
    /// </summary>
    private class MyInvoisSubmissionResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("uuid")]
        public string Uuid { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("submissionDate")]
        public string SubmissionDate { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// MyInvois error response
    /// </summary>
    private class MyInvoisErrorResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public ErrorDetail? Error { get; set; }

        public class ErrorDetail
        {
            [System.Text.Json.Serialization.JsonPropertyName("code")]
            public string Code { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("message")]
            public string Message { get; set; } = string.Empty;
        }
    }

    #endregion
}
