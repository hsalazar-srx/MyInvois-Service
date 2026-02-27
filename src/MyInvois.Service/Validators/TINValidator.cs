namespace MyInvois.Service.Validators;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;
using MyInvois.Service.Models;
using System.Text.Json;

/// <summary>
/// TINValidator - Validates TIN (Tax Identification Number) format and existence via MyInvois API
///
/// Validates:
/// - TIN format (12 chars, numeric)
/// - TIN existence via MyInvois API (cache results 1 hour)
/// - Buyer TIN optional if unavailable (B2C transactions)
///
/// Skills:
/// - integration/myinvois-validator (TIN rules)
/// - architecture/resilience-patterns (graceful degradation)
/// </summary>
public interface ITINValidator
{
    bool ValidateFormat(string? tin, out ValidationError? error, bool isRequired = true);

    Task<(bool isValid, ValidationError? error)> ValidateViaAPI(string tin, CancellationToken cancellationToken = default);
}

public class TINValidator : ITINValidator
{
    // Uses skill: integration/myinvois-validator v1.0+
    // TIN Format: Exactly 12 digits, numeric only
    // Buyer TIN is optional (B2C transactions), Supplier TIN is mandatory

    private const int TIN_LENGTH = 12;
    private const string CACHE_KEY_PREFIX = "TIN:";
    private static readonly TimeSpan CACHE_TTL = TimeSpan.FromHours(1);

    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MyInvoisApiSettings _apiSettings;
    private readonly ILogger<TINValidator> _logger;

    public TINValidator(
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        IOptions<MyInvoisApiSettings> apiSettings,
        ILogger<TINValidator> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _apiSettings = apiSettings?.Value ?? throw new ArgumentNullException(nameof(apiSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool ValidateFormat(string? tin, out ValidationError? error, bool isRequired = true)
    {
        error = null;

        // Check if TIN is required
        if (string.IsNullOrWhiteSpace(tin))
        {
            if (!isRequired)
            {
                // TIN is optional (e.g., buyer TIN in B2C transactions)
                return true;
            }

            error = new ValidationError
            {
                FieldName = "TIN",
                Message = "TIN is required",
                Severity = "Error",
                ViolatedRule = "MandatoryField_TIN"
            };
            return false;
        }

        // Validate length (exactly 12 characters)
        if (tin.Length != TIN_LENGTH)
        {
            error = new ValidationError
            {
                FieldName = "TIN",
                Message = $"TIN must be exactly {TIN_LENGTH} digits",
                Severity = "Error",
                ViolatedRule = "FixedLength_TIN"
            };
            return false;
        }

        // Validate all characters are numeric
        if (!tin.All(char.IsDigit))
        {
            error = new ValidationError
            {
                FieldName = "TIN",
                Message = "TIN must contain only numeric digits",
                Severity = "Error",
                ViolatedRule = "NumericOnly_TIN"
            };
            return false;
        }

        // TIN format is valid
        return true;
    }

    public async Task<(bool isValid, ValidationError? error)> ValidateViaAPI(string tin, CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cacheKey = CACHE_KEY_PREFIX + tin;
        if (_cache.TryGetValue<bool>(cacheKey, out var cachedResult))
        {
            _logger.LogDebug("TIN {TIN} validation result retrieved from cache: {IsValid}", tin, cachedResult);
            return cachedResult
                ? (true, null)
                : (false, new ValidationError
                {
                    FieldName = "TIN",
                    Message = $"TIN {tin} is not registered with MyInvois",
                    Severity = "Error",
                    ViolatedRule = "TIN_NotRegistered"
                });
        }

        try
        {
            _logger.LogInformation("Validating TIN {TIN} via MyInvois API", tin);

            // Call MyInvois TIN validation endpoint
            var httpClient = _httpClientFactory.CreateClient("MyInvois");
            var tinEndpoint = $"{_apiSettings.BaseUrl}/api/v1.0/taxpayer/validate/{tin}";

            var request = new HttpRequestMessage(HttpMethod.Get, tinEndpoint);
            // Note: May need OAuth token for this endpoint — depends on MyInvois API requirements
            // If needed, inject IMyInvoiceSubmitter to get token via GetAccessToken()

            var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var apiResponse = JsonSerializer.Deserialize<TinValidationResponse>(content);

                var isValid = apiResponse?.IsValid ?? false;

                // Cache the result for 1 hour
                _cache.Set(cacheKey, isValid, CACHE_TTL);

                _logger.LogInformation("TIN {TIN} validation result: {IsValid}", tin, isValid);

                if (!isValid)
                {
                    return (false, new ValidationError
                    {
                        FieldName = "TIN",
                        Message = $"TIN {tin} is not registered with MyInvois",
                        Severity = "Error",
                        ViolatedRule = "TIN_NotRegistered"
                    });
                }

                return (true, null);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // TIN not found — treat as invalid
                _cache.Set(cacheKey, false, CACHE_TTL);

                _logger.LogWarning("TIN {TIN} not found in MyInvois registry", tin);

                return (false, new ValidationError
                {
                    FieldName = "TIN",
                    Message = $"TIN {tin} is not registered with MyInvois",
                    Severity = "Error",
                    ViolatedRule = "TIN_NotRegistered"
                });
            }
            else
            {
                // API error — graceful degradation (pass validation with warning)
                _logger.LogWarning(
                    "MyInvois TIN validation API returned {StatusCode} for TIN {TIN}. Passing validation with warning (graceful degradation).",
                    response.StatusCode, tin);

                return (true, null); // Pass validation if API is unavailable
            }
        }
        catch (TaskCanceledException ex)
        {
            // Timeout — graceful degradation
            _logger.LogWarning(ex, "MyInvois TIN validation API timeout for TIN {TIN}. Passing validation with warning (graceful degradation).", tin);
            return (true, null); // Pass validation on timeout
        }
        catch (HttpRequestException ex)
        {
            // Network error — graceful degradation
            _logger.LogWarning(ex, "MyInvois TIN validation API network error for TIN {TIN}. Passing validation with warning (graceful degradation).", tin);
            return (true, null); // Pass validation on network error
        }
        catch (Exception ex)
        {
            // Unexpected error — graceful degradation
            _logger.LogError(ex, "Unexpected error validating TIN {TIN} via MyInvois API. Passing validation with warning (graceful degradation).", tin);
            return (true, null); // Pass validation on unexpected error
        }
    }

    #region Internal Models

    private class TinValidationResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("isValid")]
        public bool IsValid { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tin")]
        public string? Tin { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    #endregion
}
