namespace MyInvois.Service.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;
using MyInvois.Service.Models;
using MyInvois.Service.Validators;

/// <summary>
/// MyInvoisMapper - Transforms MOVEX invoices to MyInvois schema
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
public interface IMyInvoisMapper
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

public class MyInvoisMapper : IMyInvoisMapper
{
    // Uses skill: integration/myinvois-document-builder v1.0+
    // Uses skill: integration/myinvois-validator v1.0+
    // Orchestrates all 5 validators for comprehensive validation

    private readonly IMandatoryFieldsValidator _mandatoryFieldsValidator;
    private readonly ITINValidator _tinValidator;
    private readonly IDateValidator _dateValidator;
    private readonly ICurrencyValidator _currencyValidator;
    private readonly ITotalsValidator _totalsValidator;
    private readonly CompanySettings _companySettings;
    private readonly ILogger<MyInvoisMapper> _logger;

    public MyInvoisMapper(
        IMandatoryFieldsValidator mandatoryFieldsValidator,
        ITINValidator tinValidator,
        IDateValidator dateValidator,
        ICurrencyValidator currencyValidator,
        ITotalsValidator totalsValidator,
        IOptions<CompanySettings> companySettings,
        ILogger<MyInvoisMapper> logger)
    {
        _mandatoryFieldsValidator = mandatoryFieldsValidator ?? throw new ArgumentNullException(nameof(mandatoryFieldsValidator));
        _tinValidator = tinValidator ?? throw new ArgumentNullException(nameof(tinValidator));
        _dateValidator = dateValidator ?? throw new ArgumentNullException(nameof(dateValidator));
        _currencyValidator = currencyValidator ?? throw new ArgumentNullException(nameof(currencyValidator));
        _totalsValidator = totalsValidator ?? throw new ArgumentNullException(nameof(totalsValidator));
        _companySettings = companySettings?.Value ?? throw new ArgumentNullException(nameof(companySettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MyInvoiceDocument Transform(MovexInvoice invoice)
    {
        if (invoice == null)
        {
            throw new ArgumentNullException(nameof(invoice));
        }

        _logger.LogInformation("Transforming MOVEX invoice {InvoiceNumber} ({InvoiceType}) to MyInvois format",
            invoice.InvoiceNumber, invoice.InvoiceType);

        var document = new MyInvoiceDocument
        {
            // Metadata
            SourceInvoiceNumber = invoice.InvoiceNumber,
            InvoiceNumber = invoice.InvoiceNumber,

            // Date/Time conversion: YYYYMMDD → yyyy-MM-dd (ISO 8601)
            IssueDate = ConvertMovexDateToISO8601(invoice.InvoiceDate),
            IssueTime = DateTime.UtcNow.ToString("HH:mm:ss"),

            // Currency
            CurrencyCode = invoice.CurrencyCode,
            ExchangeRate = invoice.ExchangeRate,

            // Totals
            TotalExclTax = invoice.TotalExclTax,
            TotalTax = invoice.TotalTax,
            TotalInclTax = invoice.TotalInclTax,
            PayableAmount = invoice.TotalInclTax
        };

        // Map parties based on invoice type
        if (invoice.InvoiceType == "Sales")
        {
            // Sales (AR): Our company is the Supplier, Customer is the Buyer
            MapOurCompanyAsSupplier(document, invoice.CompanyCode);
            MapExternalPartyAsBuyer(document, invoice.Buyer);
        }
        else // Purchase (AP)
        {
            // Purchase (AP): External supplier is the Supplier, Our company is the Buyer
            MapExternalPartyAsSupplier(document, invoice.Supplier);
            MapOurCompanyAsBuyer(document, invoice.CompanyCode);
        }

        // Map line items
        document.Lines = invoice.Lines.Select((line, index) => new MyInvoiceLine
        {
            LineNumber = line.LineNumber > 0 ? line.LineNumber : index + 1,
            ItemNumber = line.ItemNumber,
            Description = line.Description,
            ClassificationCode = line.ClassificationCode,
            Quantity = line.Quantity,
            UnitOfMeasure = line.UnitOfMeasure,
            UnitPrice = line.UnitPrice,
            LineTotalExclTax = line.LineTotal,
            TaxCode = line.TaxCode,
            TaxRate = line.TaxRate,
            TaxAmount = line.TaxAmount,
            LineTotalInclTax = line.LineTotal + line.TaxAmount
        }).ToList();

        _logger.LogInformation("Transformed invoice {InvoiceNumber} with {LineCount} lines",
            invoice.InvoiceNumber, document.Lines.Count);

        return document;
    }

    public bool ValidateDocument(MyInvoiceDocument document, out List<ValidationError> errors)
    {
        errors = new List<ValidationError>();

        if (document == null)
        {
            errors.Add(new ValidationError
            {
                FieldName = "Document",
                Message = "Document cannot be null",
                Severity = "Error",
                ViolatedRule = "DocumentNull"
            });
            return false;
        }

        _logger.LogInformation("Validating MyInvois document {InvoiceNumber} with {ValidatorCount} validators",
            document.InvoiceNumber, 5);

        // 1. Mandatory Fields Validator
        if (!_mandatoryFieldsValidator.Validate(document, out var mandatoryErrors))
        {
            errors.AddRange(mandatoryErrors);
        }

        // 2. TIN Validator - Supplier TIN (mandatory)
        if (!_tinValidator.ValidateFormat(document.SupplierTIN, out var supplierTinError, isRequired: true))
        {
            if (supplierTinError != null)
            {
                errors.Add(supplierTinError);
            }
        }

        // 3. TIN Validator - Buyer TIN (optional for B2C)
        if (!_tinValidator.ValidateFormat(document.BuyerTIN, out var buyerTinError, isRequired: false))
        {
            if (buyerTinError != null)
            {
                errors.Add(buyerTinError);
            }
        }

        // 4. Date Validator
        if (!_dateValidator.ValidateInvoiceDate(document.IssueDate, out var dateError))
        {
            if (dateError != null)
            {
                errors.Add(dateError);
            }
        }

        // 5. Currency Validator - Code
        if (!_currencyValidator.ValidateCurrencyCode(document.CurrencyCode, out var currencyCodeError))
        {
            if (currencyCodeError != null)
            {
                errors.Add(currencyCodeError);
            }
        }

        // 6. Currency Validator - Exchange Rate
        if (!_currencyValidator.ValidateExchangeRate(document.ExchangeRate, document.CurrencyCode, out var exchangeRateError))
        {
            if (exchangeRateError != null)
            {
                errors.Add(exchangeRateError);
            }
        }

        // 7. Totals Validator
        if (!_totalsValidator.ValidateTotals(document, out var totalsErrors))
        {
            errors.AddRange(totalsErrors);
        }

        var isValid = errors.Count == 0;

        _logger.LogInformation("Validation completed for {InvoiceNumber}. Valid: {IsValid}, Errors: {ErrorCount}",
            document.InvoiceNumber, isValid, errors.Count);

        return isValid;
    }

    #region Private Helper Methods

    /// <summary>
    /// Convert MOVEX date format (YYYYMMDD) to ISO 8601 (yyyy-MM-dd)
    /// </summary>
    private string ConvertMovexDateToISO8601(string movexDate)
    {
        if (string.IsNullOrWhiteSpace(movexDate) || movexDate.Length != 8)
        {
            _logger.LogWarning("Invalid MOVEX date format: {Date}. Using current date.", movexDate);
            return DateTime.UtcNow.ToString("yyyy-MM-dd");
        }

        try
        {
            var year = movexDate.Substring(0, 4);
            var month = movexDate.Substring(4, 2);
            var day = movexDate.Substring(6, 2);
            return $"{year}-{month}-{day}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting MOVEX date {Date} to ISO 8601", movexDate);
            return DateTime.UtcNow.ToString("yyyy-MM-dd");
        }
    }

    private void MapOurCompanyAsSupplier(MyInvoiceDocument document, string companyCode)
    {
        var company = _companySettings.GetCompany(companyCode);

        if (company == null)
        {
            _logger.LogWarning("Company code {CompanyCode} not found in configuration. Supplier details will be empty.", companyCode);
            return;
        }

        document.SupplierTIN = company.TIN;
        document.SupplierName = company.Name;
        document.SupplierBRN = company.BRN;
        document.SupplierIdScheme = company.IdScheme;
        document.SupplierAddress = company.Address;
    }

    private void MapOurCompanyAsBuyer(MyInvoiceDocument document, string companyCode)
    {
        var company = _companySettings.GetCompany(companyCode);

        if (company == null)
        {
            _logger.LogWarning("Company code {CompanyCode} not found in configuration. Buyer details will be empty.", companyCode);
            return;
        }

        document.BuyerTIN = company.TIN;
        document.BuyerName = company.Name;
        document.BuyerAlternativeId = company.BRN;
        document.BuyerIdScheme = company.IdScheme;
        document.BuyerAddress = company.Address;
    }

    private void MapExternalPartyAsSupplier(MyInvoiceDocument document, InvoiceParty? supplier)
    {
        if (supplier == null)
        {
            _logger.LogWarning("External supplier party is null. Mandatory fields will fail validation.");
            return;
        }

        document.SupplierTIN = supplier.TIN ?? string.Empty;
        document.SupplierName = supplier.Name;
        document.SupplierBRN = supplier.BRN ?? string.Empty;
        document.SupplierIdScheme = supplier.IdScheme;
        document.SupplierAddress = supplier.Address;
    }

    private void MapExternalPartyAsBuyer(MyInvoiceDocument document, InvoiceParty? buyer)
    {
        if (buyer == null)
        {
            _logger.LogWarning("External buyer party is null. Mandatory fields will fail validation.");
            return;
        }

        document.BuyerTIN = buyer.TIN;
        document.BuyerName = buyer.Name;
        document.BuyerAlternativeId = buyer.AlternativeId;
        document.BuyerIdScheme = buyer.IdScheme;
        document.BuyerAddress = buyer.Address;
    }

    #endregion
}
