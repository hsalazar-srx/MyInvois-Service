namespace MyInvois.Service.Configuration;

/// <summary>
/// Validation Configuration
/// Maps to appsettings.json["Validation"]
/// </summary>
public class ValidationSettings
{
    /// <summary>
    /// TIN validation cache TTL in hours (cache MyInvois TIN API lookups)
    /// </summary>
    public int TINCache_TTL_Hours { get; set; } = 1;
    
    /// <summary>
    /// Require exchange rate for non-MYR currencies (per MyInvois constraints)
    /// </summary>
    public bool RequireExchangeRateForNonMYR { get; set; } = true;
    
    /// <summary>
    /// Allow NULL buyer TIN (per MyInvois constraints, TIN mandatory "if available")
    /// </summary>
    public bool AllowNullBuyerTIN { get; set; } = true;
    
    /// <summary>
    /// Number of decimal places for currency/tax amounts (standard: 2)
    /// </summary>
    public int DecimalPlaces { get; set; } = 2;
}

