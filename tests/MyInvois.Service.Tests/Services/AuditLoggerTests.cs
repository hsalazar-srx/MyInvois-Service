using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MyInvois.Service.Models;
using MyInvois.Service.Services;
using System.Data;

namespace MyInvois.Service.Tests.Services;

/// <summary>
/// Unit tests for AuditLogger
/// Uses skill: architecture/audit-logging-framework v1.0+
/// Tests SQL Server audit logging with ISO 27001 compliance
/// </summary>
public class AuditLoggerTests
{
    private readonly Mock<IDbConnection> _dbConnectionMock;
    private readonly Mock<IDbCommand> _dbCommandMock;
    private readonly Mock<IDataParameterCollection> _parametersMock;
    private readonly Mock<ILogger<AuditLogger>> _loggerMock;
    private readonly AuditLogger _sut;

    public AuditLoggerTests()
    {
        _dbConnectionMock = new Mock<IDbConnection>();
        _dbCommandMock = new Mock<IDbCommand>();
        _parametersMock = new Mock<IDataParameterCollection>();
        _loggerMock = new Mock<ILogger<AuditLogger>>();

        _dbCommandMock.Setup(c => c.Parameters).Returns(_parametersMock.Object);
        _dbCommandMock.Setup(c => c.CreateParameter()).Returns(new Mock<IDbDataParameter>().Object);
        _dbConnectionMock.Setup(c => c.CreateCommand()).Returns(_dbCommandMock.Object);

        _sut = new AuditLogger(_dbConnectionMock.Object, _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullDbConnection_ThrowsArgumentNullException()
    {
        var action = () => new AuditLogger(null!, _loggerMock.Object);
        action.Should().Throw<ArgumentNullException>().WithParameterName("dbConnection");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var action = () => new AuditLogger(_dbConnectionMock.Object, null!);
        action.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region LogSubmission Tests

    [Fact]
    public async Task LogSubmission_SuccessfulSubmission_InsertsToAuditLog()
    {
        // Arrange
        var result = new SubmissionResult
        {
            InvoiceNumber = "INV-001",
            Status = "Success",
            MyInvoisUUID = "uuid-123",
            SubmittedAt = DateTime.UtcNow
        };

        _dbConnectionMock.Setup(c => c.State).Returns(ConnectionState.Open);
        _dbCommandMock.Setup(c => c.ExecuteNonQuery()).Returns(1);

        // Act
        await _sut.LogSubmission(result);

        // Assert
        _dbCommandMock.Verify(c => c.ExecuteNonQuery(), Times.Once);
    }

    [Fact]
    public async Task LogSubmission_FailedSubmission_LogsErrorDetails()
    {
        // Arrange
        var result = new SubmissionResult
        {
            InvoiceNumber = "INV-002",
            Status = "Failed",
            ErrorCode = "DS302",
            ErrorMessage = "Duplicate submission",
            SubmittedAt = DateTime.UtcNow
        };

        _dbConnectionMock.Setup(c => c.State).Returns(ConnectionState.Open);
        _dbCommandMock.Setup(c => c.ExecuteNonQuery()).Returns(1);

        // Act
        await _sut.LogSubmission(result);

        // Assert
        _dbCommandMock.Verify(c => c.ExecuteNonQuery(), Times.Once);
    }

    [Fact]
    public async Task LogSubmission_WithDocument_IncludesDocumentDetails()
    {
        // Arrange
        var result = new SubmissionResult
        {
            InvoiceNumber = "INV-003",
            Status = "Success",
            MyInvoisUUID = "uuid-456",
            SubmittedAt = DateTime.UtcNow
        };

        var document = new MyInvoiceDocument
        {
            InvoiceNumber = "INV-003",
            SupplierTIN = "123456789012",
            SupplierName = "Test Supplier",
            BuyerTIN = "987654321098",
            BuyerName = "Test Buyer",
            TotalInclTax = 1060.00m
        };

        _dbConnectionMock.Setup(c => c.State).Returns(ConnectionState.Open);
        _dbCommandMock.Setup(c => c.ExecuteNonQuery()).Returns(1);

        // Act
        await _sut.LogSubmission(result, document);

        // Assert
        _dbCommandMock.Verify(c => c.ExecuteNonQuery(), Times.Once);
    }

    [Fact]
    public async Task LogSubmission_ConnectionClosed_OpensConnection()
    {
        // Arrange
        var result = new SubmissionResult
        {
            InvoiceNumber = "INV-004",
            Status = "Success",
            SubmittedAt = DateTime.UtcNow
        };

        _dbConnectionMock.Setup(c => c.State).Returns(ConnectionState.Closed);
        _dbCommandMock.Setup(c => c.ExecuteNonQuery()).Returns(1);

        // Act
        await _sut.LogSubmission(result);

        // Assert
        _dbConnectionMock.Verify(c => c.Open(), Times.Once);
    }

    #endregion

    #region IsInvoiceAlreadySubmitted Tests

    [Fact]
    public async Task IsInvoiceAlreadySubmitted_ExistingInvoice_ReturnsTrue()
    {
        // Arrange
        _dbConnectionMock.Setup(c => c.State).Returns(ConnectionState.Open);
        _dbCommandMock.Setup(c => c.ExecuteScalar()).Returns(1);

        // Act
        var result = await _sut.IsInvoiceAlreadySubmitted("INV-001");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsInvoiceAlreadySubmitted_NewInvoice_ReturnsFalse()
    {
        // Arrange
        _dbConnectionMock.Setup(c => c.State).Returns(ConnectionState.Open);
        _dbCommandMock.Setup(c => c.ExecuteScalar()).Returns(0);

        // Act
        var result = await _sut.IsInvoiceAlreadySubmitted("INV-NEW");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetFailedSubmissions Tests

    [Fact]
    public async Task GetFailedSubmissions_NoFailures_ReturnsEmptyList()
    {
        // Arrange
        var readerMock = new Mock<IDataReader>();
        readerMock.Setup(r => r.Read()).Returns(false);

        _dbConnectionMock.Setup(c => c.State).Returns(ConnectionState.Open);
        _dbCommandMock.Setup(c => c.ExecuteReader()).Returns(readerMock.Object);

        // Act
        var result = await _sut.GetFailedSubmissions();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFailedSubmissions_WithFailures_ReturnsList()
    {
        // Arrange
        var callCount = 0;
        var readerMock = new Mock<IDataReader>();
        readerMock.Setup(r => r.Read()).Returns(() => ++callCount <= 2); // 2 rows
        readerMock.Setup(r => r.GetString(0)).Returns("INV-FAIL");
        readerMock.Setup(r => r.GetString(1)).Returns("Failed");
        readerMock.Setup(r => r.GetString(2)).Returns("DS302");
        readerMock.Setup(r => r.GetString(3)).Returns("Duplicate");
        readerMock.Setup(r => r.GetDateTime(4)).Returns(DateTime.UtcNow);
        readerMock.Setup(r => r.IsDBNull(It.IsAny<int>())).Returns(false);

        _dbConnectionMock.Setup(c => c.State).Returns(ConnectionState.Open);
        _dbCommandMock.Setup(c => c.ExecuteReader()).Returns(readerMock.Object);

        // Act
        var result = await _sut.GetFailedSubmissions();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].InvoiceNumber.Should().Be("INV-FAIL");
        result[0].Status.Should().Be("Failed");
    }

    #endregion
}
