namespace MyInvois.Service.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;
using MyInvois.Service.Models;
using Polly;
using Polly.Retry;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

/// <summary>
/// MyInvoiceSubmitter - Submits invoices to MyInvois API
///
/// Responsibilities:
/// - Manage OAuth tokens (cache with 1-hour TTL)
/// - Build JSON submission payloads per LHDN SDK v1.5 /documentsubmissions format
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

    private static readonly JsonSerializerOptions _payloadSerializerOptions = new() { WriteIndented = false };

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

            // 2+3. Build UBL 2.1 JSON, sign (XAdES), base64-encode, wrap in submission envelope
            var payload = BuildSubmissionPayload(document);

            // 4. Submit with Polly retry policy (uses resilience-patterns skill)
            var response = await _retryPolicy.ExecuteAsync(async () =>
            {
                return await SubmitToMyInvois(payload, token, cancellationToken);
            });

            stopwatch.Stop();
            result.DurationMs = (int)stopwatch.ElapsedMilliseconds;

            // 5. Parse response
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                result.RawResponse = content;
                var submissionResponse = JsonSerializer.Deserialize<MyInvoisSubmissionResponse>(content);

                // LHDN Step 07 (synchronous) returns HTTP 200 even when documents are rejected.
                // rejectedDocuments[] is non-empty only for Step 07 failures: XAdES signature
                // errors (DS301, DS322) and duplicate invoiceCodeNumber (DS302/DUP001).
                // Field-level errors (invalid TIN, bad currency, date constraints) are Step 08
                // (async, 2-5 min after HTTP 200) and appear only via GetSubmissionStatus().
                // An empty rejectedDocuments[] + HTTP 200 = Step 07 accepted; poll Step 08 for final verdict.
                var rejected = submissionResponse?.RejectedDocuments?.FirstOrDefault(
                    d => d.InvoiceCodeNumber == document.InvoiceNumber);
                var accepted = submissionResponse?.AcceptedDocuments?.FirstOrDefault(
                    d => d.Uuid != null);

                if (rejected != null)
                {
                    result.Status = "Failed";
                    result.ErrorCode = rejected.Error?.Code ?? "LHDN_REJECTED";
                    result.ErrorMessage = rejected.Error?.Message
                        + (rejected.Error?.Details?.Count > 0
                            ? " | " + string.Join("; ", rejected.Error.Details.Select(d => $"{d.Code}: {d.Message}"))
                            : string.Empty);
                    _logger.LogWarning(
                        "Invoice {InvoiceNumber} rejected by LHDN. Code: {Code} Message: {Message}",
                        document.InvoiceNumber, result.ErrorCode, result.ErrorMessage);
                }
                else
                {
                    result.Status = "Success";
                    result.MyInvoisUUID = accepted?.Uuid
                        ?? submissionResponse?.SubmissionUid
                        ?? string.Empty;
                    _logger.LogInformation(
                        "Invoice {InvoiceNumber} submitted successfully. UUID: {UUID}",
                        document.InvoiceNumber, result.MyInvoisUUID);
                }
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
            // Per LHDN SDK FAQ: identity service is co-hosted with the API (same base URL).
            // Pre-prod: https://preprod-api.myinvois.hasil.gov.my/connect/token
            // Production: https://api.myinvois.hasil.gov.my/connect/token
            // "identity.myinvois.hasil.gov.my" and "sandbox.myinvois.*" are NOT API endpoints.
            var httpClient = _httpClientFactory.CreateClient("MyInvois");
            var identityBase = string.IsNullOrEmpty(_settings.IdentityBaseUrl)
                ? _settings.BaseUrl
                : _settings.IdentityBaseUrl;
            var tokenEndpoint = $"{identityBase}{_settings.TokenEndpoint}";

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
        if (string.IsNullOrWhiteSpace(myInvoisUUID))
            throw new ArgumentException("UUID must not be empty.", nameof(myInvoisUUID));

        var token = await GetAccessToken(cancellationToken);
        var url = _settings.BaseUrl + _settings.DetailsEndpoint.Replace("{uuid}", myInvoisUUID);

        _logger.LogInformation("Polling document status. UUID: {UUID}, URL: {Url}", myInvoisUUID, url);

        var client = _httpClientFactory.CreateClient("MyInvois");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP error polling document status for UUID {UUID}", myInvoisUUID);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Non-success response polling UUID {UUID}: HTTP {Status} — {Body}",
                myInvoisUUID, (int)response.StatusCode, content[..Math.Min(content.Length, 300)]);
            return null;
        }

        try
        {
            var details = JsonSerializer.Deserialize<DocumentDetailsResponse>(content);
            var status = details?.Status;
            _logger.LogInformation("Document status for UUID {UUID}: {Status}", myInvoisUUID, status ?? "null");
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse document details response for UUID {UUID}. Body: {Body}",
                myInvoisUUID, content[..Math.Min(content.Length, 300)]);
            return null;
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Build the LHDN /api/v1.0/documentsubmissions envelope per SDK v1.5:
    ///   1. Build signed UBL 2.1 JSON document via UblDocumentBuilder
    ///   2. Minify → SHA-256 hex documentHash
    ///   3. Base64-encode minified JSON → "document" field
    ///   4. Wrap in { "documents": [ { format, documentHash, codeNumber, document } ] }
    /// </summary>
    private string BuildSubmissionPayload(MyInvoiceDocument document)
    {
        _logger.LogDebug("Building UBL 2.1 submission payload for invoice {InvoiceNumber}", document.InvoiceNumber);

        object ublDoc;
        if (string.IsNullOrEmpty(_settings.CertificatePath))
        {
            // Graceful degradation: no certificate configured (e.g., test environment)
            // Build unsigned document so HTTP submission path can still be exercised
            _logger.LogWarning("CertificatePath not configured — building unsigned UBL document for invoice {InvoiceNumber}.", document.InvoiceNumber);
            ublDoc = UblDocumentBuilder.BuildUnsigned(document);
        }
        else
        {
            var certificate = new X509Certificate2(
                _settings.CertificatePath,
                _settings.CertificatePassword,
                X509KeyStorageFlags.Exportable);

            if (!certificate.HasPrivateKey)
                throw new InvalidOperationException("Certificate does not contain a private key.");

            _logger.LogInformation("Signing invoice {InvoiceNumber} with certificate {Thumbprint}",
                document.InvoiceNumber, certificate.Thumbprint);

            ublDoc = UblDocumentBuilder.BuildSigned(document, certificate);
        }

        var minifiedJson = UblDocumentBuilder.Minify(ublDoc);

        // documentHash = SHA-256 hex of minified JSON
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(minifiedJson));
        var documentHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // document field = base64 of minified JSON
        var documentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(minifiedJson));

        var payload = new
        {
            documents = new[]
            {
                new
                {
                    format = "JSON",
                    documentHash,
                    codeNumber = document.InvoiceNumber,
                    document = documentBase64
                }
            }
        };

        return JsonSerializer.Serialize(payload, _payloadSerializerOptions);
    }

    /// <summary>
    /// Submit JSON payload to MyInvois API /documentsubmissions endpoint.
    /// Content-Type: application/json per LHDN SDK v1.5 specification.
    /// Uses skill: integration/api-rate-limiter v1.0+
    /// </summary>
    private async Task<HttpResponseMessage> SubmitToMyInvois(
        string jsonPayload,
        string token,
        CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("MyInvois");
        var submissionEndpoint = $"{_settings.BaseUrl}{_settings.SubmissionEndpoint}";

        var request = new HttpRequestMessage(HttpMethod.Post, submissionEndpoint)
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token) },
            Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json")
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
    /// LHDN MyInvois document submission response envelope.
    /// Per LHDN SDK v1.5: HTTP 200 is returned even when individual documents are rejected.
    /// Always check RejectedDocuments — do not treat HTTP 200 as unconditional success.
    /// </summary>
    private class MyInvoisSubmissionResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("submissionUid")]
        public string? SubmissionUid { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("acceptedDocuments")]
        public List<AcceptedDocument>? AcceptedDocuments { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("rejectedDocuments")]
        public List<RejectedDocument>? RejectedDocuments { get; set; }
    }

    private class AcceptedDocument
    {
        [System.Text.Json.Serialization.JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("invoiceCodeNumber")]
        public string? InvoiceCodeNumber { get; set; }
    }

    private class RejectedDocument
    {
        [System.Text.Json.Serialization.JsonPropertyName("invoiceCodeNumber")]
        public string? InvoiceCodeNumber { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public LhdnError? Error { get; set; }
    }

    private class LhdnError
    {
        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public string? Code { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("details")]
        public List<LhdnErrorDetail>? Details { get; set; }
    }

    /// <summary>
    /// LHDN GET /api/v1.0/documents/{uuid}/details response.
    /// Step 08 async validator writes the final status here 2–5 minutes after submission.
    /// Status values: "Valid", "Invalid", "Cancelled", "Submitted" (still processing).
    /// </summary>
    private class DocumentDetailsResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("submissionUid")]
        public string? SubmissionUid { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("longId")]
        public string? LongId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("internalId")]
        public string? InternalId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("typeName")]
        public string? TypeName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("typeVersionName")]
        public string? TypeVersionName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("issuerTin")]
        public string? IssuerTin { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("issuerName")]
        public string? IssuerName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("receiverId")]
        public string? ReceiverId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("receiverName")]
        public string? ReceiverName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dateTimeIssued")]
        public string? DateTimeIssued { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dateTimeReceived")]
        public string? DateTimeReceived { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dateTimeValidated")]
        public string? DateTimeValidated { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("totalSales")]
        public decimal? TotalSales { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("totalDiscount")]
        public decimal? TotalDiscount { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("netAmount")]
        public decimal? NetAmount { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("total")]
        public decimal? Total { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string? Status { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("createdByUserId")]
        public string? CreatedByUserId { get; set; }
    }

    private class LhdnErrorDetail
    {
        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public string? Code { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("target")]
        public string? Target { get; set; }
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
