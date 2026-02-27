using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MyInvois.Service.Models;
using MyInvois.Service.Services;
using System.Data;

namespace MyInvois.Service.Tests.Integration;

/// <summary>
/// Integration tests for AuditLogger SQL Server persistence
/// Uses skill: architecture/audit-logging-framework v1.0+ (ISO 27001 compliant)
///
/// Tests the full audit logging lifecycle:
/// Insert → Query → Duplicate detection → Failed submissions query
///
/// Uses in-memory IDbConnection mock to simulate SQL Server behavior.
/// For true database integration, use [Trait("Category", "Database")] against staging.
/// </summary>
[Trait("Category", "Integration")]
public class AuditLoggerIntegrationTests
{
    private readonly Mock<IDbConnection> _dbConnectionMock;
    private readonly Mock<IDbCommand> _dbCommandMock;
    private readonly Mock<IDataParameterCollection> _parametersMock;
    private readonly Mock<ILogger<AuditLogger>> _loggerMock;
    private readonly AuditLogger _auditLogger;

    public AuditLoggerIntegrationTests()
    {
        _dbConnectionMock = new Mock<IDbConnection>();
        _dbCommandMock = new Mock<IDbCommand>();
        _parametersMock = new Mock<IDataParameterCollection>();
        _loggerMock = new Mock<ILogger<AuditLogger>>();

        _dbCommandMock.Setup(c => c.Parameters).Returns(_parametersMock.Object);
        _dbCommandMock.Setup(c => c.CreateParameter()).Returns(new Mock<IDbDataParameter>().Object);
        _dbConnectionMock.Setup(c => c.CreateCommand()).Returns(_dbCommandMock.Object);
        _dbConnectionMock.Setup(c => c.State).Returns(ConnectionState.Open);

        _auditLogger = new AuditLogger(_dbConnectionMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task LogSubmission_Success_ThenQueryDuplicate_ReturnsTrue()
    {
        // Arrange - Log a successful submission
        var result = TestDataFactory.CreateSuccessResult("INV-DUP-CHECK");
        var document = TestDataFactory.CreateValidDocument("INV-DUP-CHECK");

        _dbCommandMock.Setup(c => c.ExecuteNonQuery()).Returns(1);

        // Act - Log the submission
        await _auditLogger.LogSubmission(result, document);

        // Assert - ExecuteNonQuery was called (insert happened)
        _dbCommandMock.Verify(c => c.ExecuteNonQuery(), Times.Once);

        // Setup - Now check duplicate detection returns true
        _dbCommandMock.Setup(c => c.ExecuteScalar()).Returns(1);

        // Act - Check if already submitted
        var isDuplicate = await _auditLogger.IsInvoiceAlreadySubmitted("INV-DUP-CHECK");

        // Assert
        isDuplicate.Should().BeTrue();
    }

    [Fact]
    public async Task LogSubmission_Failed_ThenQueryFailed_ReturnsList()
    {
        // Arrange - Log a failed submission
        var result = TestDataFactory.CreateFailedResult("INV-FAIL-001", "DS302", "Duplicate submission");
        _dbCommandMock.Setup(c => c.ExecuteNonQuery()).Returns(1);

        await _auditLogger.LogSubmission(result);

        // Now query failed submissions
        var callCount = 0;
        var readerMock = new Mock<IDataReader>();
        readerMock.Setup(r => r.Read()).Returns(() => ++callCount <= 1); // 1 row
        readerMock.Setup(r => r.GetString(0)).Returns("INV-FAIL-001");
        readerMock.Setup(r => r.GetString(1)).Returns("Failed");
        readerMock.Setup(r => r.GetString(2)).Returns("DS302");
        readerMock.Setup(r => r.GetString(3)).Returns("Duplicate submission");
        readerMock.Setup(r => r.GetDateTime(4)).Returns(DateTime.UtcNow);
        readerMock.Setup(r => r.IsDBNull(It.IsAny<int>())).Returns(false);

        _dbCommandMock.Setup(c => c.ExecuteReader()).Returns(readerMock.Object);

        // Act
        var failedList = await _auditLogger.GetFailedSubmissions();

        // Assert
        failedList.Should().NotBeNull();
        failedList.Should().HaveCount(1);
        failedList[0].InvoiceNumber.Should().Be("INV-FAIL-001");
        failedList[0].ErrorCode.Should().Be("DS302");
    }

    [Fact]
    public async Task LogSubmission_WithDocumentDetails_IncludesAllParameters()
    {
        // Arrange
        var result = TestDataFactory.CreateSuccessResult("INV-DETAIL-001");
        var document = TestDataFactory.CreateValidDocument("INV-DETAIL-001");

        _dbCommandMock.Setup(c => c.ExecuteNonQuery()).Returns(1);

        // Act
        await _auditLogger.LogSubmission(result, document);

        // Assert - verify parameters were added (13 fields per insert)
        _parametersMock.Verify(
            p => p.Add(It.IsAny<IDbDataParameter>()),
            Times.Exactly(13));
    }

    [Fact]
    public async Task IsInvoiceAlreadySubmitted_NewInvoice_ReturnsFalse()
    {
        // Arrange - No prior submissions
        _dbCommandMock.Setup(c => c.ExecuteScalar()).Returns(0);

        // Act
        var isDuplicate = await _auditLogger.IsInvoiceAlreadySubmitted("INV-BRAND-NEW");

        // Assert
        isDuplicate.Should().BeFalse();
    }

    [Fact]
    public async Task LogSubmission_ConnectionClosed_OpensConnectionBeforeInsert()
    {
        // Arrange
        _dbConnectionMock.Setup(c => c.State).Returns(ConnectionState.Closed);
        _dbCommandMock.Setup(c => c.ExecuteNonQuery()).Returns(1);

        var result = TestDataFactory.CreateSuccessResult("INV-CONN-001");

        // Act
        await _auditLogger.LogSubmission(result);

        // Assert - Connection was opened
        _dbConnectionMock.Verify(c => c.Open(), Times.Once);
        _dbCommandMock.Verify(c => c.ExecuteNonQuery(), Times.Once);
    }
}
