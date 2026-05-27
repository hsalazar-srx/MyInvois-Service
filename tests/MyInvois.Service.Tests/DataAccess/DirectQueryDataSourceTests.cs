namespace MyInvois.Service.Tests.DataAccess;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MyInvois.Service.Configuration;
using MyInvois.Service.DataAccess;

// Test naming: [Method]_[Scenario]_[Expected] per ai/rules.md
// Framework: xUnit + FluentAssertions + Moq per WORKSPACE_RULES.md

/// <summary>
/// Unit tests for DirectQueryDataSource.
/// Tests constructor validation and argument contracts.
/// Actual DB2 queries tested in integration tests (requires DB2 connection).
/// </summary>
public class DirectQueryDataSourceTests
{
    private readonly Mock<ILogger<DirectQueryDataSource>> _loggerMock;
    private readonly MovexDbSettings _settings;

    public DirectQueryDataSourceTests()
    {
        _loggerMock = new Mock<ILogger<DirectQueryDataSource>>();
        _settings = new MovexDbSettings
        {
            ConnectionString = "Server=testhost;Database=MVXCDTA;",
            DataSourceStrategy = "DirectQuery",
            SchemaCmp100 = "mvxcdta",
            SchemaCmp300 = "mvxc300",
            ActiveCompanyCodes = new List<string> { "100", "300" },
            CommandTimeoutSeconds = 30,
            ArDivision = "L",
            ArTransCode = "10",
            ArCustomerStatus = "20",
            ArMinYear = 2025
        };
    }

    private MovexLineItemFetcher MakeFetcher() => new(
        Options.Create(_settings),
        new Mock<ILogger<MovexLineItemFetcher>>().Object);

    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DirectQueryDataSource(null!, _loggerMock.Object, MakeFetcher()));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DirectQueryDataSource(Options.Create(_settings), null!, MakeFetcher()));
    }

    [Fact]
    public void Constructor_WithNullLineItemFetcher_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DirectQueryDataSource(Options.Create(_settings), _loggerMock.Object, null!));
    }

    [Fact]
    public void Constructor_WithValidParams_CreatesInstance()
    {
        var sut = new DirectQueryDataSource(Options.Create(_settings), _loggerMock.Object, MakeFetcher());
        sut.Should().NotBeNull();
    }

    [Fact]
    public void RawInvoiceRecord_NewArProperties_HaveCorrectDefaults()
    {
        // Act
        var record = new RawInvoiceRecord();

        // Assert — AR-specific nullable properties default to null
        record.Division.Should().BeNull();
        record.TransCode.Should().BeNull();
        record.InvoiceEntryDate.Should().Be(0);
        record.ChangeVersion.Should().Be(0);
        record.CustomerStatus.Should().BeNull();
        record.CustomerName.Should().BeNull();
        record.MasterAddress1.Should().BeNull();
        record.MasterAddress2.Should().BeNull();
        record.MasterAddress3.Should().BeNull();
        record.MasterAddress4.Should().BeNull();
        record.InvoiceeName.Should().BeNull();
        record.Address1.Should().BeNull();
        record.Address2.Should().BeNull();
        record.Address3.Should().BeNull();
        record.Address4.Should().BeNull();
        record.PostCode.Should().BeNull();
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("")]
    [InlineData("Purchase")]
    public async Task GetInvoiceByIdAsync_WithInvalidType_ThrowsArgumentException(string invalidType)
    {
        var sut = new DirectQueryDataSource(Options.Create(_settings), _loggerMock.Object, MakeFetcher());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.GetInvoiceByIdAsync("INV001", invalidType));
    }
}
