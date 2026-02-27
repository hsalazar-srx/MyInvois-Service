namespace MyInvois.Service.DataAccess;

// Uses skill: architecture/dotnet-api-design v2.0+

/// <summary>
/// Flat DTO mapping DB2 result set columns from MOVEX ledger tables.
/// Maps to: fpledg (AP), fsledg (AR), fgledg (GL) on IBM DB2/AS400.
/// This is the raw database record before party enrichment.
/// </summary>
public class RawInvoiceRecord
{
    /// <summary>
    /// Party identifier — supplier ID (fpledg.epsuno) or customer ID (fsledg.escuno)
    /// </summary>
    public string PartyId { get; set; } = string.Empty;

    /// <summary>
    /// Invoice number — fpledg.epsino (AP) or fsledg.ESCINO (AR)
    /// </summary>
    public string InvoiceNo { get; set; } = string.Empty;

    /// <summary>
    /// Accounting date — fpledg.epacdt or fsledg.esacdt (format: YYYYMMDD as int)
    /// </summary>
    public int AccountingDate { get; set; }

    /// <summary>
    /// Invoice date — fsledg.esivdt (AR only, format: YYYYMMDD as int)
    /// AP invoices may not have a separate invoice date.
    /// </summary>
    public int? InvoiceDate { get; set; }

    /// <summary>
    /// Voucher number — fpledg.epvono or fsledg.esvono
    /// Used for GL join and audit trail
    /// </summary>
    public string VoucherNumber { get; set; } = string.Empty;

    /// <summary>
    /// Currency code — fpledg.epcucd or fsledg.escucd (ISO 4217)
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Foreign exchange rate — fpledg.eparat or fsledg.esarat
    /// </summary>
    public decimal FxRate { get; set; } = 1.0m;

    /// <summary>
    /// Invoice amount in foreign currency — fpledg.epcuam or fsledg.escuam
    /// </summary>
    public decimal InvoiceAmount { get; set; }

    /// <summary>
    /// GST/tax amount — fpledg.epvtam (AP only; AR may need calculation)
    /// </summary>
    public decimal GstAmount { get; set; }

    /// <summary>
    /// Invoice type: "AP" (Accounts Payable / Purchase) or "AR" (Accounts Receivable / Sales)
    /// </summary>
    public string InvoiceType { get; set; } = string.Empty;

    /// <summary>
    /// Company code: "100" (CMP100/mvxcdta) or "300" (CMP300/mvxc300)
    /// </summary>
    public string CompanyCode { get; set; } = string.Empty;

    /// <summary>
    /// GL account code from fgledg.egait1 (joined via voucher number).
    /// AP only — AR queries do not join FGLEDG.
    /// </summary>
    public string? GlCode { get; set; }

    // --- AR-specific fields (from FSLEDG + OCUSMA + OCUSAD joins) ---

    /// <summary>Division — FSLEDG.ESDIVI</summary>
    public string? Division { get; set; }

    /// <summary>Transaction code — FSLEDG.ESTRCD</summary>
    public string? TransCode { get; set; }

    /// <summary>Invoice entry date — FSLEDG.ESRGDT (format: YYYYMMDD as int)</summary>
    public int InvoiceEntryDate { get; set; }

    /// <summary>Change version — FSLEDG.ESCHNO</summary>
    public int ChangeVersion { get; set; }

    /// <summary>Customer status — OCUSMA.OKSTAT</summary>
    public string? CustomerStatus { get; set; }

    /// <summary>Customer master name — OCUSMA.OKCUNM</summary>
    public string? CustomerName { get; set; }

    /// <summary>Customer master address line 1 — OCUSMA.OKCUA1</summary>
    public string? MasterAddress1 { get; set; }

    /// <summary>Customer master address line 2 — OCUSMA.OKCUA2</summary>
    public string? MasterAddress2 { get; set; }

    /// <summary>Customer master address line 3 — OCUSMA.OKCUA3</summary>
    public string? MasterAddress3 { get; set; }

    /// <summary>Customer master address line 4 — OCUSMA.OKCUA4</summary>
    public string? MasterAddress4 { get; set; }

    /// <summary>Invoice address name — OCUSAD.OPCUNM (ADID='INV01')</summary>
    public string? InvoiceeName { get; set; }

    /// <summary>Invoice address line 1 — OCUSAD.OPCUA1</summary>
    public string? Address1 { get; set; }

    /// <summary>Invoice address line 2 — OCUSAD.OPCUA2</summary>
    public string? Address2 { get; set; }

    /// <summary>Invoice address line 3 — OCUSAD.OPCUA3</summary>
    public string? Address3 { get; set; }

    /// <summary>Invoice address line 4 — OCUSAD.OPCUA4</summary>
    public string? Address4 { get; set; }

    /// <summary>Post code — OCUSAD.OPPONO</summary>
    public string? PostCode { get; set; }

    /// <summary>
    /// Invoice line items from OINVOL table (joined with MITMAS for classification).
    /// Populated by data source implementation when querying DB2.
    /// </summary>
    public List<RawInvoiceLineRecord> Lines { get; set; } = new();
}
