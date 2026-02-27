using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MyInvois.Service.Configuration;
using MyInvois.Service.Validators;

namespace MyInvois.Service.Tests.Validators;
/// <summary>
/// Unit tests for TINValidator
/// Uses skill: integration/myinvois-validator v1.0+
/// Tests TIN (Tax Identification Number) format validation per MyInvois LHDNM spec
/// TIN Format: Exactly 12 digits, numeric only
/// </summary>
public class TINValidatorTests
{
    private readonly TINValidator _sut;
    private readonly Mock<IMemoryCache> _cacheMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<TINValidator>> _loggerMock;

    public TINValidatorTests()
    {
        _cacheMock = new Mock<IMemoryCache>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<TINValidator>>();

        var apiSettings = Options.Create(new MyInvoisApiSettings
        {
            BaseUrl = "https://sandbox.myinvois.hasil.gov.my",
            ClientId = "test",
            ClientSecret = "test"
        });

        _sut = new TINValidator(_cacheMock.Object, _httpClientFactoryMock.Object, apiSettings, _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_Succeeds()
    {
        // Arrange & Act
        var apiSettings = Options.Create(new MyInvoisApiSettings());
        var validator = new TINValidator(
            new Mock<IMemoryCache>().Object,
            new Mock<IHttpClientFactory>().Object,
            apiSettings,
            new Mock<ILogger<TINValidator>>().Object);

        // Assert
        validator.Should().NotBeNull();
    }

    #endregion

    #region ValidateFormat Tests - Valid TIN

    [Fact]
    public void ValidateFormat_WithValidTIN_ReturnsTrue()
    {
        // Arrange
        var tin = "123456789012";

        // Act
        var isValid = _sut.ValidateFormat(tin, out var error);

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("000000000000")]
    [InlineData("999999999999")]
    [InlineData("123456789012")]
    public void ValidateFormat_WithValid12DigitTIN_ReturnsTrue(string tin)
    {
        // Act
        var isValid = _sut.ValidateFormat(tin, out var error);

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    #endregion

    #region ValidateFormat Tests - Null/Empty TIN

    [Fact]
    public void ValidateFormat_WithNullTIN_Optional_ReturnsTrue()
    {
        // Arrange
        string? tin = null;

        // Act
        var isValid = _sut.ValidateFormat(tin, out var error, isRequired: false);

        // Assert
        isValid.Should().BeTrue("because TIN is optional for buyer");
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateFormat_WithEmptyTIN_Optional_ReturnsTrue()
    {
        // Arrange
        var tin = string.Empty;

        // Act
        var isValid = _sut.ValidateFormat(tin, out var error, isRequired: false);

        // Assert
        isValid.Should().BeTrue("because TIN is optional for buyer");
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateFormat_WithWhitespaceTIN_Optional_ReturnsTrue()
    {
        // Arrange
        var tin = "   ";

        // Act
        var isValid = _sut.ValidateFormat(tin, out var error, isRequired: false);

        // Assert
        isValid.Should().BeTrue("because TIN is optional for buyer");
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateFormat_WithNullTIN_Required_ReturnsFalse()
    {
        // Arrange
        string? tin = null;

        // Act
        var isValid = _sut.ValidateFormat(tin, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("TIN");
        error.Message.Should().Contain("required");
    }

    [Fact]
    public void ValidateFormat_WithEmptyTIN_Required_ReturnsFalse()
    {
        // Arrange
        var tin = string.Empty;

        // Act
        var isValid = _sut.ValidateFormat(tin, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("TIN");
        error.Message.Should().Contain("required");
    }

    #endregion

    #region ValidateFormat Tests - Invalid Length

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567890")]
    [InlineData("12345678901")]
    public void ValidateFormat_TINTooShort_ReturnsFalse(string tin)
    {
        // Act
        var isValid = _sut.ValidateFormat(tin, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("TIN");
        error.Message.Should().Contain("12 digits");
    }

    [Theory]
    [InlineData("1234567890123")]
    [InlineData("12345678901234567890")]
    public void ValidateFormat_TINTooLong_ReturnsFalse(string tin)
    {
        // Act
        var isValid = _sut.ValidateFormat(tin, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("TIN");
        error.Message.Should().Contain("12 digits");
    }

    #endregion

    #region ValidateFormat Tests - Non-Numeric

    [Theory]
    [InlineData("12345678901A")]
    [InlineData("A23456789012")]
    [InlineData("1234567890AB")]
    [InlineData("12345678-012")]
    public void ValidateFormat_NonNumericTIN_ReturnsFalse(string tin)
    {
        // Act
        var isValid = _sut.ValidateFormat(tin,  out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("TIN");
        error.Message.Should().Contain("numeric");
    }

    #endregion

    #region Error Properties Tests

    [Fact]
    public void ValidateFormat_InvalidTIN_ErrorHasCorrectProperties()
    {
        // Arrange
        var tin = "ABC";

        // Act
        var isValid = _sut.ValidateFormat(tin, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("TIN");
        error.Message.Should().NotBeNullOrWhiteSpace();
        error.Severity.Should().Be("Error");
        error.ViolatedRule.Should().NotBeNullOrWhiteSpace();
    }

    #endregion
}
