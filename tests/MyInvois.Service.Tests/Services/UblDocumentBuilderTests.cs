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
    public void BuildUnsigned_BuyerUnmappedCountryCode_FallsBackToMYS()
    {
        // "OTH" was the old fallback — LHDN rejects it with CV302.
        // Any unmapped code now falls back to "MYS".
        var doc = MinimalDoc(buyerCountry: "ZZ");
        var ubl = UblDocumentBuilder.BuildUnsigned(doc);
        ExtractBuyerCountryCode(ubl).Should().Be("MYS");
    }

    [Fact]
    public void BuildUnsigned_BuyerNullCountryCode_FallsBackToMYS()
    {
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
}
