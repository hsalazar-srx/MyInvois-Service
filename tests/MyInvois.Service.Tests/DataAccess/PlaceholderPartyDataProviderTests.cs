namespace MyInvois.Service.Tests.DataAccess;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MyInvois.Service.DataAccess;

/// <summary>
/// Unit tests for PlaceholderPartyDataProvider.
/// Verifies placeholder behavior: returns party ID as name, nulls for TIN/BRN/Address.
/// </summary>
public class PlaceholderPartyDataProviderTests
{
    private readonly PlaceholderPartyDataProvider _sut;

    public PlaceholderPartyDataProviderTests()
    {
        var loggerMock = new Mock<ILogger<PlaceholderPartyDataProvider>>();
        _sut = new PlaceholderPartyDataProvider(loggerMock.Object);
    }

    [Fact]
    public async Task GetSupplierDetailsAsync_WithValidId_ReturnsPlaceholderWithNullTin()
    {
        // Act
        var result = await _sut.GetSupplierDetailsAsync("SUP001");

        // Assert
        result.Should().NotBeNull();
        result!.PartyId.Should().Be("SUP001");
        result.Name.Should().Be("Supplier SUP001");
        result.TIN.Should().BeNull();
        result.BRN.Should().BeNull();
        result.Address.Should().BeNull();
    }

    [Fact]
    public async Task GetCustomerDetailsAsync_WithValidId_ReturnsPlaceholderWithNullTin()
    {
        // Act
        var result = await _sut.GetCustomerDetailsAsync("CUS001");

        // Assert
        result.Should().NotBeNull();
        result!.PartyId.Should().Be("CUS001");
        result.Name.Should().Be("Customer CUS001");
        result.TIN.Should().BeNull();
        result.BRN.Should().BeNull();
        result.Address.Should().BeNull();
    }

    [Fact]
    public async Task GetSupplierDetailsAsync_ReturnsIdSchemeBRN()
    {
        var result = await _sut.GetSupplierDetailsAsync("SUP002");

        result!.IdScheme.Should().Be("BRN");
    }
}
