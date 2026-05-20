namespace MyInvois.Service.DataAccess.Dtos;

internal sealed class ArLineItemDto
{
    public string  InvoiceNo    { get; set; } = string.Empty;
    public int     LineNumber   { get; set; }
    public string  ItemNumber   { get; set; } = string.Empty;
    public string  Description  { get; set; } = string.Empty;
    public string  ClassificationCode { get; set; } = string.Empty;
    public decimal Quantity     { get; set; }
    public string  UnitOfMeasure { get; set; } = "EA";
    public decimal UnitPrice    { get; set; }
    public decimal LineTotal    { get; set; }
    public string  TaxCode      { get; set; } = string.Empty;
    public decimal TaxRate      { get; set; }
    public decimal TaxAmount    { get; set; }

    public RawInvoiceLineRecord ToLineRecord() => new()
    {
        LineNumber   = LineNumber,
        ItemNumber   = (ItemNumber  ?? string.Empty).Trim(),
        Description  = (Description ?? string.Empty).Trim(),
        // LHDN classification code is not stored in MOVEX.
        // Default to '022' (Others) — Finance team to map item groups before go-live.
        ClassificationCode = "022",
        Quantity     = Quantity,
        UnitOfMeasure = (UnitOfMeasure ?? "EA").Trim(),
        UnitPrice    = UnitPrice,
        LineTotal    = LineTotal,
        TaxCode      = (TaxCode ?? string.Empty).Trim(),
        TaxRate      = TaxRate,
        TaxAmount    = TaxAmount
    };
}
