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

    // ── DeduplicateArRecords regression tests ────────────────────────────────
    // These protect against regressions in the M3 cash-receipt clearing dedup logic.
    // Real-world case: FSLEDG for invoice 009713132 had ESTRCD=10 (ESCUAM=-98.04)
    // and ESTRCD=20 (ESCUAM=+98.04) for the same ESCINO — the ESTRCD=20 row was
    // being submitted as a credit note to LHDN before this fix.
    //
    // ADR-019 UPDATE: the AR queries now filter ESTRCD='10' at source, so this method
    // is a no-op for DirectQueryDataSource in normal operation. It is retained as
    // defence-in-depth for the StoredProcedure data source and future callers, and
    // these tests continue to pin its behaviour.

    [Fact]
    public void DeduplicateArRecords_PairedInvoiceAndClearingEntry_RemovesClearingEntry()
    {
        // Invoice with cash-receipt allocation: ESTRCD=10 (ESCUAM<0) + ESTRCD=20 offset on same ESCINO.
        var records = new List<RawInvoiceRecord>
        {
            new() { InvoiceNo = "009713132", TransCode = "10", InvoiceAmount = -98.04m },
            new() { InvoiceNo = "009713132", TransCode = "20", InvoiceAmount =  98.04m },
        };

        var result = DirectQueryDataSource.DeduplicateArRecords(records);

        result.Should().HaveCount(1);
        result[0].TransCode.Should().Be("10");
        result[0].InvoiceAmount.Should().Be(-98.04m);
    }

    [Fact]
    public void DeduplicateArRecords_StandaloneTransCode20_IsPreserved()
    {
        // An ESTRCD=20 row with no matching ESTRCD=10 row is left alone by this method —
        // it only removes PAIRED rows. Note ADR-019 profiling found zero standalone
        // ESTRCD=20 rows in 2024–2026 across all divisions (UnpairedCount = 0), and the
        // AR queries now exclude ESTRCD=20 at source, so this input should not occur in
        // practice. Pinned to keep the method's contract narrow and predictable.
        var records = new List<RawInvoiceRecord>
        {
            new() { InvoiceNo = "CN001", TransCode = "20", InvoiceAmount = 500m },
        };

        var result = DirectQueryDataSource.DeduplicateArRecords(records);

        result.Should().HaveCount(1);
        result[0].TransCode.Should().Be("20");
    }

    [Fact]
    public void DeduplicateArRecords_MultipleInvoicesOneMixed_OnlyRemovesMatchingClearingEntry()
    {
        var records = new List<RawInvoiceRecord>
        {
            new() { InvoiceNo = "INV-A", TransCode = "10", InvoiceAmount =  1000m },
            new() { InvoiceNo = "INV-A", TransCode = "20", InvoiceAmount = -1000m }, // paired — must be removed
            new() { InvoiceNo = "INV-B", TransCode = "10", InvoiceAmount =   500m },
            new() { InvoiceNo = "CN-C",  TransCode = "20", InvoiceAmount =   200m }, // unpaired — not this method's job to remove
        };

        var result = DirectQueryDataSource.DeduplicateArRecords(records);

        result.Should().HaveCount(3);
        result.Should().ContainSingle(r => r.InvoiceNo == "INV-A" && r.TransCode == "10");
        result.Should().ContainSingle(r => r.InvoiceNo == "INV-B");
        result.Should().ContainSingle(r => r.InvoiceNo == "CN-C");
        result.Should().NotContain(r => r.InvoiceNo == "INV-A" && r.TransCode == "20");
    }

    [Fact]
    public void DeduplicateArRecords_EmptyList_ReturnsEmpty()
    {
        var result = DirectQueryDataSource.DeduplicateArRecords(new List<RawInvoiceRecord>());
        result.Should().BeEmpty();
    }
}
