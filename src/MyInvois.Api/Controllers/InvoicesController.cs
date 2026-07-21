using Microsoft.AspNetCore.Mvc;
using MyInvois.Api.Models;
using MyInvois.Service.DataAccess;

namespace MyInvois.Api.Controllers;

// Uses skill: architecture/dotnet-api-design v1.0
// Implements ADR-001: HTTP API endpoint for SM-Portal invoice extract.

/// <summary>
/// Invoice extract endpoint — reads AP and AR invoices from MOVEX DB2 via DirectQueryDataSource.
/// Authentication is handled by ApiKeyMiddleware (X-API-Key header) before this controller runs.
/// </summary>
[ApiController]
[Route("api/v1/invoices")]
public class InvoicesController(
    IInvoiceDataSource dataSource,
    ILogger<InvoicesController> logger) : ControllerBase
{
    /// <summary>
    /// Get invoices within a date range, optionally filtered by type.
    /// </summary>
    /// <param name="fromDate">Start date, inclusive (yyyy-MM-dd)</param>
    /// <param name="toDate">End date, inclusive (yyyy-MM-dd)</param>
    /// <param name="type">AP, AR, or ALL (default: ALL)</param>
    [HttpGet]
    public async Task<IActionResult> GetInvoices(
        [FromQuery] string fromDate,
        [FromQuery] string toDate,
        [FromQuery] string type = "ALL",
        CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParseExact(fromDate, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var from))
            return BadRequest(new { error = "fromDate must be in yyyy-MM-dd format." });

        if (!DateOnly.TryParseExact(toDate, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var to))
            return BadRequest(new { error = "toDate must be in yyyy-MM-dd format." });

        if (from > to)
            return BadRequest(new { error = "fromDate must not be after toDate." });

        var typeUpper = type.ToUpperInvariant();
        if (typeUpper is not ("ALL" or "AP" or "AR"))
            return BadRequest(new { error = "type must be AP, AR, or ALL." });

        var correlationId = HttpContext.TraceIdentifier;
        logger.LogInformation(
            "[InvoicesController] GetInvoices from={From} to={To} type={Type} correlationId={CorrelationId}",
            fromDate, toDate, typeUpper, correlationId);

        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue);

        List<MyInvois.Service.DataAccess.RawInvoiceRecord> records;
        try
        {
            records = await dataSource.GetInvoicesByDateRangeAsync(fromDt, toDt, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[InvoicesController] DB2 query failed correlationId={CorrelationId}",
                correlationId);
            return StatusCode(502, new
            {
                error         = "Failed to fetch invoices from MOVEX database. Check DB2 connectivity.",
                correlationId = correlationId
            });
        }

        if (typeUpper != "ALL")
            records = records.Where(r => r.InvoiceType == typeUpper).ToList();

        var items = records.Select(MapToDto).ToList();

        return Ok(new InvoiceListResponse
        {
            TotalCount = items.Count,
            FromDate   = fromDate,
            ToDate     = toDate,
            Items      = items
        });
    }

    private static InvoiceSummaryDto MapToDto(MyInvois.Service.DataAccess.RawInvoiceRecord r)
    {
        // Convert MOVEX YYYYMMDD int to ISO date string
        static string ToIsoDate(int d)
        {
            if (d <= 0) return string.Empty;
            var s = d.ToString("D8");
            return $"{s[..4]}-{s[4..6]}-{s[6..8]}";
        }

        // AR: prefer InvoiceDate (ESIVDT) if present, fall back to AccountingDate (ESACDT)
        // AP: use AccountingDate (EPACDT)
        var dateInt   = r.InvoiceType == "AR"
            ? (r.InvoiceDate.HasValue && r.InvoiceDate > 0 ? r.InvoiceDate.Value : r.AccountingDate)
            : r.AccountingDate;

        // AR: customer name from OCUSMA; AP: supplier ID (no master join at this stage)
        var partyName = r.InvoiceType == "AR"
            ? (r.CustomerName ?? r.InvoiceeName ?? r.PartyId)
            : r.PartyId;

        // AR credit notes (ESTRCD=20) carry positive ESCUAM in FSLEDG — the sign is conveyed
        // by the transaction code, not the amount. Negate here so the portal's totalInclTax < 0
        // convention works identically for AR-CN and AP-CN (AP query already does epcuam * -1).
        var sign = (r.InvoiceType == "AR" && r.TransCode == "20") ? -1m : 1m;

        return new InvoiceSummaryDto
        {
            InvoiceNo     = r.InvoiceNo.Trim(),
            Date          = ToIsoDate(dateInt),
            Type          = r.InvoiceType,
            CompanyCode   = r.CompanyCode,
            PartyName     = partyName?.Trim() ?? string.Empty,
            Currency      = r.Currency.Trim(),
            AmountExclTax = (r.InvoiceAmount - r.GstAmount) * sign,
            TaxAmount     = r.GstAmount * sign,
            TotalInclTax  = r.InvoiceAmount * sign,
        };
    }
}
