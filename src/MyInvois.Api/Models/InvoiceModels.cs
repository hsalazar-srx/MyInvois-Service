namespace MyInvois.Api.Models;

// Uses skill: architecture/dotnet-api-design v1.0

/// <summary>
/// Request body for POST /api/v1/batch/process-range.
/// </summary>
public class ProcessRangeRequest
{
    /// <summary>Start date, inclusive (yyyy-MM-dd).</summary>
    public string FromDate { get; set; } = string.Empty;

    /// <summary>End date, inclusive (yyyy-MM-dd).</summary>
    public string ToDate { get; set; } = string.Empty;
}

/// <summary>
/// Summary response for POST /api/v1/batch/process-range.
/// Full per-invoice detail is omitted to keep the response payload small.
/// </summary>
public class BatchProcessResponse
{
    public string   BatchId        { get; set; } = string.Empty;
    public string   BatchType      { get; set; } = string.Empty;
    public string   FromDate       { get; set; } = string.Empty;
    public string   ToDate         { get; set; } = string.Empty;
    public int      TotalInvoices  { get; set; }
    public int      SuccessCount   { get; set; }
    public int      FailedCount    { get; set; }
    public int      SkippedCount   { get; set; }
    public decimal  SuccessRate    { get; set; }
    public long     DurationSeconds { get; set; }
    public string?  ErrorSummary   { get; set; }
    public DateTime StartedAt      { get; set; }
    public DateTime? CompletedAt   { get; set; }
}


/// <summary>
/// Invoice summary for the list endpoint GET /api/v1/invoices.
/// Mapped from MyInvois.Service.DataAccess.RawInvoiceRecord.
/// </summary>
public class InvoiceSummaryDto
{
    public string  InvoiceNo     { get; set; } = string.Empty;

    /// <summary>ISO date "yyyy-MM-dd" — InvoiceDate (AR) or AccountingDate (AP), YYYYMMDD int.</summary>
    public string  Date          { get; set; } = string.Empty;

    /// <summary>"AP" (Accounts Payable) or "AR" (Accounts Receivable)</summary>
    public string  Type          { get; set; } = string.Empty;

    public string  CompanyCode   { get; set; } = string.Empty;

    /// <summary>CustomerName (AR from OCUSMA) or PartyId/supplier ID (AP — no master join yet).</summary>
    public string  PartyName     { get; set; } = string.Empty;

    /// <summary>ISO 4217 currency code.</summary>
    public string  Currency      { get; set; } = string.Empty;

    public decimal AmountExclTax { get; set; }
    public decimal TaxAmount     { get; set; }
    public decimal TotalInclTax  { get; set; }
}

/// <summary>
/// Response envelope for GET /api/v1/invoices.
/// TotalCount is included now so future server-side pagination can be added
/// without a breaking contract change.
/// </summary>
public class InvoiceListResponse
{
    public int                     TotalCount { get; set; }
    public string                  FromDate   { get; set; } = string.Empty;
    public string                  ToDate     { get; set; } = string.Empty;
    public List<InvoiceSummaryDto> Items      { get; set; } = [];
}
