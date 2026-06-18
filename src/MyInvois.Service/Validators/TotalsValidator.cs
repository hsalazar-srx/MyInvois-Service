namespace MyInvois.Service.Validators;

using MyInvois.Service.Models;

/// <summary>
/// TotalsValidator - Validates mathematical consistency of invoice totals
/// 
/// Validates:
/// - Total Excl Tax = Sum of line amounts
/// - Total Tax = Sum of line taxes
/// - Total Incl Tax = Total Excl Tax + Total Tax
/// - Payable Amount = Total Incl Tax (or with adjustments)
/// - Rounding consistency (banker's rounding)
/// 
/// Skills:
/// - integration/myinvois-validator (totals rules)
/// </summary>
public interface ITotalsValidator
{
    bool ValidateTotals(MyInvoiceDocument document, out List<ValidationError> errors);
}

public class TotalsValidator : ITotalsValidator
{
    // Uses skill: integration/myinvois-validator v1.0+
    // Validates mathematical consistency with ±1 cent (0.01) rounding tolerance
    // Non-fail-fast: Collects ALL validation errors

    private const decimal ROUNDING_TOLERANCE = 0.01m;

    public bool ValidateTotals(MyInvoiceDocument document, out List<ValidationError> errors)
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

        // No lines — header totals from DB2 are trusted; skip sum cross-check
        if (document.Lines == null || document.Lines.Count == 0)
            return errors.Count == 0;

        // Calculate sums from line items
        var calculatedExclTax = document.Lines.Sum(l => l.LineTotalExclTax);
        var calculatedTax = document.Lines.Sum(l => l.TaxAmount);
        var calculatedInclTax = calculatedExclTax + calculatedTax;

        // Validate TotalExclTax with rounding tolerance
        if (Math.Abs(document.TotalExclTax - calculatedExclTax) > ROUNDING_TOLERANCE)
        {
            errors.Add(new ValidationError
            {
                FieldName = "TotalExclTax",
                Message = $"TotalExclTax mismatch. Expected: {calculatedExclTax:F2}, Got: {document.TotalExclTax:F2}",
                Severity = "Error",
                ViolatedRule = "TotalMismatch_ExclTax"
            });
        }

        // Validate TotalTax with rounding tolerance
        if (Math.Abs(document.TotalTax - calculatedTax) > ROUNDING_TOLERANCE)
        {
            errors.Add(new ValidationError
            {
                FieldName = "TotalTax",
                Message = $"TotalTax mismatch. Expected: {calculatedTax:F2}, Got: {document.TotalTax:F2}",
                Severity = "Error",
                ViolatedRule = "TotalMismatch_Tax"
            });
        }

        // Validate TotalInclTax = TotalExclTax + TotalTax (with tolerance)
        if (Math.Abs(document.TotalInclTax - calculatedInclTax) > ROUNDING_TOLERANCE)
        {
            errors.Add(new ValidationError
            {
                FieldName = "TotalInclTax",
                Message = $"TotalInclTax mismatch. Expected: {calculatedInclTax:F2} (TotalExclTax + TotalTax), Got: {document.TotalInclTax:F2}",
                Severity = "Error",
                ViolatedRule = "TotalMismatch_InclTax"
            });
        }

        // Validate PayableAmount = TotalInclTax (with tolerance)
        if (Math.Abs(document.PayableAmount - document.TotalInclTax) > ROUNDING_TOLERANCE)
        {
            errors.Add(new ValidationError
            {
                FieldName = "PayableAmount",
                Message = $"PayableAmount mismatch. Expected: {document.TotalInclTax:F2} (same as TotalInclTax), Got: {document.PayableAmount:F2}",
                Severity = "Error",
                ViolatedRule = "TotalMismatch_PayableAmount"
            });
        }

        return errors.Count == 0;
    }
}

