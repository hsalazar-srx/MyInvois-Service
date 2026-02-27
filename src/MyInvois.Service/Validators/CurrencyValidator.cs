namespace MyInvois.Service.Validators;

using MyInvois.Service.Models;

/// <summary>
/// CurrencyValidator - Validates currency code and exchange rates
/// 
/// Validates:
/// - Currency code is ISO 4217 (MYR, USD, SGD, etc.)
/// - Exchange rate required if currency ≠ MYR
/// - Exchange rate > 0
/// - Exchange rate precision (max 6 decimal places)
/// 
/// Skills:
/// - integration/myinvois-validator (currency rules)
/// </summary>
public interface ICurrencyValidator
{
    bool ValidateCurrencyCode(string? code, out ValidationError? error);
    
    bool ValidateExchangeRate(decimal rate, string currencyCode, out ValidationError? error);
}

public class CurrencyValidator : ICurrencyValidator
{
    // Uses skill: integration/myinvois-validator v1.0+
    // Validates ISO 4217 currency codes (14 supported currencies)
    // Validates exchange rates with max 6 decimal places precision

    private const int CURRENCY_CODE_LENGTH = 3;
    private const int MAX_DECIMAL_PLACES = 6;

    private static readonly HashSet<string> ValidCurrencyCodes = new()
    {
        "MYR", "USD", "SGD", "IDR", "THB", "PHP", "VND", "AUD", "EUR", "GBP", "HKD", "JPY", "KRW", "CNY"
    };

    public bool ValidateCurrencyCode(string? code, out ValidationError? error)
    {
        error = null;

        // Check if code is null or empty
        if (string.IsNullOrWhiteSpace(code))
        {
            error = new ValidationError
            {
                FieldName = "CurrencyCode",
                Message = "Currency Code is required",
                Severity = "Error",
                ViolatedRule = "MandatoryField_CurrencyCode"
            };
            return false;
        }

        // Check length (exactly 3 characters per ISO 4217)
        if (code.Length != CURRENCY_CODE_LENGTH)
        {
            error = new ValidationError
            {
                FieldName = "CurrencyCode",
                Message = $"Currency Code must be exactly {CURRENCY_CODE_LENGTH} characters",
                Severity = "Error",
                ViolatedRule = "FixedLength_CurrencyCode"
            };
            return false;
        }

        // Check if code is in supported currencies list
        if (!ValidCurrencyCodes.Contains(code.ToUpperInvariant()))
        {
            error = new ValidationError
            {
                FieldName = "CurrencyCode",
                Message = $"Currency Code '{code}' is not supported. Supported: {string.Join(", ", ValidCurrencyCodes)}",
                Severity = "Error",
                ViolatedRule = "UnsupportedCurrency"
            };
            return false;
        }

        return true;
    }

    public bool ValidateExchangeRate(decimal rate, string currencyCode, out ValidationError? error)
    {
        error = null;

        // For MYR, exchange rate must be 1.0 or 0 (not set)
        if (currencyCode.Equals("MYR", StringComparison.OrdinalIgnoreCase))
        {
            if (rate != 1.0m && rate != 0m)
            {
                error = new ValidationError
                {
                    FieldName = "ExchangeRate",
                    Message = "MYR exchange rate must be 1.0 or omitted (0)",
                    Severity = "Error",
                    ViolatedRule = "InvalidMYRRate"
                };
                return false;
            }
            return true;
        }

        // For non-MYR currencies, exchange rate is mandatory and must be > 0
        if (rate <= 0m)
        {
            error = new ValidationError
            {
                FieldName = "ExchangeRate",
                Message = $"Exchange rate is required and must be greater than 0 for currency {currencyCode}",
                Severity = "Error",
                ViolatedRule = "MandatoryField_ExchangeRate"
            };
            return false;
        }

        // Validate decimal precision (max 6 decimal places)
        var decimalPlaces = GetDecimalPlaces(rate);
        if (decimalPlaces > MAX_DECIMAL_PLACES)
        {
            error = new ValidationError
            {
                FieldName = "ExchangeRate",
                Message = $"Exchange rate must not exceed {MAX_DECIMAL_PLACES} decimal places",
                Severity = "Error",
                ViolatedRule = "MaxDecimalPrecision_ExchangeRate"
            };
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the number of decimal places in a decimal value
    /// </summary>
    private int GetDecimalPlaces(decimal value)
    {
        var bits = decimal.GetBits(value);
        var scale = (bits[3] >> 16) & 0x7F;
        return scale;
    }
}

