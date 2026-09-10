namespace MyInvois.Service.Services;

using System.Reflection;

/// <summary>
/// LHDN State Codes for the UBL <c>CountrySubentityCode</c> field.
///
/// ADR-021. LHDN validates this field against its published State Codes table and rejects any
/// value not in it with CV302. The authority is <c>Resources/lhdn-state-codes.csv</c>, extracted
/// from the MyInvois batch submission template (2025.11.30) and embedded in the assembly.
///
/// The service previously sent the literal string <c>"NA"</c> for foreign parties, producing:
/// <c>CV302: ItemCode NA does not exist in CodeType State Codes</c> on the
/// <c>AccountingSupplierParty…CountrySubentityCode</c> path. LHDN publishes code <c>17</c>
/// ("Not Applicable") for precisely that case.
///
/// Only AP self-billed invoices with a foreign supplier were affected. Malaysian-to-Malaysian
/// sales invoices carry a real state code, so UAT — which had no foreign suppliers — never
/// exercised the path.
/// </summary>
public static class LhdnStateCodes
{
    private const string ResourceName = "MyInvois.Service.Resources.lhdn-state-codes.csv";

    /// <summary>Code 17 — "Not Applicable". Correct value for a non-Malaysian address.</summary>
    public const string NotApplicable = "17";

    /// <summary>Code 0 — "All States". Used when a Malaysian party has no specific state on file.</summary>
    public const string AllStates = "0";

    private static readonly IReadOnlyDictionary<string, string> Codes = LoadCodes();

    /// <summary>Number of state codes loaded. Exposed for diagnostics and tests.</summary>
    public static int CodeCount => Codes.Count;

    /// <summary>True if the value is a state code LHDN accepts.</summary>
    public static bool IsValid(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Codes.ContainsKey(code.Trim());

    /// <summary>The state name LHDN publishes for a code, or null if not accepted.</summary>
    public static string? GetStateName(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Codes.TryGetValue(code.Trim(), out var name) ? name : null;

    /// <summary>
    /// Normalise a configured state code to an LHDN-accepted value.
    /// Accepts zero-padded input ("07" → "7") since M3 and older config used padded forms.
    /// Returns null when the value cannot be mapped — callers must not substitute a real state.
    /// </summary>
    public static string? TryNormalise(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var trimmed = code.Trim();
        if (Codes.ContainsKey(trimmed)) return trimmed;

        // "00" -> "0", "07" -> "7". LHDN publishes unpadded codes.
        var unpadded = trimmed.TrimStart('0');
        if (unpadded.Length == 0) unpadded = "0";

        return Codes.ContainsKey(unpadded) ? unpadded : null;
    }

    private static IReadOnlyDictionary<string, string> LoadCodes()
    {
        var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. The LHDN state code list must be " +
                "embedded in the assembly — check the EmbeddedResource entry in MyInvois.Service.csproj.");

        using var reader = new StreamReader(stream);

        reader.ReadLine(); // header: Code,State

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var separator = line.IndexOf(',');
            if (separator <= 0) continue;

            var code = line[..separator].Trim();
            var name = line[(separator + 1)..].Trim();

            if (code.Length > 0)
                codes[code] = name;
        }

        if (codes.Count == 0)
            throw new InvalidOperationException(
                "LHDN state code list loaded but contained no entries — the embedded CSV is malformed.");

        return codes;
    }
}
