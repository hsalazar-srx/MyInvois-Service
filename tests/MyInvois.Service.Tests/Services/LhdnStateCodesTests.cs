using FluentAssertions;
using MyInvois.Service.Services;

namespace MyInvois.Service.Tests.Services;

/// <summary>
/// Tests for LhdnStateCodes (ADR-021).
///
/// Regression cover for the production CV302 rejection: the service sent the literal string "NA"
/// as CountrySubentityCode for foreign parties, and LHDN replied
/// "ItemCode NA does not exist in CodeType State Codes".
/// </summary>
[Trait("Category", "Unit")]
public class LhdnStateCodesTests
{
    [Fact]
    public void EmbeddedList_LoadsAllPublishedCodes()
    {
        // LHDN publishes 18 state codes: 0 (All States) through 17 (Not Applicable).
        LhdnStateCodes.CodeCount.Should().Be(18);
    }

    [Fact]
    public void NotApplicable_Is17_TheCodeLhdnPublishesForNonMalaysianAddresses()
    {
        LhdnStateCodes.NotApplicable.Should().Be("17");
        LhdnStateCodes.GetStateName("17").Should().Be("Not Applicable");
        LhdnStateCodes.IsValid("17").Should().BeTrue();
    }

    [Fact]
    public void AllStates_Is0_NotZeroPadded()
    {
        // The previous default was "00", which is not a code LHDN publishes.
        LhdnStateCodes.AllStates.Should().Be("0");
        LhdnStateCodes.GetStateName("0").Should().Be("All States");
    }

    [Fact]
    public void LiteralNA_IsNotValid_TheProductionRejection()
    {
        // The exact value that caused CV302 on the first production AP batch.
        LhdnStateCodes.IsValid("NA").Should().BeFalse();
        LhdnStateCodes.TryNormalise("NA").Should().BeNull();
    }

    [Theory]
    [InlineData("0",  "All States")]
    [InlineData("1",  "Johor")]
    [InlineData("10", "Selangor")]
    [InlineData("14", "Wilayah Persekutuan Kuala Lumpur")]
    [InlineData("17", "Not Applicable")]
    public void GetStateName_ReturnsLhdnPublishedName(string code, string expected)
        => LhdnStateCodes.GetStateName(code).Should().Be(expected);

    [Theory]
    [InlineData("01", "1")]    // config used zero-padded values
    [InlineData("00", "0")]
    [InlineData("07", "7")]
    [InlineData("1",  "1")]    // already unpadded
    [InlineData("10", "10")]
    [InlineData("17", "17")]
    [InlineData(" 1 ", "1")]   // whitespace tolerated
    public void TryNormalise_HandlesZeroPaddedAndPlainCodes(string input, string expected)
        => LhdnStateCodes.TryNormalise(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NA")]
    [InlineData("18")]      // beyond the published range
    [InlineData("99")]
    [InlineData("Johor")]   // name, not code
    public void TryNormalise_UnrecognisedValues_ReturnNull(string? input)
        => LhdnStateCodes.TryNormalise(input).Should().BeNull();

    [Fact]
    public void EveryPublishedCode_IsValid()
    {
        for (var i = 0; i <= 17; i++)
            LhdnStateCodes.IsValid(i.ToString()).Should().BeTrue($"code {i} is published by LHDN");
    }

    [Fact]
    public void CodesOutsidePublishedRange_AreRejected()
    {
        foreach (var code in new[] { "-1", "18", "20", "100", "NA", "OTH" })
            LhdnStateCodes.IsValid(code).Should().BeFalse($"'{code}' is not published by LHDN");
    }
}
