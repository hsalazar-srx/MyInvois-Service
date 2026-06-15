namespace MyInvois.Service.Models;

/// <summary>
/// MyInvois Document DTO - Maps to UBL 2.1 e-Invoice Schema
/// Represents the transformed invoice ready for MyInvois submission
/// </summary>
public class MyInvoiceDocument
{
    /// <summary>
    /// Internal tracking ID (GUID)
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// e-Invoice Version (e.g., "1.1")
    /// </summary>
    public string Version { get; set; } = "1.1";
    
    /// <summary>
    /// Document Type Code (01=invoice, 02=debit note, etc.)
    /// </summary>
    public string DocumentTypeCode { get; set; } = "01";
    
    /// <summary>
    /// e-Invoice Number (Supplier's invoice reference, ≤50 chars)
    /// </summary>
    public string InvoiceNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Issue Date (YYYY-MM-DD format, UTC)
    /// </summary>
    public string IssueDate { get; set; } = string.Empty;
    
    /// <summary>
    /// Issue Time (HH:MM:SS format, UTC)
    /// </summary>
    public string IssueTime { get; set; } = string.Empty;
    
    /// <summary>
    /// Currency Code (ISO 4217: MYR, USD, SGD, etc.)
    /// </summary>
    public string CurrencyCode { get; set; } = "MYR";
    
    /// <summary>
    /// Currency Exchange Rate (required if CurrencyCode != MYR)
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1.0m;
    
    // ===== SUPPLIER BLOCK (Accounting Supplier Party) =====
    
    /// <summary>
    /// Supplier TIN
    /// </summary>
    public string SupplierTIN { get; set; } = string.Empty;
    
    /// <summary>
    /// Supplier Name (registered legal name)
    /// </summary>
    public string SupplierName { get; set; } = string.Empty;
    
    /// <summary>
    /// Supplier BRN (Business Registration Number)
    /// </summary>
    public string SupplierBRN { get; set; } = string.Empty;
    
    /// <summary>
    /// Supplier ID Scheme (BRN, NRIC, PASSPORT, ARMY)
    /// </summary>
    public string SupplierIdScheme { get; set; } = "BRN";
    
    /// <summary>
    /// Supplier Address
    /// </summary>
    public string? SupplierAddress { get; set; }

    /// <summary>
    /// Supplier phone number (≥8 chars required by LHDN). Defaults to "60000000" if empty.
    /// </summary>
    public string? SupplierPhone { get; set; }

    /// <summary>
    /// Supplier ISO 3166-1 alpha-2 country code (e.g. "MY", "AU", "SG").
    /// Drives country and state fields in UBL PostalAddress.
    /// </summary>
    public string? SupplierCountryCode { get; set; }

    /// <summary>
    /// Malaysia state code for LHDN CountrySubentityCode (e.g. "01" for Johor).
    /// Only used when SupplierCountryCode = "MY". Null/empty → "00" (not specified).
    /// </summary>
    public string? SupplierStateCode { get; set; }

    /// <summary>
    /// Malaysia Standard Industrial Classification (MSIC) code.
    /// </summary>
    public string SupplierMsicCode { get; set; } = "00000";

    /// <summary>
    /// MSIC activity description for the LHDN IndustryClassificationCode/@name field.
    /// </summary>
    public string SupplierMsicDescription { get; set; } = string.Empty;

    // ===== BUYER BLOCK (Accounting Customer Party) =====
    
    /// <summary>
    /// Buyer TIN (optional if unavailable)
    /// </summary>
    public string? BuyerTIN { get; set; }
    
    /// <summary>
    /// Buyer Name
    /// </summary>
    public string BuyerName { get; set; } = string.Empty;
    
    /// <summary>
    /// Buyer ID (if TIN unavailable)
    /// </summary>
    public string? BuyerAlternativeId { get; set; }
    
    /// <summary>
    /// Buyer ID Scheme (BRN, NRIC, PASSPORT, ARMY)
    /// </summary>
    public string BuyerIdScheme { get; set; } = "BRN";
    
    /// <summary>
    /// Buyer Address
    /// </summary>
    public string? BuyerAddress { get; set; }

    /// <summary>
    /// Buyer phone number (≥8 chars required by LHDN). Defaults to "60000000" if empty.
    /// </summary>
    public string? BuyerPhone { get; set; }

    /// <summary>
    /// Buyer ISO 3166-1 alpha-2 country code (e.g. "MY", "AU", "SG").
    /// Drives country and state fields in UBL PostalAddress.
    /// </summary>
    public string? BuyerCountryCode { get; set; }

    /// <summary>
    /// Malaysia state code for LHDN CountrySubentityCode (e.g. "01" for Johor).
    /// Only used when BuyerCountryCode = "MY". Null/empty → "00" (not specified).
    /// </summary>
    public string? BuyerStateCode { get; set; }

    // ===== TOTALS =====
    
    /// <summary>
    /// Total Excl Tax (sum of all line items)
    /// </summary>
    public decimal TotalExclTax { get; set; }
    
    /// <summary>
    /// Total Tax Amount (sum of all line taxes)
    /// </summary>
    public decimal TotalTax { get; set; }
    
    /// <summary>
    /// Total Incl Tax (TotalExclTax + TotalTax)
    /// </summary>
    public decimal TotalInclTax { get; set; }
    
    /// <summary>
    /// Payable Amount (same as TotalInclTax, or with adjustments)
    /// </summary>
    public decimal PayableAmount { get; set; }
    
    // ===== LINE ITEMS =====
    
    /// <summary>
    /// Invoice Line Items (transformed to MyInvois format)
    /// </summary>
    public List<MyInvoiceLine> Lines { get; set; } = new();
    
    // ===== SIGNATURE =====
    
    /// <summary>
    /// Digital Signature (XAdES v1.1 format) - populated by signing process
    /// </summary>
    public string? Signature { get; set; }
    
    /// <summary>
    /// Signature Algorithm (e.g., "RSA-SHA256")
    /// </summary>
    public string SignatureAlgorithm { get; set; } = "RSA-SHA256";
    
    // ===== METADATA =====
    
    /// <summary>
    /// Source MOVEX invoice number (for audit trail)
    /// </summary>
    public string? SourceInvoiceNumber { get; set; }
    
    /// <summary>
    /// Timestamp when document was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Validation errors (if any)
    /// </summary>
    public List<ValidationError> ValidationErrors { get; set; } = new();
}

/// <summary>
/// MyInvois Line Item (transformed from MOVEX invoice line)
/// </summary>
public class MyInvoiceLine
{
    /// <summary>
    /// Line number (sequence)
    /// </summary>
    public int LineNumber { get; set; }
    
    /// <summary>
    /// Item Number or SKU
    /// </summary>
    public string ItemNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Item Description (≤300 chars)
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Item Classification Code (3 chars) - per MyInvois constraints
    /// </summary>
    public string ClassificationCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Quantity
    /// </summary>
    public decimal Quantity { get; set; }
    
    /// <summary>
    /// Unit of Measure (EA, KG, etc.)
    /// </summary>
    public string UnitOfMeasure { get; set; } = "EA";
    
    /// <summary>
    /// Unit Price
    /// </summary>
    public decimal UnitPrice { get; set; }
    
    /// <summary>
    /// Line Total Excl Tax (Quantity × UnitPrice)
    /// </summary>
    public decimal LineTotalExclTax { get; set; }
    
    /// <summary>
    /// Tax Code (UN/ECE 5153)
    /// </summary>
    public string TaxCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Tax Rate (as percentage, e.g., 6.0 for 6%)
    /// </summary>
    public decimal TaxRate { get; set; }
    
    /// <summary>
    /// Tax Amount
    /// </summary>
    public decimal TaxAmount { get; set; }
    
    /// <summary>
    /// Line Total Incl Tax
    /// </summary>
    public decimal LineTotalInclTax { get; set; }
}

/// <summary>
/// Validation Error (for audit trail and diagnostics)
/// </summary>
public class ValidationError
{
    /// <summary>
    /// Field name that failed validation
    /// </summary>
    public string FieldName { get; set; } = string.Empty;
    
    /// <summary>
    /// Error message (user-friendly)
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Severity: Info, Warning, Error, Critical
    /// </summary>
    public string Severity { get; set; } = "Error";
    
    /// <summary>
    /// Rule that was violated (e.g., "TIN_Format", "MandatoryField")
    /// </summary>
    public string ViolatedRule { get; set; } = string.Empty;
}

