using FluentAssertions;
using MyInvois.Service.Validators;

namespace MyInvois.Service.Tests.Validators;
/// <summary>
/// Unit tests for CurrencyValidator
/// Uses skill: integration/myinvois-validator v1.0+
/// Tests currency code (ISO 4217) and exchange rate validation per MyInvois LHDNM spec
/// </summary>
public class CurrencyValidatorTests
{
    private readonly CurrencyValidator _sut;

    public CurrencyValidatorTests()
    {
        _sut = new CurrencyValidator();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_Succeeds()
    {
        // Arrange & Act
        var validator = new CurrencyValidator();

        // Assert
        validator.Should().NotBeNull();
    }

    #endregion

    #region ValidateCurrencyCode Tests - Valid Codes

    [Theory]
    [InlineData("MYR")]
    [InlineData("USD")]
    [InlineData("SGD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    public void ValidateCurrencyCode_WithValidCode_ReturnsTrue(string code)
    {
        // Act
        var isValid = _sut.ValidateCurrencyCode(code, out var error);

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    #endregion

    #region ValidateCurrencyCode Tests - Invalid Codes

    [Fact]
    public void ValidateCurrencyCode_WithNull_ReturnsFalse()
    {
        // Arrange
        string? code = null;

        // Act
        var isValid = _sut.ValidateCurrencyCode(code, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("CurrencyCode");
        error.Message.Should().Contain("required");
    }

    [Fact]
    public void ValidateCurrencyCode_WithEmpty_ReturnsFalse()
    {
        // Arrange
        var code = string.Empty;

        // Act
        var isValid = _sut.ValidateCurrencyCode(code, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("CurrencyCode");
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("XYZ")]
    [InlineData("ZZZ")]
    public void ValidateCurrencyCode_WithUnsupportedCode_ReturnsFalse(string code)
    {
        // Act
        var isValid = _sut.ValidateCurrencyCode(code, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("CurrencyCode");
        error.Message.Should().Contain("not supported");
    }

    [Theory]
    [InlineData("MY")]
    [InlineData("MYRI")]
    public void ValidateCurrencyCode_WithInvalidLength_ReturnsFalse(string code)
    {
        // Act
        var isValid = _sut.ValidateCurrencyCode(code, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("CurrencyCode");
        error.Message.Should().Contain("3 characters");
    }

    #endregion

    #region ValidateExchangeRate Tests - MYR

    [Fact]
    public void ValidateExchangeRate_MYR_WithRate1_ReturnsTrue()
    {
        // Arrange
        var rate = 1.0m;
        var currencyCode = "MYR";

        // Act
        var isValid = _sut.ValidateExchangeRate(rate, currencyCode, out var error);

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateExchangeRate_MYR_WithRate0_ReturnsTrue()
    {
        // Arrange
        var rate = 0m;
        var currencyCode = "MYR";

        // Act
        var isValid = _sut.ValidateExchangeRate(rate, currencyCode, out var error);

        // Assert
        isValid.Should().BeTrue("because MYR rate can be 0 (meaning not set) or 1.0");
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateExchangeRate_MYR_WithRateNot1_ReturnsFalse()
    {
        // Arrange
        var rate = 4.5m;
        var currencyCode = "MYR";

        // Act
        var isValid = _sut.ValidateExchangeRate(rate, currencyCode, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("ExchangeRate");
        error.Message.Should().Contain("MYR").And.Contain("1.0");
    }

    #endregion

    #region ValidateExchangeRate Tests - Non-MYR

    [Theory]
    [InlineData("USD", 4.45)]
    [InlineData("SGD", 3.30)]
    [InlineData("EUR", 4.85)]
    public void ValidateExchangeRate_NonMYR_WithValidRate_ReturnsTrue(string currencyCode, decimal rate)
    {
        // Act
        var isValid = _sut.ValidateExchangeRate(rate, currencyCode, out var error);

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("SGD")]
    [InlineData("EUR")]
    public void ValidateExchangeRate_NonMYR_WithZeroRate_ReturnsFalse(string currencyCode)
    {
        // Arrange
        var rate = 0m;

        // Act
        var isValid = _sut.ValidateExchangeRate(rate, currencyCode, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("ExchangeRate");
        (error.Message.Contains("required") || error.Message.Contains("greater than 0")).Should().BeTrue();
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("SGD")]
    public void ValidateExchangeRate_NonMYR_WithNegativeRate_ReturnsFalse(string currencyCode)
    {
        // Arrange
        var rate = -1.5m;

        // Act
        var isValid = _sut.ValidateExchangeRate(rate, currencyCode, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("ExchangeRate");
        error.Message.Should().Contain("greater than 0");
    }

    #endregion

    #region ValidateExchangeRate Tests - Decimal Precision

    [Fact]
    public void ValidateExchangeRate_With6DecimalPlaces_ReturnsTrue()
    {
        // Arrange
        var rate = 4.456789m;
        var currencyCode = "USD";

        // Act
        var isValid = _sut.ValidateExchangeRate(rate, currencyCode, out var error);

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateExchangeRate_WithMoreThan6DecimalPlaces_ReturnsFalse()
    {
        // Arrange
        var rate = 4.4567891m; // 7 decimal places
        var currencyCode = "USD";

        // Act
        var isValid = _sut.ValidateExchangeRate(rate, currencyCode, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("ExchangeRate");
        error.Message.Should().Contain("6 decimal places");
    }

    #endregion

    #region Error Properties Tests

    [Fact]
    public void ValidateCurrencyCode_InvalidCode_ErrorHasCorrectProperties()
    {
        // Arrange
        var code = "ABC";

        // Act
        var isValid = _sut.ValidateCurrencyCode(code, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("CurrencyCode");
        error.Message.Should().NotBeNullOrWhiteSpace();
        error.Severity.Should().Be("Error");
        error.ViolatedRule.Should().NotBeNullOrWhiteSpace();
    }

    #endregion
}
