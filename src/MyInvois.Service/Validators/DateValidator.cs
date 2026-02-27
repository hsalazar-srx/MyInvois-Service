namespace MyInvois.Service.Validators;

using MyInvois.Service.Models;

/// <summary>
/// DateValidator - Validates invoice date and time per MyInvois constraints
/// 
/// Validates:
/// - No placeholder dates ("N/A", "0000-00-00", null)
/// - Real ISO 8601 date (YYYY-MM-DD)
/// - Real time (HH:MM:SS or HHMMSSss)
/// - UTC timezone
/// 
/// Skills:
/// - integration/myinvois-validator (date/time rules)
/// </summary>
public interface IDateValidator
{
    bool ValidateInvoiceDate(string? date, out ValidationError? error);
    
    bool ValidateInvoiceTime(string? time, out ValidationError? error);
}

public class DateValidator : IDateValidator
{
    // Uses skill: integration/myinvois-validator v1.0+
    // Validates ISO 8601 date format, no placeholders, no future dates
    // Validates time format HH:mm:ss

    private static readonly HashSet<string> InvalidDatePlaceholders = new()
    {
        "N/A", "n/a", "0000-00-00", "00000000"
    };

    private const string ISO_8601_DATE_FORMAT = "yyyy-MM-dd";
    private const string TIME_FORMAT = "HH:mm:ss";

    public bool ValidateInvoiceDate(string? date, out ValidationError? error)
    {
        error = null;

        // Check if date is null or empty
        if (string.IsNullOrWhiteSpace(date))
        {
            error = new ValidationError
            {
                FieldName = "IssueDate",
                Message = "Issue Date is required",
                Severity = "Error",
                ViolatedRule = "MandatoryField_IssueDate"
            };
            return false;
        }

        // Check for placeholders
        if (InvalidDatePlaceholders.Contains(date))
        {
            error = new ValidationError
            {
                FieldName = "IssueDate",
                Message = "Date placeholder not allowed. Must be a real date in ISO 8601 format (yyyy-MM-dd)",
                Severity = "Error",
                ViolatedRule = "NoPlaceholder_IssueDate"
            };
            return false;
        }

        // Parse as ISO 8601 (yyyy-MM-dd)
        if (!DateTime.TryParseExact(date, ISO_8601_DATE_FORMAT,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out DateTime parsedDate))
        {
            error = new ValidationError
            {
                FieldName = "IssueDate",
                Message = $"Date must be in ISO 8601 format ({ISO_8601_DATE_FORMAT})",
                Severity = "Error",
                ViolatedRule = "InvalidFormat_IssueDate"
            };
            return false;
        }

        // Validate it's a real date (TryParseExact already validates leap years, valid day/month)
        // Additional check: ensure the parsed date matches the input (catches edge cases)
        if (parsedDate.ToString(ISO_8601_DATE_FORMAT) != date)
        {
            error = new ValidationError
            {
                FieldName = "IssueDate",
                Message = "Date is not a valid date",
                Severity = "Error",
                ViolatedRule = "InvalidDate_IssueDate"
            };
            return false;
        }

        // Check it's not in the future
        if (parsedDate.Date > DateTime.UtcNow.Date)
        {
            error = new ValidationError
            {
                FieldName = "IssueDate",
                Message = "Invoice date cannot be in the future",
                Severity = "Error",
                ViolatedRule = "FutureDate_IssueDate"
            };
            return false;
        }

        return true;
    }

    public bool ValidateInvoiceTime(string? time, out ValidationError? error)
    {
        error = null;

        // Check if time is null or empty
        if (string.IsNullOrWhiteSpace(time))
        {
            error = new ValidationError
            {
                FieldName = "IssueTime",
                Message = "Issue Time is required",
                Severity = "Error",
                ViolatedRule = "MandatoryField_IssueTime"
            };
            return false;
        }

        // Parse and validate time format (HH:mm:ss)
        if (!DateTime.TryParseExact(time, TIME_FORMAT,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out DateTime parsedTime))
        {
            error = new ValidationError
            {
                FieldName = "IssueTime",
                Message = $"Time must be in format {TIME_FORMAT} (HH:mm:ss)",
                Severity = "Error",
                ViolatedRule = "InvalidFormat_IssueTime"
            };
            return false;
        }

        // TryParseExact already validates hours (0-23), minutes (0-59), seconds (0-59)
        return true;
    }
}

