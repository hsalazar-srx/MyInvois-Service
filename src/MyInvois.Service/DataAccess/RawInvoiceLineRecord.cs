namespace MyInvois.Service.DataAccess;

/// <summary>
/// Flat DTO mapping DB2 result set columns for invoice line items.
///
/// Uses skill: integration/movex-db2-data-source v1.0+
///
/// DB2 Source Tables (varies by invoice type):
///   AR (Sales):  OINVOL (invoice lines) + MITMAS (item classification)
///   AP (Purchase): FGINLI (invoice lines) + MPLINE (PO line for item details)
///
/// LHDN classification codes (e.g., "022") are assigned in the mapper layer,
/// not sourced from M3 item master (MITMAS.ITCL).
/// </summary>
public class RawInvoiceLineRecord
{
    /// <summary>
    /// Line number — OINVOL.OILVNO
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Item number — OINVOL.OILITNO
    /// </summary>
    public string ItemNumber { get; set; } = string.Empty;

    /// <summary>
    /// Item description — OINVOL.OILITDS (≤300 chars for MyInvois)
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Classification code (3 chars) — MITMAS.ITCL (joined via OILITNO = ITNO)
    /// </summary>
    public string ClassificationCode { get; set; } = string.Empty;

    /// <summary>
    /// Quantity — OINVOL.OILQA (must be > 0)
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Unit of measure — OINVOL.OILUN (default "EA")
    /// </summary>
    public string UnitOfMeasure { get; set; } = "EA";

    /// <summary>
    /// Unit price — OINVOL.OILSA
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Line total excl tax (Quantity × UnitPrice)
    /// </summary>
    public decimal LineTotal { get; set; }

    /// <summary>
    /// Tax code — OINVOL.OILVTCD (maps to UN/ECE 5153)
    /// </summary>
    public string TaxCode { get; set; } = string.Empty;

    /// <summary>
    /// Tax rate as percentage
    /// </summary>
    public decimal TaxRate { get; set; }

    /// <summary>
    /// Tax amount — OINVOL.OILTAX
    /// </summary>
    public decimal TaxAmount { get; set; }
}
