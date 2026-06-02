namespace MyInvois.Service.Configuration;

/// <summary>
/// MyInvois API Configuration
/// Maps to appsettings.json["MyInvoisApi"]
/// Sensitive values (ClientId, ClientSecret) injected from User Secrets
/// </summary>
public class MyInvoisApiSettings
{
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Identity server base URL — same host as the API (per LHDN SDK FAQ).
    /// Pre-prod (sandbox): https://preprod-api.myinvois.hasil.gov.my
    /// Production:         https://api.myinvois.hasil.gov.my
    /// Token endpoint:     {IdentityBaseUrl}/connect/token
    /// If empty, falls back to BaseUrl.
    /// </summary>
    public string IdentityBaseUrl { get; set; } = string.Empty;

    public string TokenEndpoint { get; set; } = "/connect/token";
    
    public string SubmissionEndpoint { get; set; } = "/api/v1.0/documentsubmissions";
    
    public string DetailsEndpoint { get; set; } = "/api/v1.0/documents/{uuid}/details";
    
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// MyInvois environment: "sandbox" or "production"
    /// </summary>
    public string Environment { get; set; } = "sandbox";
    
    /// <summary>
    /// OAuth Client ID (from User Secrets)
    /// </summary>
    public string ClientId { get; set; } = string.Empty;
    
    /// <summary>
    /// OAuth Client Secret (from User Secrets)
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;
    
    /// <summary>
    /// Malaysian TIN for organization
    /// </summary>
    public string TIN { get; set; } = string.Empty;

    /// <summary>
    /// Path to the PKCS#12 (.p12/.pfx) certificate file for XAdES signing.
    /// The certificate must be from a Malaysian CA with Key Usage = Non-Repudiation.
    /// </summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>
    /// Password for the certificate file (from User Secrets — never hardcode).
    /// User secret key: "Certificate:Password"
    /// </summary>
    public string CertificatePassword { get; set; } = string.Empty;

    /// <summary>
    /// Optional path to a plain-text file containing the certificate password.
    /// Use this instead of CertificatePassword when the password contains special
    /// characters (e.g. { }) that ASP.NET Core config token substitution corrupts.
    /// The file should contain only the raw password, no quotes or newlines.
    /// Example: C:\Certs\MyInvois\cert-password.txt
    /// </summary>
    public string CertificatePasswordFile { get; set; } = string.Empty;
}

