namespace MyInvois.Service.Configuration;

/// <summary>
/// Company Configuration for MyInvois Integration
/// Maps to appsettings.json["Companies"]
/// Contains company-specific details for TIN, Name, BRN mapping
/// </summary>
public class CompanySettings
{
    /// <summary>
    /// List of companies configured for MyInvois submission
    /// </summary>
    public Dictionary<string, CompanyDetails> Companies { get; set; } = new();

    /// <summary>
    /// Get company details by company code
    /// </summary>
    public CompanyDetails? GetCompany(string companyCode)
    {
        return Companies.TryGetValue(companyCode, out var company) ? company : null;
    }
}

/// <summary>
/// Company Details (TIN, Name, BRN) per company code
/// </summary>
public class CompanyDetails
{
    /// <summary>
    /// Malaysian Tax Identification Number (TIN) - 12 digits
    /// </summary>
    public string TIN { get; set; } = string.Empty;

    /// <summary>
    /// Registered Company Name (≤300 chars)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Business Registration Number (BRN)
    /// </summary>
    public string BRN { get; set; } = string.Empty;

    /// <summary>
    /// ID Scheme (BRN, NRIC, PASSPORT, ARMY)
    /// </summary>
    public string IdScheme { get; set; } = "BRN";

    /// <summary>
    /// Registered Address
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Company phone number (≥8 chars, required by LHDN Contact block).
    /// </summary>
    public string? Phone { get; set; }
}
