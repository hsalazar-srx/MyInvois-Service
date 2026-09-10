using FluentAssertions;
using MyInvois.Service.Models;
using MyInvois.Service.Services;
using System.Text.Json;

namespace MyInvois.Service.Tests.Services;

public class UblDocumentBuilderTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static MyInvoiceDocument MinimalDoc(string? buyerCountry = "MY", string? supplierCountry = "MY") => new()
    {
        InvoiceNumber = "TEST-001",
        IssueDate = "2026-07-01",
        IssueTime = "12:00:00",
        CurrencyCode = "MYR",
        SupplierTIN = "C20921865070",
        SupplierName = "Scanfil APAC Sdn. Bhd.",
        SupplierBRN = "200901031056",
        SupplierPhone = "6072319006",
        SupplierCountryCode = supplierCountry,
        BuyerTIN = "EI00000000010",
        BuyerName = "Test Buyer",
        BuyerPhone = "6072319006",
        BuyerCountryCode = buyerCountry,
        TotalExclTax = 100m,
        TotalTax = 6m,
        TotalInclTax = 106m,
        PayableAmount = 106m,
        Lines = new List<MyInvoiceLine>
        {
            new()
            {
                LineNumber = 1,
                Description = "Test Item",
                ClassificationCode = "022",
                Quantity = 1m,
                UnitOfMeasure = "EA",
                UnitPrice = 100m,
                LineTotalExclTax = 100m,
                TaxCode = "01",
                TaxRate = 6m,
                TaxAmount = 6m,
            }
        }
    };

    /// <summary>
    /// Deserialise the minified UBL JSON, navigate to the buyer's PostalAddress IdentificationCode.
    /// Path: Invoice[0].AccountingCustomerParty[0].Party[0].PostalAddress[0].Country[0].IdentificationCode[0]._
    /// </summary>
    private static string ExtractBuyerCountryCode(object ublGraph)
    {
        var json = UblDocumentBuilder.Minify(ublGraph);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("Invoice")[0]
            .GetProperty("AccountingCustomerParty")[0]
            .GetProperty("Party")[0]
            .GetProperty("PostalAddress")[0]
            .GetProperty("Country")[0]
            .GetProperty("IdentificationCode")[0]
            .GetProperty("_").GetString()!;
    }

    /// <summary>
    /// Path: Invoice[0].{party}[0].Party[0].PostalAddress[0].CountrySubentityCode[0]._
    /// This is the exact field LHDN rejected with CV302 on the first production AP batch.
    /// </summary>
    private static string ExtractStateCode(object ublGraph, string party)
    {
        var json = UblDocumentBuilder.Minify(ublGraph);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("Invoice")[0]
            .GetProperty(party)[0]
            .GetProperty("Party")[0]
            .GetProperty("PostalAddress")[0]
            .GetProperty("CountrySubentityCode")[0]
            .GetProperty("_").GetString()!;
    }

    private static string ExtractSupplierCountryCode(object ublGraph)
    {
        var json = UblDocumentBuilder.Minify(ublGraph);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("Invoice")[0]
            .GetProperty("AccountingSupplierParty")[0]
            .GetProperty("Party")[0]
            .GetProperty("PostalAddress")[0]
            .GetProperty("Country")[0]
            .GetProperty("IdentificationCode")[0]
            .GetProperty("_").GetString()!;
    }

    // -----------------------------------------------------------------------
    // Country code mapping — CV302 regression suite
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("MY",  "MYS")]   // standard alpha-2
    [InlineData("MYS", "MYS")]   // alpha-3 pass-through
    [InlineData("GB",  "GBR")]   // standard alpha-2 for UK
    [InlineData("GBR", "GBR")]   // alpha-3 pass-through
    [InlineData("UK",  "GBR")]   // non-standard M3 alias — CV302 root cause
    [InlineData("HK",  "HKG")]   // HK buyers (CIDMAS)
    [InlineData("SG",  "SGP")]
    [InlineData("AU",  "AUS")]
    [InlineData("US",  "USA")]
    [InlineData("CN",  "CHN")]
    public void BuildUnsigned_BuyerCountryCode_MapsToCorrectAlpha3(string input, string expected)
    {
        var doc = MinimalDoc(buyerCountry: input);
        var ubl = UblDocumentBuilder.BuildUnsigned(doc);
        ExtractBuyerCountryCode(ubl).Should().Be(expected);
    }

    [Fact]
    public void BuildUnsigned_BuyerUnmappedCountryCode_ThrowsRatherThanMislabellingCountry()
    {
        // ADR-020 — behaviour deliberately INVERTED.
        //
        // This test previously asserted that an unmapped code falls back to "MYS". That fallback
        // was the defect: a Belgian supplier (BE, absent from the old hand-maintained map) was
        // submitted to LHDN declared as Malaysian and rejected in the first production batch.
        //
        // Declaring the wrong country to a tax authority is worse than failing the invoice, so an
        // unresolvable code now throws. ProcessInvoice catches per-invoice exceptions, so the
        // invoice is marked Failed with an actionable message and the rest of the batch proceeds.
        var doc = MinimalDoc(buyerCountry: "ZZ");

        var act = () => UblDocumentBuilder.BuildUnsigned(doc);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*ZZ*not in LHDN's accepted country list*");
    }

    [Fact]
    public void BuildUnsigned_BuyerBelgium_ResolvesToBEL()
    {
        // The production incident: BE must resolve to BEL, never to MYS.
        var doc = MinimalDoc(buyerCountry: "BE");
        var ubl = UblDocumentBuilder.BuildUnsigned(doc);
        ExtractBuyerCountryCode(ubl).Should().Be("BEL");
    }

    [Fact]
    public void BuildUnsigned_SupplierBelgium_ResolvesToBEL()
    {
        // AP self-billed invoices carry the foreign supplier's country — the actual failure path.
        var doc = MinimalDoc(supplierCountry: "BE");
        var ubl = UblDocumentBuilder.BuildUnsigned(doc);
        ExtractSupplierCountryCode(ubl).Should().Be("BEL");
    }

    // ── CV302: CountrySubentityCode must be an LHDN State Code (ADR-021) ──────

    [Fact]
    public void BuildUnsigned_ForeignSupplier_StateCodeIs17NotNA()
    {
        // Production rejection on the first AP batch:
        //   CV302 "ItemCode NA does not exist in CodeType State Codes"
        //   path: $.Invoice[*].AccountingSupplierParty[*].Party[*].PostalAddress[*].CountrySubentityCode[*]._
        // LHDN publishes code 17 = "Not Applicable" for non-Malaysian addresses.
        var doc = MinimalDoc(supplierCountry: "BE");
        var ubl = UblDocumentBuilder.BuildUnsigned(doc);

        var state = ExtractStateCode(ubl, "AccountingSupplierParty");
        state.Should().Be("17");
        state.Should().NotBe("NA", "the literal string NA is what LHDN rejected");
        LhdnStateCodes.IsValid(state).Should().BeTrue();
    }

    [Fact]
    public void BuildUnsigned_ForeignBuyer_StateCodeIs17NotNA()
    {
        var doc = MinimalDoc(buyerCountry: "AU");
        var ubl = UblDocumentBuilder.BuildUnsigned(doc);

        ExtractStateCode(ubl, "AccountingCustomerParty").Should().Be("17");
    }

    [Theory]
    [InlineData("1",  "1")]     // Johor, already unpadded
    [InlineData("01", "1")]     // zero-padded config form is normalised
    [InlineData("10", "10")]    // Selangor — the value in successful UAT submissions
    [InlineData("14", "14")]    // WP Kuala Lumpur
    public void BuildUnsigned_MalaysianParty_UsesNormalisedStateCode(string configured, string expected)
    {
        var doc = MinimalDoc(supplierCountry: "MY");
        doc.SupplierStateCode = configured;

        var ubl = UblDocumentBuilder.BuildUnsigned(doc);

        ExtractStateCode(ubl, "AccountingSupplierParty").Should().Be(expected);
    }

    [Fact]
    public void BuildUnsigned_MalaysianPartyBlankState_UsesCode0NotDoubleZero()
    {
        // "00" is not a code LHDN publishes; "0" (All States) is.
        var doc = MinimalDoc(supplierCountry: "MY");
        doc.SupplierStateCode = null;

        var ubl = UblDocumentBuilder.BuildUnsigned(doc);

        ExtractStateCode(ubl, "AccountingSupplierParty").Should().Be("0");
    }

    [Fact]
    public void BuildUnsigned_MalaysianPartyInvalidState_ThrowsRatherThanGuessing()
    {
        var doc = MinimalDoc(supplierCountry: "MY");
        doc.SupplierStateCode = "99";

        var act = () => UblDocumentBuilder.BuildUnsigned(doc);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*99*not in LHDN's State Codes*");
    }

    [Fact]
    public void BuildUnsigned_EveryEmittedStateCode_IsAcceptedByLhdn()
    {
        // Whole-set invariant: whatever state code ends up in the document must exist in LHDN's
        // State Codes table, for every country we can resolve.
        foreach (var country in new[] { "MY", "BE", "AU", "SG", "US", "GB", "DE", "JP" })
        {
            var doc = MinimalDoc(supplierCountry: country, buyerCountry: country);
            var ubl = UblDocumentBuilder.BuildUnsigned(doc);

            foreach (var party in new[] { "AccountingSupplierParty", "AccountingCustomerParty" })
            {
                var state = ExtractStateCode(ubl, party);
                LhdnStateCodes.IsValid(state).Should()
                    .BeTrue($"{party} state '{state}' for country '{country}' must be an LHDN State Code");
            }
        }
    }

    [Fact]
    public void BuildUnsigned_BuyerNullCountryCode_FallsBackToMYS()
    {
        // A blank country is different from an unrecognised one: no country on file means our own
        // Malaysian entity, which is a safe and intentional default.
        var doc = MinimalDoc(buyerCountry: null);
        var ubl = UblDocumentBuilder.BuildUnsigned(doc);
        ExtractBuyerCountryCode(ubl).Should().Be("MYS");
    }

    [Fact]
    public void BuildUnsigned_SupplierCountryCode_MapsToCorrectAlpha3()
    {
        var doc = MinimalDoc(supplierCountry: "MY");
        var ubl = UblDocumentBuilder.BuildUnsigned(doc);
        ExtractSupplierCountryCode(ubl).Should().Be("MYS");
    }

    [Fact]
    public void BuildUnsigned_SupplierUkCountryCode_MapsToGBR()
    {
        var doc = MinimalDoc(supplierCountry: "UK");
        var ubl = UblDocumentBuilder.BuildUnsigned(doc);
        ExtractSupplierCountryCode(ubl).Should().Be("GBR");
    }

    // -----------------------------------------------------------------------
    // DS322 regression — JSON string escaping must match LHDN's serializer
    //
    // LHDN parse+reserializes our submitted document and re-computes the SHA-256
    // docDigest. System.Text.Json's DEFAULT encoder escapes &, +, ', <, > and all
    // non-ASCII as \uXXXX; LHDN's library emits them literally. That byte divergence
    // produced DS322 on any invoice whose text contained one of these characters —
    // and passed on plain-ASCII invoices, which is why failures looked patternless.
    // MinifyOptions must therefore use JavaScriptEncoder.UnsafeRelaxedJsonEscaping.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("SMITH & SONS SDN BHD", "&")]          // ampersand — company names
    [InlineData("O'BRIEN TRADING", "'")]               // apostrophe — names
    [InlineData("MÜLLER ELEKTRONIK", "Ü")]             // non-ASCII accent
    [InlineData("LOT 5–7, JALAN PERUSAHAAN", "–")]     // en-dash — addresses
    [InlineData("A <TEST> COMPANY", "<")]              // angle brackets
    public void Minify_SpecialCharactersInText_EmittedLiterallyNotUnicodeEscaped(
        string supplierName, string expectedLiteralChar)
    {
        var doc = MinimalDoc();
        doc.SupplierName = supplierName;

        var json = UblDocumentBuilder.Minify(UblDocumentBuilder.BuildUnsigned(doc));

        json.Should().Contain(expectedLiteralChar,
            "LHDN re-serializes with literal characters; escaping them breaks the docDigest (DS322)");
        json.Should().NotContain("\\u",
            "any \\uXXXX escape means our bytes diverge from LHDN's re-serialized bytes");
    }

    [Fact]
    public void Minify_PlusPrefixedPhoneNumber_NotEscaped()
    {
        // "+" is escaped to + by the default encoder — breaks digests on any
        // invoice carrying an international-format phone number.
        var doc = MinimalDoc();
        doc.SupplierPhone = "+6072319006";

        var json = UblDocumentBuilder.Minify(UblDocumentBuilder.BuildUnsigned(doc));

        json.Should().Contain("+6072319006");
        json.Should().NotContain("\\u002B");
    }

    [Fact]
    public void Minify_PlainAsciiDocument_ContainsNoEscapeSequences()
    {
        // Control case: plain-ASCII invoices always passed LHDN validation. This test
        // pins that they remain byte-stable after the encoder change.
        var json = UblDocumentBuilder.Minify(UblDocumentBuilder.BuildUnsigned(MinimalDoc()));

        json.Should().NotContain("\\u");
    }
}
