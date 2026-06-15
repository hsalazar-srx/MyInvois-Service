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

    /// <summary>
    /// ISO 3166-1 alpha-2 country code for this company (e.g. "MY").
    /// Used to set the correct country and suppress the hardcoded Malaysian state in UBL.
    /// </summary>
    public string CountryCode { get; set; } = "MY";

    /// <summary>
    /// Malaysia state code for LHDN CountrySubentityCode (01=Johor, 14=W.P.Kuala Lumpur, etc.).
    /// Only required when CountryCode = "MY". Ignored for foreign companies.
    /// </summary>
    public string StateCode { get; set; } = "00";

    /// <summary>
    /// Malaysia Standard Industrial Classification (MSIC) code.
    /// </summary>
    public string MsicCode { get; set; } = "00000";

    /// <summary>
    /// MSIC activity description — appears in LHDN IndustryClassificationCode/@name.
    /// </summary>
    public string MsicDescription { get; set; } = string.Empty;
}
