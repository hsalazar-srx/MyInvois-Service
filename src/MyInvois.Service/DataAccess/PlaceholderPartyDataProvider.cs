namespace MyInvois.Service.DataAccess;

using Microsoft.Extensions.Logging;

// Uses skill: architecture/dotnet-api-design v2.0+

/// <summary>
/// Placeholder implementation of IPartyDataProvider.
/// Returns party ID as Name with null TIN/BRN/Address.
/// Logs a warning on every call to highlight that real party data is needed.
///
/// This unblocks development while the party data source is undecided (ADR-013 Known Gap #1).
/// Replace with MovexMasterPartyDataProvider or CustomLookupPartyDataProvider before production.
/// </summary>
public class PlaceholderPartyDataProvider : IPartyDataProvider
{
    private readonly ILogger<PlaceholderPartyDataProvider> _logger;

    public PlaceholderPartyDataProvider(ILogger<PlaceholderPartyDataProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<PartyDetails?> GetSupplierDetailsAsync(string supplierId, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("PlaceholderPartyDataProvider: Returning placeholder data for supplier {SupplierId}. " +
            "TIN/BRN/Address are null — MyInvois validation will fail until real party data provider is configured.",
            supplierId);

        return Task.FromResult<PartyDetails?>(new PartyDetails
        {
            PartyId = supplierId,
            Name = $"Supplier {supplierId}",
            TIN = null,
            BRN = null,
            Address = null,
            ContactPerson = null,
            IdScheme = "BRN"
        });
    }

    public Task<PartyDetails?> GetCustomerDetailsAsync(string customerId, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("PlaceholderPartyDataProvider: Returning placeholder data for customer {CustomerId}. " +
            "TIN/BRN/Address are null — MyInvois validation will fail until real party data provider is configured.",
            customerId);

        return Task.FromResult<PartyDetails?>(new PartyDetails
        {
            PartyId = customerId,
            Name = $"Customer {customerId}",
            TIN = null,
            BRN = null,
            Address = null,
            ContactPerson = null,
            IdScheme = "BRN"
        });
    }
}
