namespace MyInvois.Service.Services;

using MyInvois.Service.Models;

/// <summary>
/// MyInvoiceMapper - Transforms MOVEX invoices to MyInvois schema
/// 
/// Responsibilities:
/// - Transform MovexInvoice to MyInvoiceDocument (UBL 2.1)
/// - Apply all MyInvois validation rules (per 03-myinvois-requirements.md)
/// - Handle mandatory field mapping
/// - Validate currency, exchange rates, totals
/// - Generate validation errors if validation fails
/// 
/// Validation Rules Implemented:
/// - MandatoryFieldsValidator: All 20+ mandatory fields
/// - TINValidator: TIN format and optional validation
/// - DateValidator: Real dates, UTC conversion
/// - CurrencyValidator: ISO 4217 codes, exchange rates
/// - TotalsValidator: Mathematical consistency
/// 
/// Skills:
/// - integration/myinvois-document-builder (UBL 2.1 mapping)
/// - integration/myinvois-validator (validation orchestration)
/// </summary>
public interface IMyInvoiceMapper
{
    /// <summary>
    /// Transform MOVEX invoice to MyInvois document
    /// </summary>
    MyInvoiceDocument Transform(MovexInvoice invoice);
    
    /// <summary>
    /// Validate transformed document before submission
    /// </summary>
    bool ValidateDocument(MyInvoiceDocument document, out List<ValidationError> errors);
}

public class MyInvoiceMapper : IMyInvoiceMapper
{
    public MyInvoiceDocument Transform(MovexInvoice invoice)
    {
        // TODO: Week 2 - Implement transformation
        // 1. Map supplier fields (TIN, Name, BRN, Address)
        // 2. Map buyer fields (TIN, Name, BRN, Address)
        // 3. Map invoice header (number, date, time, currency, exchange rate)
        // 4. Map line items with classification codes
        // 5. Calculate and validate totals
        // 6. Return MyInvoiceDocument with all mandatory fields populated
        throw new NotImplementedException("Mapper implementation scheduled for Week 2");
    }
    
    public bool ValidateDocument(MyInvoiceDocument document, out List<ValidationError> errors)
    {
        errors = new List<ValidationError>();
        
        // TODO: Week 2 - Implement validation
        // 1. Call MandatoryFieldsValidator for all 20+ fields
        // 2. Call TINValidator for supplier/buyer TIN
        // 3. Call DateValidator for invoice date/time
        // 4. Call CurrencyValidator for currency and exchange rate
        // 5. Call TotalsValidator for mathematical consistency
        // 6. Collect all validation errors
        
        return errors.Count == 0;
    }
}

