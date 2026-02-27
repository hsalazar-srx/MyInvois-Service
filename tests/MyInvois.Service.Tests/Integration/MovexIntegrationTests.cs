using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MyInvois.Service.Configuration;
using MyInvois.Service.DataAccess;
using MyInvois.Service.Models;
using MyInvois.Service.Services;

namespace MyInvois.Service.Tests.Integration;

/// <summary>
/// Integration tests for MOVEX DB2 data access layer
/// Uses skill: integration/movex-db2-data-source v1.0+
/// Tests: IInvoiceDataSource → MovexInvoiceReader → enriched MovexInvoice DTOs
///
/// These tests validate the full data flow from raw DB2 records through party
/// enrichment to the final MovexInvoice DTOs consumed by InvoiceProcessor.
/// </summary>
[Trait("Category", "Integration")]
public class MovexIntegrationTests
{
    private readonly Mock<IInvoiceDataSource> _dataSourceMock;
    private readonly Mock<IPartyDataProvider> _partyProviderMock;
    private readonly Mock<ILogger<MovexInvoiceReader>> _loggerMock;
    private readonly MovexInvoiceReader _reader;

    public MovexIntegrationTests()
    {
        _dataSourceMock = new Mock<IInvoiceDataSource>();
        _partyProviderMock = new Mock<IPartyDataProvider>();
        _loggerMock = new Mock<ILogger<MovexInvoiceReader>>();

        _reader = new MovexInvoiceReader(
            _dataSourceMock.Object,
            _partyProviderMock.Object,
            Options.Create(new ForeignPartyDefaultsSettings()),
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetPendingInvoices_WithSalesAndPurchase_ReturnsBothTypes()
    {
        // Arrange - simulate DB2 returning mix of AP and AR records
        var records = new List<RawInvoiceRecord>
        {
            CreateRawRecord("INV-S-001", "AR"),
            CreateRawRecord("INV-S-002", "AR"),
            CreateRawRecord("INV-P-001", "AP"),
        };

        _dataSourceMock
            .Setup(ds => ds.GetPendingInvoicesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        SetupPartyProviderDefaults();

        // Act
        var result = await _reader.GetPendingInvoices(new DateTime(2026, 2, 1));

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result.Count(i => i.InvoiceType == "Sales").Should().Be(2);
        result.Count(i => i.InvoiceType == "Purchase").Should().Be(1);
    }

    [Fact]
    public async Task GetPendingInvoices_EnrichesPartyData_ForARRecords()
    {
        // Arrange - AR record should get customer details as Buyer
        var records = new List<RawInvoiceRecord>
        {
            CreateRawRecord("INV-001", "AR", partyId: "CUS-200"),
        };

        _dataSourceMock
            .Setup(ds => ds.GetPendingInvoicesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        _partyProviderMock
            .Setup(pp => pp.GetCustomerDetailsAsync("CUS-200", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId = "CUS-200",
                TIN = "C22222222222",
                Name = "Enriched Buyer Corp",
                BRN = "BRN-CUS-200",
                CountryCode = "MY"
            });

        // Act
        var result = await _reader.GetPendingInvoices(new DateTime(2026, 2, 1));

        // Assert
        result.Should().HaveCount(1);
        var invoice = result[0];
        invoice.InvoiceType.Should().Be("Sales");
        invoice.Buyer.Should().NotBeNull();
        invoice.Buyer!.Name.Should().Be("Enriched Buyer Corp");
        invoice.Buyer!.TIN.Should().Be("C22222222222");
    }

    [Fact]
    public async Task GetPendingInvoices_EnrichesPartyData_ForAPRecords()
    {
        // Arrange - AP record should get supplier details as Supplier
        var records = new List<RawInvoiceRecord>
        {
            CreateRawRecord("INV-P-001", "AP", partyId: "SUP-100"),
        };

        _dataSourceMock
            .Setup(ds => ds.GetPendingInvoicesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        _partyProviderMock
            .Setup(pp => pp.GetSupplierDetailsAsync("SUP-100", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId = "SUP-100",
                TIN = "C11111111111",
                Name = "Enriched Supplier Corp",
                BRN = "BRN-SUP-100",
                CountryCode = "MY"
            });

        // Act
        var result = await _reader.GetPendingInvoices(new DateTime(2026, 2, 1));

        // Assert
        result.Should().HaveCount(1);
        var invoice = result[0];
        invoice.InvoiceType.Should().Be("Purchase");
        invoice.Supplier.Should().NotBeNull();
        invoice.Supplier!.Name.Should().Be("Enriched Supplier Corp");
        invoice.Supplier!.TIN.Should().Be("C11111111111");
    }

    [Fact]
    public async Task GetInvoiceById_ValidId_ReturnsEnrichedInvoice()
    {
        // Arrange
        var record = CreateRawRecord("INV-SPECIFIC-001", "AR");

        // GetInvoiceById tries AP first, then AR
        _dataSourceMock
            .Setup(ds => ds.GetInvoiceByIdAsync("INV-SPECIFIC-001", "AP", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RawInvoiceRecord?)null);

        _dataSourceMock
            .Setup(ds => ds.GetInvoiceByIdAsync("INV-SPECIFIC-001", "AR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        SetupPartyProviderDefaults();

        // Act
        var result = await _reader.GetInvoiceById("INV-SPECIFIC-001");

        // Assert
        result.Should().NotBeNull();
        result!.InvoiceNumber.Should().Be("INV-SPECIFIC-001");
        result.InvoiceType.Should().Be("Sales");
    }

    [Fact]
    public async Task GetInvoiceById_NotFound_ReturnsNull()
    {
        // Arrange
        _dataSourceMock
            .Setup(ds => ds.GetInvoiceByIdAsync("NONEXISTENT", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RawInvoiceRecord?)null);

        // Act
        var result = await _reader.GetInvoiceById("NONEXISTENT");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetInvoicesByDateRange_ReturnsAllMatchingRecords()
    {
        // Arrange
        var records = new List<RawInvoiceRecord>
        {
            CreateRawRecord("INV-JAN-001", "AR"),
            CreateRawRecord("INV-JAN-002", "AP"),
        };

        _dataSourceMock
            .Setup(ds => ds.GetInvoicesByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        SetupPartyProviderDefaults();

        // Act
        var from = new DateTime(2026, 1, 1);
        var to = new DateTime(2026, 1, 31);
        var result = await _reader.GetInvoicesByDateRange(from, to);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);

        _dataSourceMock.Verify(
            ds => ds.GetInvoicesByDateRangeAsync(from, to, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPendingInvoices_CorrectlyMapsCurrencyAndAmounts()
    {
        // Arrange
        var records = new List<RawInvoiceRecord>
        {
            new()
            {
                InvoiceNo = "INV-FX-001",
                AccountingDate = 20260201,
                InvoiceType = "AR",
                CompanyCode = "100",
                PartyId = "CUS-001",
                Currency = "USD",
                FxRate = 4.25m,
                InvoiceAmount = 1060.00m,
                GstAmount = 60.00m,
                VoucherNumber = "V001"
            }
        };

        _dataSourceMock
            .Setup(ds => ds.GetPendingInvoicesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        SetupPartyProviderDefaults();

        // Act
        var result = await _reader.GetPendingInvoices(new DateTime(2026, 2, 1));

        // Assert
        var invoice = result[0];
        invoice.CurrencyCode.Should().Be("USD");
        invoice.ExchangeRate.Should().Be(4.25m);
        invoice.TotalInclTax.Should().Be(1060.00m);
        invoice.TotalTax.Should().Be(60.00m);
        invoice.TotalExclTax.Should().Be(1000.00m); // InvoiceAmount - GstAmount
    }

    #region Helpers

    private RawInvoiceRecord CreateRawRecord(string invoiceNumber, string invoiceType,
        string partyId = "PARTY-DEFAULT")
    {
        return new RawInvoiceRecord
        {
            InvoiceNo = invoiceNumber,
            AccountingDate = 20260201,
            InvoiceDate = 20260201,
            InvoiceType = invoiceType,
            CompanyCode = "100",
            PartyId = partyId,
            Currency = "MYR",
            FxRate = 1.0m,
            InvoiceAmount = 1060.00m,
            GstAmount = 60.00m,
            VoucherNumber = $"V-{invoiceNumber}"
        };
    }

    private void SetupPartyProviderDefaults()
    {
        _partyProviderMock
            .Setup(pp => pp.GetSupplierDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId = "DEFAULT-SUP",
                TIN = "C00000000000",
                Name = "Default Supplier",
                BRN = "BRN-DEFAULT"
            });

        _partyProviderMock
            .Setup(pp => pp.GetCustomerDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyDetails
            {
                PartyId = "DEFAULT-CUS",
                TIN = "C99999999999",
                Name = "Default Customer",
                BRN = "BRN-DEFAULT-C"
            });
    }

    #endregion
}
