using FluentAssertions;
using MyInvois.Service.Services;

namespace MyInvois.Service.Tests.Services;

/// <summary>
/// Tests for LhdnCountryCodes (ADR-020).
///
/// The authority is LHDN's published country list, embedded as a CSV resource. These tests pin
/// that behaviour so the resolver cannot silently regress to a hand-maintained subset or start
/// emitting codes LHDN does not accept.
/// </summary>
[Trait("Category", "Unit")]
public class LhdnCountryCodesTests
{
    [Fact]
    public void EmbeddedList_LoadsAllPublishedCodes()
    {
        // LHDN's 2025.11.30 batch submission template lists 249 country codes.
        LhdnCountryCodes.AcceptedCodeCount.Should().Be(249);
    }

    // ── The production incident ──────────────────────────────────────────────

    [Fact]
    public void TryResolve_Belgium_ReturnsBEL_NotMalaysia()
    {
        // A Belgian supplier in the first production batch resolved to "MYS" under the old
        // hand-maintained map and was rejected by LHDN.
        LhdnCountryCodes.TryResolve("BE").Should().Be("BEL");
    }

    [Fact]
    public void TryResolve_Kosovo_ReturnsNull_BecauseLhdnDoesNotAcceptIt()
    {
        // .NET's RegionInfo maps XK -> XKK, but XKK is absent from LHDN's list. Emitting it
        // would trade a wrong-country bug for a rejected-code bug, so it must resolve to null.
        LhdnCountryCodes.TryResolve("XK").Should().BeNull();
        LhdnCountryCodes.TryResolve("XKK").Should().BeNull();
        LhdnCountryCodes.IsAccepted("XKK").Should().BeFalse();
    }

    // ── Input forms ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("BE", "BEL")]
    [InlineData("MY", "MYS")]
    [InlineData("AU", "AUS")]
    [InlineData("SG", "SGP")]
    [InlineData("US", "USA")]
    [InlineData("DE", "DEU")]
    [InlineData("NL", "NLD")]
    [InlineData("CZ", "CZE")]
    [InlineData("PL", "POL")]
    [InlineData("TR", "TUR")]
    [InlineData("AE", "ARE")]
    [InlineData("JP", "JPN")]
    [InlineData("CN", "CHN")]
    public void TryResolve_Alpha2_ReturnsAlpha3(string input, string expected)
        => LhdnCountryCodes.TryResolve(input).Should().Be(expected);

    [Theory]
    [InlineData("BEL")]
    [InlineData("MYS")]
    [InlineData("GBR")]
    [InlineData("USA")]
    public void TryResolve_Alpha3_ReturnsItself(string alpha3)
        => LhdnCountryCodes.TryResolve(alpha3).Should().Be(alpha3);

    [Theory]
    [InlineData("be")]
    [InlineData("Be")]
    [InlineData("bEl")]
    [InlineData("  BE  ")]
    public void TryResolve_IsCaseAndWhitespaceInsensitive(string input)
        => LhdnCountryCodes.TryResolve(input).Should().Be("BEL");

    [Theory]
    [InlineData("UK")]    // M3 non-standard alias
    [InlineData("uk")]
    [InlineData("  UK  ")]
    [InlineData("GB")]    // ISO 3166-1 alpha-2
    [InlineData("gb")]
    [InlineData("GBR")]   // ISO 3166-1 alpha-3 — the only form LHDN publishes
    [InlineData("gbr")]
    public void TryResolve_AllUnitedKingdomInputForms_ConvergeOnGBR(string input)
    {
        // No clash between the UK alias and the GB/GBR ISO codes: they are distinct KEYS in one
        // lookup, all mapping to the same value. Aliases are applied last in BuildLookup, so
        // UK -> GBR cannot be overwritten by the RegionInfo pass.
        LhdnCountryCodes.TryResolve(input).Should().Be("GBR");
    }

    [Fact]
    public void UkAndGb_AreValidInputsButNotEmittableCodes()
    {
        // LHDN publishes only GBR. UK and GB are accepted as INPUT but must never be emitted.
        LhdnCountryCodes.IsAccepted("GBR").Should().BeTrue();
        LhdnCountryCodes.IsAccepted("UK").Should().BeFalse();
        LhdnCountryCodes.IsAccepted("GB").Should().BeFalse();
        LhdnCountryCodes.GetCountryName("GBR").Should().Be("UNITED KINGDOM");
    }

    [Theory]
    // Every mapping from the hand-maintained dictionary this class replaced (ADR-020).
    // Pins that the change is purely additive — no previously-working code may regress.
    [InlineData("MY", "MYS")] [InlineData("MYS", "MYS")]
    [InlineData("AU", "AUS")] [InlineData("AUS", "AUS")]
    [InlineData("SG", "SGP")] [InlineData("SGP", "SGP")]
    [InlineData("US", "USA")] [InlineData("USA", "USA")]
    [InlineData("GB", "GBR")] [InlineData("GBR", "GBR")] [InlineData("UK", "GBR")]
    [InlineData("DE", "DEU")] [InlineData("DEU", "DEU")]
    [InlineData("JP", "JPN")] [InlineData("JPN", "JPN")]
    [InlineData("CN", "CHN")] [InlineData("CHN", "CHN")]
    [InlineData("TH", "THA")] [InlineData("THA", "THA")]
    [InlineData("ID", "IDN")] [InlineData("IDN", "IDN")]
    [InlineData("VN", "VNM")] [InlineData("VNM", "VNM")]
    [InlineData("PH", "PHL")] [InlineData("PHL", "PHL")]
    [InlineData("IN", "IND")] [InlineData("IND", "IND")]
    [InlineData("KR", "KOR")] [InlineData("KOR", "KOR")]
    [InlineData("TW", "TWN")] [InlineData("TWN", "TWN")]
    [InlineData("HK", "HKG")] [InlineData("HKG", "HKG")]
    [InlineData("NZ", "NZL")] [InlineData("NZL", "NZL")]
    [InlineData("FR", "FRA")] [InlineData("FRA", "FRA")]
    [InlineData("NL", "NLD")] [InlineData("NLD", "NLD")]
    [InlineData("IT", "ITA")] [InlineData("ITA", "ITA")]
    [InlineData("SE", "SWE")] [InlineData("SWE", "SWE")]
    [InlineData("FI", "FIN")] [InlineData("FIN", "FIN")]
    [InlineData("CH", "CHE")] [InlineData("CHE", "CHE")]
    [InlineData("MX", "MEX")] [InlineData("MEX", "MEX")]
    [InlineData("BR", "BRA")] [InlineData("BRA", "BRA")]
    [InlineData("CA", "CAN")] [InlineData("CAN", "CAN")]
    public void TryResolve_LegacyHandMaintainedMappings_AreUnchanged(string input, string expected)
        => LhdnCountryCodes.TryResolve(input).Should().Be(expected);

    // ── Unmapped inputs return null rather than a wrong country ──────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ZZ")]
    [InlineData("XX")]
    [InlineData("NOTACOUNTRY")]
    [InlineData("123")]
    public void TryResolve_UnmappedOrBlank_ReturnsNull(string? input)
        => LhdnCountryCodes.TryResolve(input).Should().BeNull();

    [Fact]
    public void TryResolve_NeverSilentlyReturnsMalaysiaForUnknownInput()
    {
        // The specific regression: unknown codes must not become MYS.
        foreach (var junk in new[] { "ZZ", "XX", "QQ", "NOTACOUNTRY" })
            LhdnCountryCodes.TryResolve(junk).Should().NotBe("MYS", $"'{junk}' is not Malaysia");
    }

    // ── Accepted-list membership ────────────────────────────────────────────

    [Theory]
    [InlineData("BEL", "BELGIUM")]
    [InlineData("MYS", "MALAYSIA")]
    [InlineData("ATA", "ANTARCTICA")]
    public void GetCountryName_ReturnsLhdnPublishedName(string alpha3, string expectedName)
        => LhdnCountryCodes.GetCountryName(alpha3).Should().Be(expectedName);

    [Theory]
    [InlineData("ATA")]  // Antarctica — in LHDN's list, absent from .NET RegionInfo
    [InlineData("ATF")]  // French Southern Territories
    [InlineData("BVT")]  // Bouvet Island
    [InlineData("ESH")]  // Western Sahara
    [InlineData("HMD")]  // Heard and McDonald Islands
    [InlineData("SGS")]  // South Georgia and the South Sandwich Islands
    public void IsAccepted_CodesLhdnHasButDotNetLacks_AreStillAccepted(string alpha3)
    {
        // These six exist in LHDN's list but not in .NET's region data. Because the accepted set
        // is driven by the CSV, alpha-3 input for them still resolves.
        LhdnCountryCodes.IsAccepted(alpha3).Should().BeTrue();
        LhdnCountryCodes.TryResolve(alpha3).Should().Be(alpha3);
    }

    [Fact]
    public void EveryResolvedValue_IsAcceptedByLhdn()
    {
        // Whole-set invariant: nothing the resolver emits may fall outside LHDN's list.
        var inputs = new List<string>();
        for (var a = 'A'; a <= 'Z'; a++)
            for (var b = 'A'; b <= 'Z'; b++)
                inputs.Add($"{a}{b}");

        foreach (var input in inputs)
        {
            var resolved = LhdnCountryCodes.TryResolve(input);
            if (resolved is not null)
                LhdnCountryCodes.IsAccepted(resolved).Should()
                    .BeTrue($"'{input}' resolved to '{resolved}', which LHDN must accept");
        }
    }
}
