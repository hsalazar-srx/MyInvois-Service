namespace MyInvois.Service.DataAccess.Dtos;

internal sealed class ApLineItemDto
{
    public string  SupplierId        { get; set; } = string.Empty;
    public string  SupplierInvoiceNo { get; set; } = string.Empty;
    public int     InvoiceYear       { get; set; }
    public int     LineNumber        { get; set; }
    public string  ItemNumber        { get; set; } = string.Empty;
    public string  Description       { get; set; } = string.Empty;
    public string  ClassificationCode { get; set; } = "022";
    public decimal Quantity          { get; set; }
    public string  UnitOfMeasure     { get; set; } = "EA";
    public decimal UnitPrice         { get; set; }
    public decimal LineTotal         { get; set; }
    public string  TaxCode           { get; set; } = string.Empty;
    public decimal TaxAmount         { get; set; }

    public RawInvoiceLineRecord ToLineRecord() => new()
    {
        LineNumber    = LineNumber,
        ItemNumber    = ItemNumber,
        Description   = Description,
        ClassificationCode = ClassificationCode,
        Quantity      = Quantity,
        UnitOfMeasure = UnitOfMeasure,
        UnitPrice     = UnitPrice,
        LineTotal     = LineTotal,
        TaxCode       = TaxCode,
        TaxRate       = 0, // M3 VAT rates not directly available; mapper applies LHDN tax type mapping
        TaxAmount     = TaxAmount
    };
}
