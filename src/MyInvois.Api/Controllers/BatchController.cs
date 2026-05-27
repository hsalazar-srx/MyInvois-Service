using Microsoft.AspNetCore.Mvc;
using MyInvois.Api.Models;
using MyInvois.Service.Services;

namespace MyInvois.Api.Controllers;

/// <summary>
/// Batch submission endpoint — triggers InvoiceProcessor.ProcessDateRangeBatch()
/// over an HTTP surface for scheduled jobs, smoke tests, and backfill operations.
/// Authentication is handled by ApiKeyMiddleware (X-API-Key header) before this controller runs.
/// </summary>
[ApiController]
[Route("api/v1/batch")]
public class BatchController(
    IInvoiceProcessor processor,
    ILogger<BatchController> logger) : ControllerBase
{
    /// <summary>
    /// Submit all invoices in the given date range to LHDN MyInvois.
    /// Long-running — typical daily batch takes 30–120 s depending on invoice count.
    /// </summary>
    /// <param name="request">fromDate and toDate in yyyy-MM-dd format.</param>
    [HttpPost("process-range")]
    public async Task<IActionResult> ProcessRange(
        [FromBody] ProcessRangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParseExact(request.FromDate, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var from))
            return BadRequest(new { error = "fromDate must be in yyyy-MM-dd format." });

        if (!DateOnly.TryParseExact(request.ToDate, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var to))
            return BadRequest(new { error = "toDate must be in yyyy-MM-dd format." });

        if (from > to)
            return BadRequest(new { error = "fromDate must not be after toDate." });

        if ((to.ToDateTime(TimeOnly.MinValue) - from.ToDateTime(TimeOnly.MinValue)).TotalDays > 93)
            return BadRequest(new { error = "Date range must not exceed 93 days." });

        var correlationId = HttpContext.TraceIdentifier;
        logger.LogInformation(
            "[BatchController] ProcessRange from={From} to={To} correlationId={CorrelationId}",
            request.FromDate, request.ToDate, correlationId);

        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue);

        var result = await processor.ProcessDateRangeBatch(fromDt, toDt, cancellationToken);

        return Ok(new BatchProcessResponse
        {
            BatchId         = result.BatchId,
            BatchType       = result.BatchType,
            FromDate        = request.FromDate,
            ToDate          = request.ToDate,
            TotalInvoices   = result.TotalInvoices,
            SuccessCount    = result.SuccessCount,
            FailedCount     = result.FailedCount,
            SkippedCount    = result.SkippedCount,
            SuccessRate     = result.SuccessRate,
            DurationSeconds = result.DurationSeconds,
            ErrorSummary    = result.ErrorSummary,
            StartedAt       = result.StartedAt,
            CompletedAt     = result.CompletedAt,
        });
    }
}
