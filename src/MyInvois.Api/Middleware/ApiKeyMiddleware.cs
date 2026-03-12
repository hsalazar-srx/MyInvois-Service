using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace MyInvois.Api.Middleware;

// Uses skill: security/api-key-authentication v1.0
// Pattern sourced from: Reporting-Service/src/Reporting.Api/Middleware/ApiKeyMiddleware.cs

/// <summary>
/// Validates the X-API-Key header for all requests except /api/v1/health.
/// Two-tier: Primary key for normal clients (SM-Portal); Admin key for operations access.
/// Uses CryptographicOperations.FixedTimeEquals to prevent timing attacks.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private const string ApiKeyHeader   = "X-API-Key";
    private const string AdminKeyHeader = "X-Admin-Key";

    private readonly RequestDelegate              _next;
    private readonly ILogger<ApiKeyMiddleware>    _logger;
    private readonly IOptionsMonitor<ApiKeyOptions> _optionsMonitor;

    public ApiKeyMiddleware(
        RequestDelegate next,
        IOptionsMonitor<ApiKeyOptions> optionsMonitor,
        ILogger<ApiKeyMiddleware> logger)
    {
        _next           = next;
        _optionsMonitor = optionsMonitor;
        _logger         = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Health probe — no auth required
        if (context.Request.Path.StartsWithSegments("/api/v1/health"))
        {
            await _next(context);
            return;
        }

        var options = _optionsMonitor.CurrentValue;

        // Admin key check
        if (context.Request.Headers.TryGetValue(AdminKeyHeader, out var adminHeaderValue) &&
            !string.IsNullOrWhiteSpace(options.Admin) &&
            IsKeyValid(adminHeaderValue.ToString(), options.Admin))
        {
            _logger.LogInformation("Admin key access granted for {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await _next(context);
            return;
        }

        // Primary key not configured — service misconfigured
        if (string.IsNullOrWhiteSpace(options.Primary))
        {
            _logger.LogWarning("ApiKeys:Primary not configured; denying request to {Path}",
                context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                code          = "SERVICE_UNAVAILABLE",
                message       = "Service is not configured to accept requests.",
                correlationId = context.TraceIdentifier,
                timestamp     = DateTime.UtcNow
            });
            return;
        }

        // Primary key validation
        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey) ||
            !IsKeyValid(providedKey.ToString(), options.Primary))
        {
            _logger.LogWarning("Invalid or missing API key for {Method} {Path}",
                context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                code          = "UNAUTHORIZED",
                message       = "Invalid or missing API key.",
                correlationId = context.TraceIdentifier,
                timestamp     = DateTime.UtcNow
            });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Constant-time key comparison — prevents timing attacks that could reveal the key
    /// via response time measurement.
    /// </summary>
    private static bool IsKeyValid(string provided, string configured)
    {
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(configured))
            return false;

        var providedBytes   = Encoding.UTF8.GetBytes(provided);
        var configuredBytes = Encoding.UTF8.GetBytes(configured);

        if (providedBytes.Length != configuredBytes.Length)
        {
            // Still compare to maintain constant time — result will always be false
            var maxLen           = Math.Max(providedBytes.Length, configuredBytes.Length);
            var paddedProvided   = new byte[maxLen];
            var paddedConfigured = new byte[maxLen];
            providedBytes.CopyTo(paddedProvided, 0);
            configuredBytes.CopyTo(paddedConfigured, 0);
            CryptographicOperations.FixedTimeEquals(paddedProvided, paddedConfigured);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }
}

/// <summary>
/// API key configuration. Bound from appsettings "ApiKeys" section.
/// Actual values come from User Secrets (dev) or Azure Key Vault (prod).
/// </summary>
public sealed class ApiKeyOptions
{
    /// <summary>Primary key — used by SM-Portal and future internal callers.</summary>
    public string? Primary { get; set; }

    /// <summary>Admin key — elevated access for operations/monitoring tools.</summary>
    public string? Admin { get; set; }
}
