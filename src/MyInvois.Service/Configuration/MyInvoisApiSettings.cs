namespace MyInvois.Service.Configuration;

/// <summary>
/// MyInvois API Configuration
/// Maps to appsettings.json["MyInvoisApi"]
/// Sensitive values (ClientId, ClientSecret) injected from User Secrets
/// </summary>
public class MyInvoisApiSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    
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
}

