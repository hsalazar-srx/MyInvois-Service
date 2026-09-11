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

    // Step 8 polling delay — injectable for tests (pass TimeSpan.Zero to skip the wait).
    // Default: 5 minutes, matching LHDN's typical async validation window.
    internal TimeSpan Step8PollingDelay { get; set; } = TimeSpan.FromMinutes(5);

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

            // 2. Pre-flight: verify LHDN is reachable before committing to the full batch.
            // Token service has its own 3-retry policy; if it still fails here, LHDN is down.
            try
            {
                await _submitter.GetAccessToken(cancellationToken);
                _logger.LogInformation("LHDN pre-flight check passed. BatchId: {BatchId}", batchId);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.CompletedAt  = DateTime.UtcNow;
                result.FailedCount  = invoices.Count;
                result.ErrorSummary = "LhdnUnavailable";
                _logger.LogError(ex,
                    "LHDN pre-flight token check failed — aborting batch of {Count} invoices. " +
                    "Retry the batch when LHDN recovers. BatchId: {BatchId}",
                    invoices.Count, batchId);
                return result;
            }

            // 4. Process each invoice through the full pipeline
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

            // 5. Step 8 polling — wait for LHDN async validation then update audit records.
            // Only poll if at least one invoice was accepted by LHDN (UUID assigned).
            var accepted = result.Submissions
                .Where(s => s.Status == "Success" && !string.IsNullOrWhiteSpace(s.MyInvoisUUID))
                .ToList();

            if (accepted.Count > 0)
            {
                await PollStep8Async(accepted, batchId, cancellationToken);
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
    /// Wait for LHDN Step 8 async validation then update audit records.
    /// "Valid"     → MyInvoisStatus updated; Status remains "Success"; duplicate check blocks resubmission.
    /// "Invalid"   → Status set to "Failed"; duplicate check allows retry after user fixes the invoice.
    /// "Submitted" → validation still in progress; log warning and leave record unchanged.
    /// </summary>
    private async Task PollStep8Async(List<SubmissionResult> accepted, string batchId, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Waiting {Delay}s before polling Step 8 status for {Count} accepted invoice(s). BatchId: {BatchId}",
            (int)Step8PollingDelay.TotalSeconds, accepted.Count, batchId);

        await Task.Delay(Step8PollingDelay, cancellationToken);

        _logger.LogInformation("Polling LHDN Step 8 status for {Count} invoice(s). BatchId: {BatchId}",
            accepted.Count, batchId);

        var invalidCount = 0;

        foreach (var submission in accepted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var step8 = await _submitter.GetSubmissionDetails(submission.MyInvoisUUID!, cancellationToken);
                var lhdnStatus = step8.Status;

                if (string.IsNullOrWhiteSpace(lhdnStatus))
                {
                    _logger.LogWarning(
                        "Step 8 poll returned no status for invoice {InvoiceNumber} (UUID: {UUID}). " +
                        "Will remain as Submitted — check LHDN portal manually.",
                        submission.InvoiceNumber, submission.MyInvoisUUID);
                    continue;
                }

                // Record LHDN's own validator output (error code + offending field) rather than a
                // generic "check the portal" message. The fallback text is used only when LHDN
                // returns Invalid without a validationResults block.
                string? errorDetail = lhdnStatus == "Invalid"
                    ? step8.FailureDetail
                      ?? "LHDN Step 8 async validation rejected this document, but returned no " +
                         "validation detail. Retrieve it with scripts/Get-LhdnDocumentDetails.ps1 " +
                         $"-Uuid {submission.MyInvoisUUID}"
                    : null;

                await _auditLogger.UpdateStep8Status(
                    submission.InvoiceNumber, submission.MyInvoisUUID!, lhdnStatus, errorDetail, cancellationToken);

                if (lhdnStatus == "Invalid")
                {
                    invalidCount++;
                    _logger.LogWarning(
                        "Invoice {InvoiceNumber} failed LHDN Step 8 validation (UUID: {UUID}). " +
                        "Status set to Failed — invoice can be resubmitted after fix.",
                        submission.InvoiceNumber, submission.MyInvoisUUID);
                }
                else
                {
                    _logger.LogInformation(
                        "Invoice {InvoiceNumber} Step 8 status: {Status} (UUID: {UUID})",
                        submission.InvoiceNumber, lhdnStatus, submission.MyInvoisUUID);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to poll Step 8 status for invoice {InvoiceNumber} (UUID: {UUID}). " +
                    "Audit record not updated — check LHDN portal manually.",
                    submission.InvoiceNumber, submission.MyInvoisUUID);
            }
        }

        if (invalidCount > 0)
            _logger.LogWarning(
                "Step 8 polling complete. {InvalidCount}/{Total} invoice(s) rejected by LHDN async validation. " +
                "These are flagged as Failed in the audit log and can be resubmitted after fixing. BatchId: {BatchId}",
                invalidCount, accepted.Count, batchId);
        else
            _logger.LogInformation(
                "Step 8 polling complete. All {Count} invoice(s) passed LHDN async validation. BatchId: {BatchId}",
                accepted.Count, batchId);
    }

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
