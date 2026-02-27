namespace MyInvois.Service.Configuration;

/// <summary>
/// Default TIN/BRN values for foreign (non-Malaysian) parties.
/// MyInvois uses generic "EI" TINs for foreign entities:
///   - EI00000000030: Foreign suppliers (self-billed imports)
///   - EI00000000020: Foreign buyers (export sales)
/// Maps to appsettings.json["ForeignPartyDefaults"]
/// </summary>
public class ForeignPartyDefaultsSettings
{
    public string SupplierTIN { get; set; } = "EI00000000030";
    public string SupplierBRN { get; set; } = "NA";
    public string BuyerTIN { get; set; } = "EI00000000020";
    public string BuyerBRN { get; set; } = "NA";
}
