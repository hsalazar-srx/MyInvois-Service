namespace MyInvois.Api.Models;

// Uses skill: architecture/dotnet-api-design v1.0

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
