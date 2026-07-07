
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

using Microsoft.Extensions.Logging;
using MyInvois.Service.Configuration;
using MyInvois.Service.Services;
using MyInvois.Service.DataAccess;


/// <summary>
/// Unit tests for MovexInvoiceReader (facade over IInvoiceDataSource + IPartyDataProvider).
/// Uses Moq to verify correct delegation and enrichment behavior.
/// </summary>
public class MovexInvoiceReaderTests
{
    private readonly Mock<IInvoiceDataSource> _dataSourceMock;
    private readonly Mock<IPartyDataProvider> _partyProviderMock;
    private readonly Mock<ILogger<MovexInvoiceReader>> _loggerMock;
    private readonly MovexInvoiceReader _sut;

    public MovexInvoiceReaderTests()
    {
        _dataSourceMock = new Mock<IInvoiceDataSource>();
        _partyProviderMock = new Mock<IPartyDataProvider>();
        _loggerMock = new Mock<ILogger<MovexInvoiceReader>>();
        _sut = new MovexInvoiceReader(_dataSourceMock.Object, _partyProviderMock.Object, Options.Create(new ForeignPartyDefaultsSettings()), _loggerMock.Object);
    }

    [Fact]
    public void Constructor_WithNullDataSource_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MovexInvoiceReader(null!, _partyProviderMock.Object, Options.Create(new ForeignPartyDefaultsSettings()), _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullPartyProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MovexInvoiceReader(_dataSourceMock.Object, null!, Options.Create(new ForeignPartyDefaultsSettings()), _loggerMock.Object));
    }

    [Fact]
    public async Task GetPendingInvoices_DelegatesToDataSource()
    {
        // Arrange
        var fromDate = new DateTime(2026, 1, 1);
        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>());

        // Act
        var result = await _sut.GetPendingInvoices(fromDate);

        // Assert
        _dataSourceMock.Verify(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingInvoices_WithApRecord_EnrichesSupplierParty()
    {
        // Arrange
        var fromDate = new DateTime(2026, 1, 1);
        var rawRecords = new List<RawInvoiceRecord>
        {
            new()
            {
                PartyId = "SUP001",
                InvoiceNo = "INV001",
                AccountingDate = 20260101,
                VoucherNumber = "V001",
                Currency = "MYR",
                FxRate = 1.0m,
                InvoiceAmount = 1100.00m,
                GstAmount = 100.00m,
                InvoiceType = "AP",
                CompanyCode = "100"
            }
        };

        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawRecords);

        _partyProviderMock
            .Setup(x => x.GetSupplierDetailsAsync("SUP001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId = "SUP001",
                Name = "Test Supplier",
                TIN = "123456789012",
                BRN = "BRN001",
                CountryCode = "MY"
            });

        // Act
        var result = await _sut.GetPendingInvoices(fromDate);

        // Assert
        result.Should().HaveCount(1);
        var invoice = result[0];
        invoice.InvoiceNumber.Should().Be("INV001");
        invoice.InvoiceType.Should().Be("Purchase");
        invoice.Supplier.Should().NotBeNull();
        invoice.Supplier!.Name.Should().Be("Test Supplier");
        invoice.Supplier.TIN.Should().Be("123456789012");
        invoice.CompanyCode.Should().Be("100");
        invoice.VoucherNumber.Should().Be("V001");
        invoice.TotalExclTax.Should().Be(1000.00m);
        invoice.TotalTax.Should().Be(100.00m);
        invoice.TotalInclTax.Should().Be(1100.00m);
    }

    [Fact]
    public async Task GetPendingInvoices_WithArRecord_EnrichesBuyerParty()
    {
        // Arrange
        var fromDate = new DateTime(2026, 1, 1);
        var rawRecords = new List<RawInvoiceRecord>
        {
            new()
            {
                PartyId = "CUS001",
                InvoiceNo = "SI001",
                AccountingDate = 20260115,
                InvoiceDate = 20260115,
                VoucherNumber = "V100",
                Currency = "USD",
                FxRate = 4.45m,
                InvoiceAmount = 500.00m,
                GstAmount = 0m,
                InvoiceType = "AR",
                CompanyCode = "300"
            }
        };

        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawRecords);

        _partyProviderMock
            .Setup(x => x.GetCustomerDetailsAsync("CUS001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails { PartyId = "CUS001", Name = "Test Customer" });

        // Act
        var result = await _sut.GetPendingInvoices(fromDate);

        // Assert
        result.Should().HaveCount(1);
        var invoice = result[0];
        invoice.InvoiceType.Should().Be("Sales");
        invoice.Buyer.Should().NotBeNull();
        invoice.Buyer!.Name.Should().Be("Test Customer");
        invoice.CurrencyCode.Should().Be("USD");
        invoice.ExchangeRate.Should().Be(4.45m);
    }

    [Fact]
    public async Task GetInvoiceById_WhenNotFound_ReturnsNull()
    {
        // Arrange
        _dataSourceMock
            .Setup(x => x.GetInvoiceByIdAsync("NOTFOUND", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RawInvoiceRecord?)null);

        // Act
        var result = await _sut.GetInvoiceById("NOTFOUND");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPendingInvoices_WithLineItems_MapsLinesToInvoice()
    {
        // Arrange
        var fromDate = new DateTime(2026, 1, 1);
        var rawRecords = new List<RawInvoiceRecord>
        {
            new()
            {
                PartyId = "CUS001",
                InvoiceNo = "INV-LINES-001",
                AccountingDate = 20260101,
                InvoiceDate = 20260101,
                VoucherNumber = "V001",
                Currency = "MYR",
                FxRate = 1.0m,
                InvoiceAmount = 1060.00m,
                GstAmount = 60.00m,
                InvoiceType = "AR",
                CompanyCode = "100",
                Lines = new List<RawInvoiceLineRecord>
                {
                    new()
                    {
                        LineNumber = 1,
                        ItemNumber = "ITEM-001",
                        Description = "Test Item A",
                        ClassificationCode = "022",
                        Quantity = 10,
                        UnitOfMeasure = "EA",
                        UnitPrice = 100.00m,
                        LineTotal = 1000.00m,
                        TaxCode = "01",
                        TaxRate = 6.0m,
                        TaxAmount = 60.00m
                    }
                }
            }
        };

        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawRecords);

        _partyProviderMock
            .Setup(x => x.GetCustomerDetailsAsync("CUS001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails { PartyId = "CUS001", Name = "Test Customer" });

        // Act
        var result = await _sut.GetPendingInvoices(fromDate);

        // Assert
        result.Should().HaveCount(1);
        var invoice = result[0];
        invoice.Lines.Should().HaveCount(1);

        var line = invoice.Lines[0];
        line.LineNumber.Should().Be(1);
        line.ItemNumber.Should().Be("ITEM-001");
        line.Description.Should().Be("Test Item A");
        line.ClassificationCode.Should().Be("022");
        line.Quantity.Should().Be(10);
        line.UnitOfMeasure.Should().Be("EA");
        line.UnitPrice.Should().Be(100.00m);
        line.LineTotal.Should().Be(1000.00m);
        line.TaxCode.Should().Be("01");
        line.TaxRate.Should().Be(6.0m);
        line.TaxAmount.Should().Be(60.00m);
    }

    [Fact]
    public async Task GetPendingInvoices_WithNoLineItems_GeneratesSyntheticLine()
    {
        // LHDN requires at least one InvoiceLine (TooFewItems). When MOVEX has no line detail
        // for a voucher, a synthetic line is generated from the header totals.
        var fromDate = new DateTime(2026, 1, 1);
        var rawRecords = new List<RawInvoiceRecord>
        {
            new()
            {
                PartyId = "CUS001",
                InvoiceNo = "INV-NOLINES",
                AccountingDate = 20260101,
                VoucherNumber = "V001",
                Currency = "MYR",
                InvoiceAmount = 100.00m,
                GstAmount = 6.00m,
                InvoiceType = "AR",
                CompanyCode = "100"
            }
        };

        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawRecords);

        _partyProviderMock
            .Setup(x => x.GetCustomerDetailsAsync("CUS001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails { PartyId = "CUS001", Name = "Test Customer" });

        // Act
        var result = await _sut.GetPendingInvoices(fromDate);

        // Assert
        result.Should().HaveCount(1);
        result[0].Lines.Should().HaveCount(1, "synthetic line must be generated when no lines exist");
        result[0].Lines[0].LineNumber.Should().Be(1);
        result[0].Lines[0].Quantity.Should().Be(1m);
        result[0].Lines[0].ClassificationCode.Should().Be("022");
        result[0].Lines[0].TaxAmount.Should().Be(6.00m);
    }

    [Fact]
    public async Task GetInvoicesByDateRange_DelegatesToDataSource()
    {
        // Arrange
        var from = new DateTime(2026, 1, 1);
        var to = new DateTime(2026, 1, 31);
        _dataSourceMock
            .Setup(x => x.GetInvoicesByDateRangeAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>());

        // Act
        var result = await _sut.GetInvoicesByDateRange(from, to);

        // Assert
        _dataSourceMock.Verify(x => x.GetInvoicesByDateRangeAsync(from, to, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingInvoices_ForeignSupplier_AppliesDefaultTIN()
    {
        // Arrange — foreign supplier (country != MY) should get EI TIN
        var fromDate = new DateTime(2026, 1, 1);
        var rawRecords = new List<RawInvoiceRecord>
        {
            new()
            {
                PartyId = "SUP-FOREIGN",
                InvoiceNo = "INV-IMP-001",
                AccountingDate = 20260101,
                VoucherNumber = "V001",
                Currency = "USD",
                FxRate = 4.45m,
                InvoiceAmount = 5000.00m,
                GstAmount = 0m,
                InvoiceType = "AP",
                CompanyCode = "100"
            }
        };

        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawRecords);

        _partyProviderMock
            .Setup(x => x.GetSupplierDetailsAsync("SUP-FOREIGN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId = "SUP-FOREIGN",
                Name = "Foreign Supplier Inc",
                CountryCode = "US"
            });

        // Act
        var result = await _sut.GetPendingInvoices(fromDate);

        // Assert
        result.Should().HaveCount(1);
        var invoice = result[0];
        invoice.Supplier.Should().NotBeNull();
        invoice.Supplier!.TIN.Should().Be("EI00000000030");
        invoice.Supplier.BRN.Should().Be("NA");
        invoice.Supplier.Name.Should().Be("Foreign Supplier Inc");
    }

    [Fact]
    public async Task GetPendingInvoices_ForeignBuyer_AppliesDefaultTIN()
    {
        // Arrange — foreign buyer (country != MY) should get EI TIN
        var fromDate = new DateTime(2026, 1, 1);
        var rawRecords = new List<RawInvoiceRecord>
        {
            new()
            {
                PartyId = "CUS-FOREIGN",
                InvoiceNo = "INV-EXP-001",
                AccountingDate = 20260101,
                InvoiceDate = 20260101,
                VoucherNumber = "V002",
                Currency = "SGD",
                FxRate = 3.30m,
                InvoiceAmount = 2000.00m,
                GstAmount = 0m,
                InvoiceType = "AR",
                CompanyCode = "300"
            }
        };

        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawRecords);

        _partyProviderMock
            .Setup(x => x.GetCustomerDetailsAsync("CUS-FOREIGN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId = "CUS-FOREIGN",
                Name = "Singapore Buyer Pte Ltd",
                CountryCode = "SG"
            });

        // Act
        var result = await _sut.GetPendingInvoices(fromDate);

        // Assert
        result.Should().HaveCount(1);
        var invoice = result[0];
        invoice.Buyer.Should().NotBeNull();
        invoice.Buyer!.TIN.Should().Be("EI00000000020");
        invoice.Buyer.BRN.Should().Be("NA");
        invoice.Buyer.Name.Should().Be("Singapore Buyer Pte Ltd");
    }

    [Fact]
    public async Task GetPendingInvoices_DomesticSupplier_UsesMovexTIN()
    {
        // Arrange — domestic supplier (MY) should keep MOVEX TIN/BRN
        var fromDate = new DateTime(2026, 1, 1);
        var rawRecords = new List<RawInvoiceRecord>
        {
            new()
            {
                PartyId = "SUP-MY",
                InvoiceNo = "INV-DOM-001",
                AccountingDate = 20260101,
                VoucherNumber = "V003",
                Currency = "MYR",
                FxRate = 1.0m,
                InvoiceAmount = 1060.00m,
                GstAmount = 60.00m,
                InvoiceType = "AP",
                CompanyCode = "100"
            }
        };

        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawRecords);

        _partyProviderMock
            .Setup(x => x.GetSupplierDetailsAsync("SUP-MY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId = "SUP-MY",
                Name = "Local Supplier Sdn Bhd",
                TIN = "C55555555555",
                BRN = "202001012345",
                CountryCode = "MY"
            });

        // Act
        var result = await _sut.GetPendingInvoices(fromDate);

        // Assert
        result.Should().HaveCount(1);
        var invoice = result[0];
        invoice.Supplier.Should().NotBeNull();
        invoice.Supplier!.TIN.Should().Be("C55555555555");
        invoice.Supplier.BRN.Should().Be("202001012345");
    }

    #region FX rate normalisation

    [Theory]
    [InlineData("MYR", "0.0")]    // FPLEDG.EPARAT = 0 — functional currency, no rate stored
    [InlineData("MYR", "0.25")]   // stale cross-rate from a previous foreign voucher on same supplier
    [InlineData("MYR", "4.45")]   // any non-1.0 rate for MYR is invalid — always override to 1.0
    public async Task GetPendingInvoices_MyrInvoiceWithAnyFxRate_NormalisesToOne(string currency, string rawRateStr)
    {
        var rawRate = decimal.Parse(rawRateStr);
        var fromDate = new DateTime(2026, 1, 1);
        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>
            {
                new()
                {
                    PartyId = "SUP001", InvoiceNo = "MYR-RATE-TEST",
                    AccountingDate = 20260101, VoucherNumber = "V001",
                    Currency = currency, FxRate = rawRate,
                    InvoiceAmount = 1060m, GstAmount = 60m,
                    InvoiceType = "AP", CompanyCode = "100"
                }
            });
        _partyProviderMock
            .Setup(x => x.GetSupplierDetailsAsync("SUP001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails { PartyId = "SUP001", Name = "Supplier", CountryCode = "MY" });

        var result = await _sut.GetPendingInvoices(fromDate);

        result[0].ExchangeRate.Should().Be(1.0m);
    }

    [Fact]
    public async Task GetPendingInvoices_ForeignInvoiceWithZeroFxRate_NormalisesToOne()
    {
        // FPLEDG.EPARAT = 0 for a USD invoice means the rate was never recorded.
        // Normalise to 1.0 so the document is structurally valid; CurrencyValidator will flag it.
        var fromDate = new DateTime(2026, 1, 1);
        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>
            {
                new()
                {
                    PartyId = "SUP001", InvoiceNo = "USD-ZERO-RATE",
                    AccountingDate = 20260101, VoucherNumber = "V001",
                    Currency = "USD", FxRate = 0m,
                    InvoiceAmount = 1000m, GstAmount = 0m,
                    InvoiceType = "AP", CompanyCode = "100"
                }
            });
        _partyProviderMock
            .Setup(x => x.GetSupplierDetailsAsync("SUP001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails { PartyId = "SUP001", Name = "Supplier", CountryCode = "SG" });

        var result = await _sut.GetPendingInvoices(fromDate);

        result[0].ExchangeRate.Should().Be(1.0m);
    }

    #endregion

    #region Zero-quantity line filtering

    [Fact]
    public async Task GetPendingInvoices_WithZeroQuantityLine_SkipsZeroQtyLine()
    {
        // ODLINE.UBIVQT = 0 for free-of-charge / service lines. LHDN rejects Quantity <= 0.
        var fromDate = new DateTime(2026, 1, 1);
        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>
            {
                new()
                {
                    PartyId = "CUST001", InvoiceNo = "ZERO-QTY-001",
                    AccountingDate = 20260101, InvoiceDate = 20260101,
                    VoucherNumber = "V001", Currency = "USD", FxRate = 4.45m,
                    InvoiceAmount = 1000m, GstAmount = 0m,
                    InvoiceType = "AR", CompanyCode = "100",
                    Lines = new List<RawInvoiceLineRecord>
                    {
                        new() { LineNumber = 1, Description = "Normal line", ClassificationCode = "022",
                                Quantity = 5m, UnitOfMeasure = "EA", UnitPrice = 200m, LineTotal = 1000m,
                                TaxCode = "", TaxRate = 0m, TaxAmount = 0m },
                        new() { LineNumber = 2, Description = "Zero qty line", ClassificationCode = "022",
                                Quantity = 0m, UnitOfMeasure = "EA", UnitPrice = 0m, LineTotal = 0m,
                                TaxCode = "", TaxRate = 0m, TaxAmount = 0m }
                    }
                }
            });
        _partyProviderMock
            .Setup(x => x.GetCustomerDetailsAsync("CUST001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails { PartyId = "CUST001", Name = "Customer", CountryCode = "SG" });

        var result = await _sut.GetPendingInvoices(fromDate);

        result[0].Lines.Should().HaveCount(1);
        result[0].Lines[0].Quantity.Should().Be(5m);
    }

    [Fact]
    public async Task GetPendingInvoices_AllLinesZeroQuantity_GeneratesSyntheticLine()
    {
        // When all lines have zero quantity they are all skipped → synthetic line fallback.
        var fromDate = new DateTime(2026, 1, 1);
        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>
            {
                new()
                {
                    PartyId = "CUST001", InvoiceNo = "ALL-ZERO-QTY",
                    AccountingDate = 20260101, InvoiceDate = 20260101,
                    VoucherNumber = "V001", Currency = "USD", FxRate = 4.45m,
                    InvoiceAmount = 114284.72m, GstAmount = 0m,
                    InvoiceType = "AR", CompanyCode = "100",
                    Lines = new List<RawInvoiceLineRecord>
                    {
                        new() { LineNumber = 1, Description = "Zero qty", ClassificationCode = "022",
                                Quantity = 0m, UnitOfMeasure = "EA", UnitPrice = 0m, LineTotal = 0m,
                                TaxCode = "", TaxRate = 0m, TaxAmount = 0m }
                    }
                }
            });
        _partyProviderMock
            .Setup(x => x.GetCustomerDetailsAsync("CUST001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails { PartyId = "CUST001", Name = "Customer", CountryCode = "SG" });

        var result = await _sut.GetPendingInvoices(fromDate);

        // Should have exactly one synthetic line with Quantity = 1
        result[0].Lines.Should().HaveCount(1);
        result[0].Lines[0].Quantity.Should().Be(1m);
        result[0].Lines[0].Description.Should().Be("Invoice");
        result[0].Lines[0].ClassificationCode.Should().Be("022");
    }

    #endregion

    [Fact]
    public async Task GetPendingInvoices_NullCountryCode_TreatedAsForeign()
    {
        // Arrange — null country code should be treated as foreign
        var fromDate = new DateTime(2026, 1, 1);
        var rawRecords = new List<RawInvoiceRecord>
        {
            new()
            {
                PartyId = "SUP-UNKNOWN",
                InvoiceNo = "INV-UNK-001",
                AccountingDate = 20260101,
                VoucherNumber = "V004",
                Currency = "MYR",
                InvoiceAmount = 500.00m,
                GstAmount = 0m,
                InvoiceType = "AP",
                CompanyCode = "100"
            }
        };

        _dataSourceMock
            .Setup(x => x.GetPendingInvoicesAsync(fromDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawRecords);

        _partyProviderMock
            .Setup(x => x.GetSupplierDetailsAsync("SUP-UNKNOWN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId = "SUP-UNKNOWN",
                Name = "Unknown Country Supplier",
                CountryCode = null
            });

        // Act
        var result = await _sut.GetPendingInvoices(fromDate);

        // Assert
        result.Should().HaveCount(1);
        var invoice = result[0];
        invoice.Supplier.Should().NotBeNull();
        invoice.Supplier!.TIN.Should().Be("EI00000000030");
        invoice.Supplier.BRN.Should().Be("NA");
    }
}
