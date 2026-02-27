namespace MyInvois.Service.DataAccess;

// Uses skill: architecture/clean-architecture v1.2+

/// <summary>
/// Interface for retrieving party (supplier/customer) details.
/// Source is undecided (ADR-013 Known Gap #1) — designed as pluggable provider.
/// Implementations selected via MovexDbSettings.PartyDataSource configuration.
/// </summary>
public interface IPartyDataProvider
{
    /// <summary>
    /// Get supplier details by supplier ID.
    /// Returns TIN, BRN, Name, Address for MyInvois mandatory fields.
    /// </summary>
    Task<PartyDetails?> GetSupplierDetailsAsync(string supplierId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get customer details by customer ID.
    /// Returns TIN, BRN, Name, Address for MyInvois mandatory fields.
    /// </summary>
    Task<PartyDetails?> GetCustomerDetailsAsync(string customerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Party details DTO for supplier/customer enrichment.
/// Maps to MyInvois mandatory fields: TIN, BRN, Name, Address.
/// </summary>
public class PartyDetails
{
    public string PartyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? TIN { get; set; }
    public string? BRN { get; set; }
    public string? Address { get; set; }
    public string? ContactPerson { get; set; }
    public string? CountryCode { get; set; }
    public string IdScheme { get; set; } = "BRN";
}
