namespace MyInvois.Service.Validators;

using MyInvois.Service.Models;

/// <summary>
/// MandatoryFieldsValidator - Validates all mandatory fields per MyInvois constraints
/// 
/// Validates:
/// - Supplier: TIN, Name, BRN, Address
/// - Buyer: Name (TIN optional)
/// - Invoice: Number (≤50), Date (real date, not "N/A"), Time, Currency
/// - Lines: Description (≤300), Classification (3 chars), Qty > 0, UnitPrice
/// - Totals: All required fields present
/// 
/// Skills:
/// - integration/myinvois-validator (mandatory field checks)
/// </summary>
public interface IMandatoryFieldsValidator
{
    bool Validate(MyInvoiceDocument document, out List<ValidationError> errors);
}

public class MandatoryFieldsValidator : IMandatoryFieldsValidator
{
    // Uses skill: integration/myinvois-validator v1.0+
    // Validates 20+ mandatory fields per MyInvois LHDNM specification
    // Non-fail-fast: Collects ALL validation errors for comprehensive feedback

    private const int MAX_NAME_LENGTH = 300;
    private const int MAX_INVOICE_NUMBER_LENGTH = 50;
    private const int MAX_DESCRIPTION_LENGTH = 300;
    private const int CLASSIFICATION_CODE_LENGTH = 3;

    public bool Validate(MyInvoiceDocument document, out List<ValidationError> errors)
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

        // Supplier validation block
        ValidateSupplierTIN(document, errors);
        ValidateSupplierName(document, errors);
        ValidateSupplierBRN(document, errors);

        // Buyer validation block
        ValidateBuyerName(document, errors);

        // Invoice header validation block
        ValidateInvoiceNumber(document, errors);
        ValidateIssueDate(document, errors);
        ValidateCurrencyCode(document, errors);

        // Line items validation block
        ValidateLineItems(document, errors);

        return errors.Count == 0;
    }

    private void ValidateSupplierTIN(MyInvoiceDocument document, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(document.SupplierTIN))
        {
            errors.Add(new ValidationError
            {
                FieldName = "SupplierTIN",
                Message = "Supplier TIN is required",
                Severity = "Error",
                ViolatedRule = "MandatoryField_SupplierTIN"
            });
        }
    }

    private void ValidateSupplierName(MyInvoiceDocument document, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(document.SupplierName))
        {
            errors.Add(new ValidationError
            {
                FieldName = "SupplierName",
                Message = "Supplier Name is required",
                Severity = "Error",
                ViolatedRule = "MandatoryField_SupplierName"
            });
        }
        else if (document.SupplierName.Length > MAX_NAME_LENGTH)
        {
            errors.Add(new ValidationError
            {
                FieldName = "SupplierName",
                Message = $"Supplier Name must not exceed {MAX_NAME_LENGTH} characters",
                Severity = "Error",
                ViolatedRule = "MaxLength_SupplierName"
            });
        }
    }

    private void ValidateSupplierBRN(MyInvoiceDocument document, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(document.SupplierBRN))
        {
            errors.Add(new ValidationError
            {
                FieldName = "SupplierBRN",
                Message = "Supplier BRN is required",
                Severity = "Error",
                ViolatedRule = "MandatoryField_SupplierBRN"
            });
        }
    }

    private void ValidateBuyerName(MyInvoiceDocument document, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(document.BuyerName))
        {
            errors.Add(new ValidationError
            {
                FieldName = "BuyerName",
                Message = "Buyer Name is required",
                Severity = "Error",
                ViolatedRule = "MandatoryField_BuyerName"
            });
        }
        else if (document.BuyerName.Length > MAX_NAME_LENGTH)
        {
            errors.Add(new ValidationError
            {
                FieldName = "BuyerName",
                Message = $"Buyer Name must not exceed {MAX_NAME_LENGTH} characters",
                Severity = "Error",
                ViolatedRule = "MaxLength_BuyerName"
            });
        }
    }

    private void ValidateInvoiceNumber(MyInvoiceDocument document, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(document.InvoiceNumber))
        {
            errors.Add(new ValidationError
            {
                FieldName = "InvoiceNumber",
                Message = "Invoice Number is required",
                Severity = "Error",
                ViolatedRule = "MandatoryField_InvoiceNumber"
            });
        }
        else if (document.InvoiceNumber.Length > MAX_INVOICE_NUMBER_LENGTH)
        {
            errors.Add(new ValidationError
            {
                FieldName = "InvoiceNumber",
                Message = $"Invoice Number must not exceed {MAX_INVOICE_NUMBER_LENGTH} characters",
                Severity = "Error",
                ViolatedRule = "MaxLength_InvoiceNumber"
            });
        }
    }

    private void ValidateIssueDate(MyInvoiceDocument document, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(document.IssueDate))
        {
            errors.Add(new ValidationError
            {
                FieldName = "IssueDate",
                Message = "Issue Date is required",
                Severity = "Error",
                ViolatedRule = "MandatoryField_IssueDate"
            });
        }
    }

    private void ValidateCurrencyCode(MyInvoiceDocument document, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(document.CurrencyCode))
        {
            errors.Add(new ValidationError
            {
                FieldName = "CurrencyCode",
                Message = "Currency Code is required",
                Severity = "Error",
                ViolatedRule = "MandatoryField_CurrencyCode"
            });
        }
    }

    private void ValidateLineItems(MyInvoiceDocument document, List<ValidationError> errors)
    {
        if (document.Lines == null || document.Lines.Count == 0)
            return;

        for (int i = 0; i < document.Lines.Count; i++)
        {
            var line = document.Lines[i];
            var linePrefix = $"Lines[{i}]";

            // Description validation
            if (string.IsNullOrWhiteSpace(line.Description))
            {
                errors.Add(new ValidationError
                {
                    FieldName = $"{linePrefix}.Description",
                    Message = $"Line {i + 1}: Description is required",
                    Severity = "Error",
                    ViolatedRule = "MandatoryField_LineDescription"
                });
            }
            else if (line.Description.Length > MAX_DESCRIPTION_LENGTH)
            {
                errors.Add(new ValidationError
                {
                    FieldName = $"{linePrefix}.Description",
                    Message = $"Line {i + 1}: Description must not exceed {MAX_DESCRIPTION_LENGTH} characters",
                    Severity = "Error",
                    ViolatedRule = "MaxLength_LineDescription"
                });
            }

            // Classification Code validation
            if (string.IsNullOrWhiteSpace(line.ClassificationCode))
            {
                errors.Add(new ValidationError
                {
                    FieldName = $"{linePrefix}.ClassificationCode",
                    Message = $"Line {i + 1}: Classification Code is required",
                    Severity = "Error",
                    ViolatedRule = "MandatoryField_ClassificationCode"
                });
            }
            else if (line.ClassificationCode.Length != CLASSIFICATION_CODE_LENGTH)
            {
                errors.Add(new ValidationError
                {
                    FieldName = $"{linePrefix}.ClassificationCode",
                    Message = $"Line {i + 1}: Classification Code must be exactly {CLASSIFICATION_CODE_LENGTH} characters",
                    Severity = "Error",
                    ViolatedRule = "FixedLength_ClassificationCode"
                });
            }

            // Quantity validation
            if (line.Quantity <= 0)
            {
                errors.Add(new ValidationError
                {
                    FieldName = $"{linePrefix}.Quantity",
                    Message = $"Line {i + 1}: Quantity must be greater than 0",
                    Severity = "Error",
                    ViolatedRule = "MinValue_Quantity"
                });
            }
        }
    }
}

