namespace MyInvois.Service.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;
using MyInvois.Service.Models;
using MyInvois.Service.Models.Lhdn;
using Polly;
using Polly.Retry;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

public interface IMyInvoiceSubmitter
{
    Task<SubmissionResult> Submit(MyInvoiceDocument document, CancellationToken cancellationToken = default);
    Task<string?> GetSubmissionStatus(string myInvoisUUID, CancellationToken cancellationToken = default);

    // Kept on the interface for callers that need it directly (e.g. performance tests warm-up).
    Task<string> GetAccessToken(CancellationToken cancellationToken = default);
}

public class MyInvoiceSubmitter : IMyInvoiceSubmitter
{
    private readonly IHttpClientFactory      _httpClientFactory;
    private readonly MyInvoisApiSettings     _settings;
    private readonly IMyInvoisTokenService   _tokenService;
    private readonly ILogger<MyInvoiceSubmitter> _logger;

    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    private static readonly JsonSerializerOptions _payloadOptions = new() { WriteIndented = false };

    public MyInvoiceSubmitter(
        IHttpClientFactory              httpClientFactory,
        IOptions<MyInvoisApiSettings>   settings,
        IMyInvoisTokenService           tokenService,
        ILogger<MyInvoiceSubmitter>     logger,
        Func<int, TimeSpan>?            retrySleepProvider = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _settings          = settings?.Value   ?? throw new ArgumentNullException(nameof(settings));
        _tokenService      = tokenService      ?? throw new ArgumentNullException(nameof(tokenService));
        _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));

        // Retry on 429 (rate limit), 500, 503. Do not retry on 400 (non-retriable error codes).
        // retrySleepProvider is injectable so tests can pass TimeSpan.Zero to avoid real delays.
        var sleepProvider = retrySleepProvider
            ?? (attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt) * 5)); // 5s, 10s, 20s

        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => IsRetriable(r.StatusCode))
            .Or<HttpRequestException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: sleepProvider,
                onRetry: (outcome, timespan, retryCount, _) =>
                    _logger.LogWarning(
                        "Retry {RetryCount}/3 after {Delay}s. Reason: {Reason}",
                        retryCount,
                        timespan.TotalSeconds,
                        outcome.Exception?.Message ?? outcome.Result.ReasonPhrase));
    }

    public async Task<SubmissionResult> Submit(MyInvoiceDocument document, CancellationToken cancellationToken = default)
    {
        var sw     = Stopwatch.StartNew();
        var result = new SubmissionResult
        {
            InvoiceNumber = document.InvoiceNumber,
            SubmittedAt   = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("Submitting invoice {InvoiceNumber} to MyInvois", document.InvoiceNumber);

            var token   = await _tokenService.GetAccessTokenAsync(cancellationToken);
            var payload = BuildSubmissionPayload(document);

            var response = await _retryPolicy.ExecuteAsync(
                () => SubmitToMyInvois(payload, token, cancellationToken));

            sw.Stop();
            result.DurationMs = (int)sw.ElapsedMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                var content  = await response.Content.ReadAsStringAsync(cancellationToken);
                result.RawResponse = content;
                var parsed   = JsonSerializer.Deserialize<SubmissionResponse>(content);

                var rejected = parsed?.RejectedDocuments?.FirstOrDefault(
                    d => d.InvoiceCodeNumber == document.InvoiceNumber);
                var accepted = parsed?.AcceptedDocuments?.FirstOrDefault(d => d.Uuid != null);

                if (rejected != null)
                {
                    result.Status       = "Failed";
                    result.ErrorCode    = rejected.Error?.Code ?? "LHDN_REJECTED";
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
                    result.Status        = "Success";
                    result.MyInvoisUUID  = accepted?.Uuid ?? parsed?.SubmissionUid ?? string.Empty;

                    _logger.LogInformation(
                        "Invoice {InvoiceNumber} submitted successfully. UUID: {UUID}",
                        document.InvoiceNumber, result.MyInvoisUUID);
                }
            }
            else
            {
                var content      = await response.Content.ReadAsStringAsync(cancellationToken);
                result.Status       = "Failed";
                result.ErrorCode    = ParseErrorCode(content);
                result.ErrorMessage = ParseErrorMessage(content);
                result.RawResponse  = content;

                _logger.LogError(
                    "Invoice {InvoiceNumber} submission failed. Status: {StatusCode}, Error: {ErrorCode} - {ErrorMessage}",
                    document.InvoiceNumber, response.StatusCode, result.ErrorCode, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.DurationMs   = (int)sw.ElapsedMilliseconds;
            result.Status       = "Failed";
            result.ErrorMessage = ex.Message;

            _logger.LogError(ex, "Exception submitting invoice {InvoiceNumber}", document.InvoiceNumber);
        }

        return result;
    }

    // Forwarded to token service; kept on IMyInvoiceSubmitter for backward compatibility.
    public Task<string> GetAccessToken(CancellationToken cancellationToken = default)
        => _tokenService.GetAccessTokenAsync(cancellationToken);

    public async Task<string?> GetSubmissionStatus(string myInvoisUUID, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(myInvoisUUID))
            throw new ArgumentException("UUID must not be empty.", nameof(myInvoisUUID));

        var token = await _tokenService.GetAccessTokenAsync(cancellationToken);
        var url   = _settings.BaseUrl + _settings.DetailsEndpoint.Replace("{uuid}", myInvoisUUID);

        _logger.LogInformation("Polling document status. UUID: {UUID}", myInvoisUUID);

        var client  = _httpClientFactory.CreateClient("MyInvois");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP error polling status for UUID {UUID}", myInvoisUUID);
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
            var status  = details?.Status;
            _logger.LogInformation("Document status for UUID {UUID}: {Status}", myInvoisUUID, status ?? "null");
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse document details for UUID {UUID}. Body: {Body}",
                myInvoisUUID, content[..Math.Min(content.Length, 300)]);
            return null;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Build the LHDN /documentsubmissions envelope per SDK v1.5:
    ///   1. Build signed UBL 2.1 JSON (or unsigned when no cert configured).
    ///   2. Minify → SHA-256 hex documentHash.
    ///   3. Base64-encode minified JSON → "document" field.
    ///   4. Wrap in { "documents": [ { format, documentHash, codeNumber, document } ] }.
    /// </summary>
    private string BuildSubmissionPayload(MyInvoiceDocument document)
    {
        _logger.LogDebug("Building UBL 2.1 submission payload for invoice {InvoiceNumber}", document.InvoiceNumber);

        object ublDoc;
        if (string.IsNullOrEmpty(_settings.CertificatePath))
        {
            _logger.LogWarning(
                "CertificatePath not configured — building unsigned UBL document for invoice {InvoiceNumber}.",
                document.InvoiceNumber);
            ublDoc = UblDocumentBuilder.BuildUnsigned(document);
        }
        else
        {
            // EphemeralKeySet: load private key directly from the .p12 file without
            // persisting to the Windows CNG key store. Required for IIS app pool identities
            // which have no user profile and cannot access per-user key storage.
            var cert = new X509Certificate2(
                _settings.CertificatePath,
                _settings.CertificatePassword,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);

            if (!cert.HasPrivateKey)
                throw new InvalidOperationException("Certificate does not contain a private key.");

            _logger.LogInformation("Signing invoice {InvoiceNumber} with certificate {Thumbprint}",
                document.InvoiceNumber, cert.Thumbprint);

            ublDoc = UblDocumentBuilder.BuildSigned(document, cert);
        }

        var minifiedJson  = UblDocumentBuilder.Minify(ublDoc);
        var hashBytes     = SHA256.HashData(Encoding.UTF8.GetBytes(minifiedJson));
        var documentHash  = Convert.ToHexString(hashBytes).ToLowerInvariant();
        var documentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(minifiedJson));

        var payload = new
        {
            documents = new[]
            {
                new { format = "JSON", documentHash, codeNumber = document.InvoiceNumber, document = documentBase64 }
            }
        };

        return JsonSerializer.Serialize(payload, _payloadOptions);
    }

    private async Task<HttpResponseMessage> SubmitToMyInvois(
        string payload, string token, CancellationToken cancellationToken)
    {
        var client   = _httpClientFactory.CreateClient("MyInvois");
        var endpoint = $"{_settings.BaseUrl}{_settings.SubmissionEndpoint}";

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token) },
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        _logger.LogDebug("POST {Endpoint}", endpoint);
        return await client.SendAsync(request, cancellationToken);
    }

    private static bool IsRetriable(HttpStatusCode code) =>
        code is HttpStatusCode.TooManyRequests
             or HttpStatusCode.InternalServerError
             or HttpStatusCode.ServiceUnavailable;

    private static string ParseErrorCode(string content)
    {
        try { return JsonSerializer.Deserialize<ErrorResponse>(content)?.Error?.Code ?? "UNKNOWN"; }
        catch { return "UNKNOWN"; }
    }

    private static string ParseErrorMessage(string content)
    {
        try { return JsonSerializer.Deserialize<ErrorResponse>(content)?.Error?.Message ?? content; }
        catch { return content; }
    }
}
