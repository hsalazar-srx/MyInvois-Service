namespace MyInvois.Service.Services;

using Microsoft.Extensions.Logging;
using MyInvois.Service.Models;
using System.Diagnostics;

/// <summary>
/// InvoiceProcessor - Main orchestrator for batch processing
/// Coordinates MOVEX reading, transformation, and MyInvois submission
///
/// Responsibilities:
/// - Fetch pending invoices from MOVEX database (DB2 on AS/400) via IMovexInvoiceReader
/// - Coordinate transformation to MyInvois schema
/// - Manage batch submission workflow
/// - Handle retries and failure recovery
/// - Log all operations to SQL Server audit log
///
/// Skills:
/// - architecture/clean-architecture (orchestration boundaries)
/// - architecture/resilience-patterns (retry policy hooks)
/// - integration/api-rate-limiter (batch delay enforcement)
/// </summary>
public interface IInvoiceProcessor
{
    /// <summary>
    /// Process daily batch (if enabled in configuration)
    /// Fetches all pending sales and purchase invoices
    /// </summary>
    Task<BatchResult> ProcessDailyBatch(CancellationToken cancellationToken = default);

    /// <summary>
    /// Process monthly batch (scheduled for 1st of month)
    /// </summary>
    Task<BatchResult> ProcessMonthlyBatch(CancellationToken cancellationToken = default);

    /// <summary>
    /// Process invoices for a specific date range (for smoke testing and backfill).
    /// </summary>
    Task<BatchResult> ProcessDateRangeBatch(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process a single invoice (on-demand via portal)
    /// </summary>
    Task<SubmissionResult> ProcessSingleInvoice(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retry previously failed submission
    /// </summary>
    Task<SubmissionResult> RetryFailedInvoice(string submissionId, CancellationToken cancellationToken = default);
}

public class InvoiceProcessor : IInvoiceProcessor
{
    // Uses skill: architecture/clean-architecture v1.2+
    // Uses skill: architecture/resilience-patterns v1.8+
    // Uses skill: integration/api-rate-limiter v1.0+
    // Orchestrates: Reader → Mapper → Validator → Submitter → AuditLogger

    private readonly IMovexInvoiceReader _movexReader;
    private readonly IMyInvoisMapper _mapper;
    private readonly IMyInvoiceSubmitter _submitter;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<InvoiceProcessor> _logger;

    public InvoiceProcessor(
        IMovexInvoiceReader movexReader,
        IMyInvoisMapper mapper,
        IMyInvoiceSubmitter submitter,
        IAuditLogger auditLogger,
        ILogger<InvoiceProcessor> logger)
    {
        _movexReader = movexReader ?? throw new ArgumentNullException(nameof(movexReader));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _submitter = submitter ?? throw new ArgumentNullException(nameof(submitter));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<BatchResult> ProcessDailyBatch(CancellationToken cancellationToken = default)
    {
        // Yesterday midnight → today midnight (local time, converted to UTC for DB2 query).
        var today    = DateTime.Today;
        var fromDate = today.AddDays(-1);
        var toDate   = today.AddSeconds(-1); // 23:59:59 yesterday

        _logger.LogInformation(
            "Starting daily batch processing. From: {From:yyyy-MM-dd}, To: {To:yyyy-MM-dd}",
            fromDate, toDate);

        return ProcessDateRangeBatch(fromDate, toDate, cancellationToken);
    }

    public Task<BatchResult> ProcessMonthlyBatch(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var fromDate = new DateTime(now.Year, now.Month, 1);
        var toDate = fromDate.AddMonths(1).AddSeconds(-1);
        return ProcessDateRangeBatch(fromDate, toDate, cancellationToken);
    }

    public async Task<BatchResult> ProcessDateRangeBatch(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var batchId = Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();
        var result = new BatchResult
        {
            BatchId = batchId,
            BatchType = $"DateRange:{fromDate:yyyy-MM-dd}:{toDate:yyyy-MM-dd}",
            StartedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Starting batch processing. BatchId: {BatchId}, From: {From}, To: {To}",
            batchId, fromDate, toDate);

        try
        {
            // 1. Fetch pending invoices from MOVEX DB2
            var invoices = await _movexReader.GetInvoicesByDateRange(fromDate, toDate, cancellationToken);

            result.TotalInvoices = invoices.Count;
            _logger.LogInformation("Fetched {Count} pending invoices from MOVEX. BatchId: {BatchId}",
                invoices.Count, batchId);

            if (invoices.Count == 0)
            {
                result.CompletedAt = DateTime.UtcNow;
                _logger.LogInformation("No pending invoices found. BatchId: {BatchId}", batchId);
                return result;
            }

            // 2. Process each invoice through the full pipeline
            foreach (var invoice in invoices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var submissionResult = await ProcessInvoice(invoice, cancellationToken);
                result.Submissions.Add(submissionResult);

                switch (submissionResult.Status)
                {
                    case "Success":
                        result.SuccessCount++;
                        break;
                    case "Skipped":
                        result.SkippedCount++;
                        break;
                    default:
                        result.FailedCount++;
                        break;
                }
            }

            stopwatch.Stop();
            result.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Batch complete. BatchId: {BatchId}, Total: {Total}, Success: {Success}, Failed: {Failed}, Skipped: {Skipped}, Duration: {Duration}s",
                batchId, result.TotalInvoices, result.SuccessCount, result.FailedCount,
                result.SkippedCount, stopwatch.Elapsed.TotalSeconds);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Batch cancelled. BatchId: {BatchId}", batchId);
            result.ErrorSummary = "Batch processing was cancelled";
            result.CompletedAt = DateTime.UtcNow;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Monthly batch processing failed. BatchId: {BatchId}", batchId);
            result.ErrorSummary = ex.Message;
            result.CompletedAt = DateTime.UtcNow;
            return result;
        }
    }

    public async Task<SubmissionResult> ProcessSingleInvoice(string invoiceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing single invoice: {InvoiceId}", invoiceId);

        try
        {
            // 1. Fetch invoice from MOVEX
            var invoice = await _movexReader.GetInvoiceById(invoiceId, cancellationToken);
            if (invoice == null)
            {
                _logger.LogWarning("Invoice {InvoiceId} not found in MOVEX", invoiceId);
                return new SubmissionResult
                {
                    InvoiceNumber = invoiceId,
                    Status = "Failed",
                    ErrorMessage = $"Invoice {invoiceId} not found in MOVEX database"
                };
            }

            // 2. Check for duplicate
            if (await _auditLogger.IsInvoiceAlreadySubmitted(invoiceId, cancellationToken))
            {
                _logger.LogWarning("Invoice {InvoiceId} already submitted. Skipping.", invoiceId);
                return new SubmissionResult
                {
                    InvoiceNumber = invoiceId,
                    Status = "Failed",
                    ErrorCode = "DUPLICATE",
                    ErrorMessage = $"Invoice {invoiceId} has already been successfully submitted"
                };
            }

            // 3. Process through the full pipeline
            return await ProcessInvoice(invoice, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing single invoice {InvoiceId}", invoiceId);
            return new SubmissionResult
            {
                InvoiceNumber = invoiceId,
                Status = "Failed",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<SubmissionResult> RetryFailedInvoice(string submissionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrying failed submission: {SubmissionId}", submissionId);
        // TODO: Implement retry logic (Phase 2 - requires loading previous submission from audit log)
        throw new NotImplementedException("Retry logic scheduled for Phase 2");
    }

    #region Private Methods

    /// <summary>
    /// Process a single invoice through the full pipeline:
    /// Duplicate check → Transform → Validate → Submit → Audit
    /// </summary>
    private async Task<SubmissionResult> ProcessInvoice(MovexInvoice invoice, CancellationToken cancellationToken)
    {
        var invoiceNumber = invoice.InvoiceNumber;

        try
        {
            // 1. Check for duplicate submission
            if (await _auditLogger.IsInvoiceAlreadySubmitted(invoiceNumber, cancellationToken))
            {
                _logger.LogInformation("Invoice {InvoiceNumber} already submitted. Skipping.", invoiceNumber);
                return new SubmissionResult
                {
                    InvoiceNumber = invoiceNumber,
                    Status = "Skipped",
                    ErrorCode = "DUPLICATE",
                    ErrorMessage = "Invoice already submitted successfully"
                };
            }

            // 2. Transform MOVEX → MyInvois format
            var document = _mapper.Transform(invoice);

            // 3. Validate document (non-fail-fast: collects ALL errors)
            if (!_mapper.ValidateDocument(document, out var validationErrors))
            {
                _logger.LogWarning(
                    "Invoice {InvoiceNumber} validation failed with {ErrorCount} errors",
                    invoiceNumber, validationErrors.Count);

                var failedResult = new SubmissionResult
                {
                    InvoiceNumber = invoiceNumber,
                    Status = "Failed",
                    ErrorCode = "VALIDATION",
                    ErrorMessage = $"Validation failed: {string.Join("; ", validationErrors.Select(e => e.Message))}"
                };

                // Log validation failure to audit trail
                await _auditLogger.LogSubmission(failedResult, document, cancellationToken);
                return failedResult;
            }

            // 4. Submit to MyInvois API
            var submissionResult = await _submitter.Submit(document, cancellationToken);
            submissionResult.InvoiceNumber = invoiceNumber;

            // 5. Log to audit trail (success or API failure)
            await _auditLogger.LogSubmission(submissionResult, document, cancellationToken);

            _logger.LogInformation(
                "Invoice {InvoiceNumber} processed: {Status} (UUID: {UUID})",
                invoiceNumber, submissionResult.Status, submissionResult.MyInvoisUUID ?? "N/A");

            return submissionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing invoice {InvoiceNumber}", invoiceNumber);

            var errorResult = new SubmissionResult
            {
                InvoiceNumber = invoiceNumber,
                Status = "Failed",
                ErrorMessage = ex.Message
            };

            // Best-effort audit logging for unexpected errors
            try
            {
                await _auditLogger.LogSubmission(errorResult, null, cancellationToken);
            }
            catch (Exception auditEx)
            {
                _logger.LogError(auditEx, "Failed to log audit entry for invoice {InvoiceNumber}", invoiceNumber);
            }

            return errorResult;
        }
    }

    #endregion
}
