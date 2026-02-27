namespace MyInvois.Service.Models;

/// <summary>
/// MOVEX Invoice DTO - Maps to MOVEX database records (DB2 on AS/400)
/// Represents an invoice fetched from MOVEX ERP ledger tables (fpledg/fsledg)
/// Enriched with party data from IPartyDataProvider
/// </summary>
public class MovexInvoice
{
    /// <summary>
    /// Invoice Number — fpledg.epsino (AP) or fsledg.esivno (AR)
    /// </summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>
    /// Invoice/Accounting Date — format: YYYYMMDD
    /// Source: fsledg.esivdt (AR) or fpledg.epacdt (AP)
    /// </summary>
    public string InvoiceDate { get; set; } = string.Empty;
    
    /// <summary>
    /// Invoice Type: "Sales" or "Purchase"
    /// </summary>
    public string InvoiceType { get; set; } = string.Empty;
    
    /// <summary>
    /// Currency Code — fpledg.epcucd / fsledg.escucd (ISO 4217: MYR, USD, SGD, etc.)
    /// </summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>
    /// Exchange Rate — fpledg.eparat / fsledg.esarat. Required if CurrencyCode != MYR
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1.0m;

    /// <summary>
    /// Total Excl Tax (InvoiceAmount - GstAmount)
    /// </summary>
    public decimal TotalExclTax { get; set; }

    /// <summary>
    /// Total Tax — fpledg.epvtam (AP GST amount)
    /// </summary>
    public decimal TotalTax { get; set; }

    /// <summary>
    /// Total Incl Tax — fpledg.epcuam / fsledg.escuam (invoice amount in foreign currency)
    /// </summary>
    public decimal TotalInclTax { get; set; }

    /// <summary>
    /// Company Code: "100" (CMP100/mvxcdta) or "300" (CMP300/mvxc300)
    /// </summary>
    public string CompanyCode { get; set; } = string.Empty;

    /// <summary>
    /// Voucher Number — fpledg.epvono / fsledg.esvono (used for GL join and audit trail)
    /// </summary>
    public string VoucherNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Supplier/Seller Information
    /// </summary>
    public InvoiceParty? Supplier { get; set; }
    
    /// <summary>
    /// Buyer/Customer Information
    /// </summary>
    public InvoiceParty? Buyer { get; set; }
    
    /// <summary>
    /// Invoice Line Items
    /// </summary>
    public List<InvoiceLine> Lines { get; set; } = new();
    
    /// <summary>
    /// Status in MOVEX (Open, Posted, Completed, etc.)
    /// </summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Invoice Party (Supplier/Buyer)
/// </summary>
public class InvoiceParty
{
    /// <summary>
    /// TIN - Tax Identification Number (Malaysian)
    /// </summary>
    public string? TIN { get; set; }
    
    /// <summary>
    /// Party Name (registered legal name)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Business Registration Number
    /// </summary>
    public string? BRN { get; set; }
    
    /// <summary>
    /// Alternative ID (NRIC, Passport, Army ID)
    /// </summary>
    public string? AlternativeId { get; set; }
    
    /// <summary>
    /// ID Scheme (BRN, NRIC, PASSPORT, ARMY)
    /// </summary>
    public string IdScheme { get; set; } = "BRN";
    
    /// <summary>
    /// Address
    /// </summary>
    public string? Address { get; set; }
    
    /// <summary>
    /// Contact Person
    /// </summary>
    public string? ContactPerson { get; set; }
}

/// <summary>
/// Invoice Line Item
/// </summary>
public class InvoiceLine
{
    /// <summary>
    /// Line number (sequence)
    /// </summary>
    public int LineNumber { get; set; }
    
    /// <summary>
    /// Item Number (MITMAS.ITNO or description)
    /// </summary>
    public string ItemNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Item Description (OINVOL.ITDS)
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Item Classification Code (MITMAS.ITCL) - 3 chars
    /// </summary>
    public string ClassificationCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Quantity (OINVOL.IVQA)
    /// </summary>
    public decimal Quantity { get; set; }
    
    /// <summary>
    /// Unit of Measure
    /// </summary>
    public string UnitOfMeasure { get; set; } = "EA";
    
    /// <summary>
    /// Unit Price (OINVOL.SAPR)
    /// </summary>
    public decimal UnitPrice { get; set; }
    
    /// <summary>
    /// Line Total Excl Tax
    /// </summary>
    public decimal LineTotal { get; set; }
    
    /// <summary>
    /// Tax Code (OINVOL.VTCD) - UN/ECE 5153
    /// </summary>
    public string TaxCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Tax Rate as percentage
    /// </summary>
    public decimal TaxRate { get; set; }
    
    /// <summary>
    /// Tax Amount (OINVOL.VTAM)
    /// </summary>
    public decimal TaxAmount { get; set; }
}

