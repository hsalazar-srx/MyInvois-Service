namespace MyInvois.Service.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyInvois.Service.Data;
using MyInvois.Service.Models;
using System.Text.Json;

/// <summary>
/// AuditLogger - Writes submission audit trail to SQLite via EF Core 8.
///
/// ADR-014: Replaces SQL Server + ADO.NET implementation (Phase 1) with
/// SQLite + EF Core (Phase 2). IAuditLogger interface is unchanged.
/// Thread-safe: IDbContextFactory creates one DbContext per operation.
///
/// Skills:
/// - architecture/audit-logging-framework v1.0+ (audit schema + retention)
/// - architecture/configuration-management v1.0+ (connection settings)
/// </summary>
public interface IAuditLogger
{
    /// <summary>Log a submission attempt (success or failure).</summary>
    Task LogSubmission(SubmissionResult result, MyInvoiceDocument? document = null, CancellationToken cancellationToken = default);

    /// <summary>Query failed submissions for retry processing.</summary>
    Task<List<SubmissionResult>> GetFailedSubmissions(int maxResults = 100, CancellationToken cancellationToken = default);

    /// <summary>Check if an invoice was already submitted successfully (duplicate detection).</summary>
    Task<bool> IsInvoiceAlreadySubmitted(string invoiceNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the audit record with the LHDN Step 8 async validation outcome.
    /// Called after polling GetDocumentDetails ~5 minutes after batch submission.
    /// "Valid"    → leave Status="Success", update MyInvoisStatus="Valid"
    /// "Invalid"  → set Status="Failed", MyInvoisStatus="Invalid" — allows retry after user fixes invoice
    /// other      → update MyInvoisStatus only, leave Status unchanged
    /// </summary>
    Task UpdateStep8Status(string invoiceNumber, string uuid, string lhdnStatus, string? errorDetail, CancellationToken cancellationToken = default);
}

public class AuditLogger : IAuditLogger
{
    // ADR-014: SQLite via EF Core 8 — ISO 27001 compliant, immutable append-only log
    // IDbContextFactory ensures one DbContext per operation (thread-safe)

    private readonly IDbContextFactory<AuditDbContext> _contextFactory;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(IDbContextFactory<AuditDbContext> contextFactory, ILogger<AuditLogger> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LogSubmission(SubmissionResult result, MyInvoiceDocument? document = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Logging submission for invoice {InvoiceNumber}, Status: {Status}",
            result.InvoiceNumber, result.Status);

        try
        {
            using var ctx = _contextFactory.CreateDbContext();
            ctx.AuditLogs.Add(new AuditLogEntity
            {
                // Standard fields
                AuditId   = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow.ToString("O"),

                // What
                Action   = "MyInvois_Submit",
                Category = "Integration",
                Severity = result.IsSuccess ? "Info" : "Error",

                // Where
                ResourceType = "Invoice",
                ResourceId   = result.InvoiceNumber,
                Endpoint     = "POST /api/v1.0/documentsubmissions",

                // Result
                Status       = result.Status,
                StatusCode   = result.HttpStatusCode?.ToString() ?? result.ErrorCode,
                ErrorMessage = result.ErrorMessage,
                ResponsePayload = result.RawResponse,

                // Payload (no PII — invoice number, amounts, currency only)
                RequestPayload = JsonSerializer.Serialize(new
                {
                    result.InvoiceNumber,
                    TotalAmount  = document?.TotalInclTax,
                    CurrencyCode = document?.CurrencyCode
                }),

                // Metadata
                CorrelationId = result.SubmissionId,
                Duration      = result.DurationMs > 0 ? (int?)result.DurationMs : null,
                RetryCount    = result.RetryCount,

                // MyInvois-specific
                MyInvoisUUID         = result.MyInvoisUUID,
                MyInvoisStatus       = result.MyInvoisStatus,
                MyInvoisSubmissionId = result.SubmissionReference,
                InvoiceNumber        = result.InvoiceNumber,
                InvoiceDate          = document?.IssueDate,
                InvoiceType          = MapInvoiceType(document?.DocumentTypeCode),
                TotalAmount          = document?.TotalInclTax,
                TotalTax             = document?.TotalTax,
                CurrencyCode         = document?.CurrencyCode,
                ExchangeRate         = document?.ExchangeRate > 0 ? document.ExchangeRate : null,
            });

            await ctx.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Audit log entry created for invoice {InvoiceNumber}",
                result.InvoiceNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write audit log for invoice {InvoiceNumber}",
                result.InvoiceNumber);
            throw;
        }
    }

    public async Task<bool> IsInvoiceAlreadySubmitted(string invoiceNumber,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Checking duplicate for invoice {InvoiceNumber}", invoiceNumber);

        try
        {
            using var ctx = _contextFactory.CreateDbContext();
            return await ctx.AuditLogs.AnyAsync(x =>
                x.InvoiceNumber == invoiceNumber &&
                x.Status == "Success", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to check duplicate for invoice {InvoiceNumber}", invoiceNumber);
            throw;
        }
    }

    public async Task<List<SubmissionResult>> GetFailedSubmissions(int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying failed submissions (max: {MaxResults})", maxResults);

        try
        {
            using var ctx = _contextFactory.CreateDbContext();

            // Materialize first — DateTime.TryParse cannot be translated to SQL
            var entities = await ctx.AuditLogs
                .Where(x => x.Status == "Failed" && x.Category == "Integration")
                .OrderByDescending(x => x.Timestamp)
                .Take(maxResults)
                .ToListAsync(cancellationToken);

            return entities.Select(x => new SubmissionResult
            {
                InvoiceNumber = x.InvoiceNumber ?? string.Empty,
                Status        = x.Status,
                ErrorCode     = x.StatusCode,
                ErrorMessage  = x.ErrorMessage,
                SubmittedAt   = DateTime.TryParse(x.Timestamp, out var ts) ? ts : DateTime.MinValue,
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query failed submissions");
            throw;
        }
    }

    public async Task UpdateStep8Status(string invoiceNumber, string uuid, string lhdnStatus, string? errorDetail, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Updating Step 8 status for invoice {InvoiceNumber} (UUID: {UUID}): {LhdnStatus}",
            invoiceNumber, uuid, lhdnStatus);

        try
        {
            using var ctx = _contextFactory.CreateDbContext();

            var entity = await ctx.AuditLogs
                .Where(x => x.MyInvoisUUID == uuid && x.InvoiceNumber == invoiceNumber)
                .OrderByDescending(x => x.Timestamp)
                .FirstOrDefaultAsync(cancellationToken);

            if (entity == null)
            {
                _logger.LogWarning(
                    "No audit record found for invoice {InvoiceNumber} UUID {UUID} — Step 8 status not recorded.",
                    invoiceNumber, uuid);
                return;
            }

            entity.MyInvoisStatus = lhdnStatus;

            if (lhdnStatus == "Invalid")
            {
                entity.Status       = "Failed";
                entity.ErrorMessage = errorDetail ?? "LHDN Step 8 async validation rejected the document.";
                entity.Severity     = "Error";
            }

            await ctx.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Step 8 status updated for invoice {InvoiceNumber}: MyInvoisStatus={LhdnStatus}, Status={Status}",
                invoiceNumber, lhdnStatus, entity.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update Step 8 status for invoice {InvoiceNumber} UUID {UUID}",
                invoiceNumber, uuid);
            throw;
        }
    }

    private static string? MapInvoiceType(string? documentTypeCode) => documentTypeCode switch
    {
        "01" => "Sales",
        "02" => "Sales Credit Note",
        "11" => "Purchase",
        "12" => "Purchase Credit Note",
        _    => documentTypeCode
    };
}
