namespace MyInvois.Service.Services;

using System.Globalization;
using System.Reflection;

/// <summary>
/// Resolves country codes to the ISO 3166-1 alpha-3 values that LHDN actually accepts.
///
/// ADR-020. The authority is <c>Resources/lhdn-country-codes.csv</c> — LHDN's own published
/// list, extracted from the MyInvois batch submission template (2025.11.30, 249 codes).
/// It is embedded in the assembly, so what we validate against is exactly what LHDN published.
///
/// .NET's <see cref="RegionInfo"/> is used only to translate alpha-2 input (M3 stores 2-char
/// codes) into alpha-3. Every translated value is then checked against the LHDN list — a code
/// .NET knows but LHDN does not is treated as unmapped rather than sent. That check is not
/// theoretical: .NET emits <c>XKK</c> for Kosovo, which is absent from LHDN's list and would be
/// rejected on submission.
///
/// Why this class exists: a hand-maintained map of ~26 countries silently resolved any unlisted
/// code to <c>"MYS"</c>. A Belgian supplier in the first production batch was therefore declared
/// Malaysian, and LHDN rejected the document. Returning null for unmapped codes lets the caller
/// surface the problem instead of mislabelling a foreign party.
/// </summary>
public static class LhdnCountryCodes
{
    private const string ResourceName = "MyInvois.Service.Resources.lhdn-country-codes.csv";

    /// <summary>
    /// Non-ISO country codes observed in this M3 installation.
    /// Each target must exist in the LHDN list or it is dropped when the lookup is built.
    /// Declared first: static field initializers run in declaration order, and BuildLookup reads this.
    /// </summary>
    private static readonly (string Input, string Alpha3)[] InstallationAliases =
    {
        ("UK", "GBR"),   // ISO 3166-1 for the United Kingdom is GB; M3 stores UK
    };

    /// <summary>Alpha-3 codes LHDN accepts, with their published country names.</summary>
    private static readonly IReadOnlyDictionary<string, string> Accepted = LoadAcceptedCodes();

    /// <summary>Any input form (alpha-2, alpha-3, known alias) → accepted alpha-3.</summary>
    private static readonly IReadOnlyDictionary<string, string> Lookup = BuildLookup(Accepted, InstallationAliases);

    /// <summary>Number of accepted codes loaded from the LHDN list. Exposed for diagnostics.</summary>
    public static int AcceptedCodeCount => Accepted.Count;

    /// <summary>
    /// Resolve a country code to an LHDN-accepted alpha-3 value.
    /// Returns null when the input is blank, unrecognised, or maps to a code LHDN does not accept.
    /// Callers must decide what to do with null — never substitute a different country.
    /// </summary>
    public static string? TryResolve(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) return null;
        return Lookup.TryGetValue(countryCode.Trim(), out var alpha3) ? alpha3 : null;
    }

    /// <summary>True if the value is an alpha-3 code LHDN accepts.</summary>
    public static bool IsAccepted(string? alpha3) =>
        !string.IsNullOrWhiteSpace(alpha3) && Accepted.ContainsKey(alpha3.Trim());

    /// <summary>The country name LHDN publishes for an accepted code, or null.</summary>
    public static string? GetCountryName(string? alpha3) =>
        !string.IsNullOrWhiteSpace(alpha3) && Accepted.TryGetValue(alpha3.Trim(), out var name)
            ? name
            : null;

    private static IReadOnlyDictionary<string, string> LoadAcceptedCodes()
    {
        var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. The LHDN country code list must be " +
                "embedded in the assembly — check the EmbeddedResource entry in MyInvois.Service.csproj.");

        using var reader = new StreamReader(stream);

        reader.ReadLine(); // header: Code,Country

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var separator = line.IndexOf(',');
            if (separator <= 0) continue;

            var code = line[..separator].Trim();
            var name = line[(separator + 1)..].Trim();

            if (code.Length == 3)
                codes[code] = name;
        }

        if (codes.Count == 0)
            throw new InvalidOperationException(
                "LHDN country code list loaded but contained no entries — the embedded CSV is malformed.");

        return codes;
    }

    /// <summary>
    /// Dependencies are passed in rather than read from static fields, so this cannot observe a
    /// half-initialized type regardless of field declaration order.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildLookup(
        IReadOnlyDictionary<string, string> accepted,
        (string Input, string Alpha3)[] aliases)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Every accepted alpha-3 maps to itself.
        foreach (var code in accepted.Keys)
            lookup[code] = code;

        // Alpha-2 → alpha-3 via RegionInfo, but only where LHDN accepts the result.
        try
        {
            foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                try
                {
                    var region = new RegionInfo(culture.Name);
                    var alpha2 = region.TwoLetterISORegionName;
                    var alpha3 = region.ThreeLetterISORegionName;

                    if (alpha2.Length != 2 || alpha3.Length != 3) continue;
                    if (!alpha3.All(char.IsLetter)) continue;

                    // The guard that matters: .NET knows codes LHDN does not accept (e.g. XKK).
                    if (!accepted.ContainsKey(alpha3)) continue;

                    lookup[alpha2] = accepted.Keys.First(k =>
                        k.Equals(alpha3, StringComparison.OrdinalIgnoreCase));
                }
                catch (ArgumentException)
                {
                    // Culture carries no region — skip.
                }
            }
        }
        catch (Exception)
        {
            // Globalization data unavailable (InvariantGlobalization). Alpha-3 input still resolves;
            // alpha-2 input falls back to the explicit aliases below.
        }

        foreach (var (input, alpha3) in aliases)
        {
            if (accepted.ContainsKey(alpha3))
                lookup[input] = alpha3;
        }

        return lookup;
    }
}
