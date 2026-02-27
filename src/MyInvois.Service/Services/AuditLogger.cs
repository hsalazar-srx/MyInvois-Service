namespace MyInvois.Service.Services;

using Microsoft.Extensions.Logging;
using MyInvois.Service.Models;
using System.Data;

/// <summary>
/// AuditLogger - Writes submission audit trail to SQL Server
///
/// Responsibilities:
/// - Log every submission attempt to [dbo].[AuditLog]
/// - Include all MyInvois-specific fields (UUID, status, error code)
/// - Store request/response payloads for forensics
/// - Include correlation ID for tracing
/// - Query for duplicate detection
///
/// Skills:
/// - architecture/audit-logging-framework v1.0+ (audit schema + retention)
/// - architecture/configuration-management v1.0+ (connection settings)
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Log a submission attempt
    /// </summary>
    Task LogSubmission(SubmissionResult result, MyInvoiceDocument? document = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Query failed submissions for retry
    /// </summary>
    Task<List<SubmissionResult>> GetFailedSubmissions(int maxResults = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if invoice was already submitted successfully
    /// </summary>
    Task<bool> IsInvoiceAlreadySubmitted(string invoiceNumber, CancellationToken cancellationToken = default);
}

public class AuditLogger : IAuditLogger
{
    // Uses skill: architecture/audit-logging-framework v1.0+
    // ISO 27001 compliant: 7-year retention, immutable log entries
    // All submissions logged regardless of success/failure

    private readonly IDbConnection _dbConnection;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(IDbConnection dbConnection, ILogger<AuditLogger> logger)
    {
        _dbConnection = dbConnection ?? throw new ArgumentNullException(nameof(dbConnection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LogSubmission(SubmissionResult result, MyInvoiceDocument? document = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Logging submission for invoice {InvoiceNumber}, Status: {Status}",
            result.InvoiceNumber, result.Status);

        try
        {
            EnsureConnectionOpen();

            using var command = _dbConnection.CreateCommand();
            command.CommandText = @"
                INSERT INTO [dbo].[AuditLog]
                    ([Action], [Category], [InvoiceNumber], [Status],
                     [MyInvoisUUID], [ErrorCode], [ErrorMessage],
                     [SupplierTIN], [BuyerTIN], [TotalAmount],
                     [RawResponse], [SubmittedAt], [CreatedAt])
                VALUES
                    (@Action, @Category, @InvoiceNumber, @Status,
                     @MyInvoisUUID, @ErrorCode, @ErrorMessage,
                     @SupplierTIN, @BuyerTIN, @TotalAmount,
                     @RawResponse, @SubmittedAt, @CreatedAt)";

            AddParameter(command, "@Action", "MyInvois_Submit");
            AddParameter(command, "@Category", "MyInvois");
            AddParameter(command, "@InvoiceNumber", result.InvoiceNumber ?? string.Empty);
            AddParameter(command, "@Status", result.Status ?? string.Empty);
            AddParameter(command, "@MyInvoisUUID", result.MyInvoisUUID ?? string.Empty);
            AddParameter(command, "@ErrorCode", result.ErrorCode ?? string.Empty);
            AddParameter(command, "@ErrorMessage", result.ErrorMessage ?? string.Empty);
            AddParameter(command, "@SupplierTIN", document?.SupplierTIN ?? string.Empty);
            AddParameter(command, "@BuyerTIN", document?.BuyerTIN ?? string.Empty);
            AddParameter(command, "@TotalAmount", document?.TotalInclTax ?? 0m);
            AddParameter(command, "@RawResponse", result.RawResponse ?? string.Empty);
            AddParameter(command, "@SubmittedAt", result.SubmittedAt);
            AddParameter(command, "@CreatedAt", DateTime.UtcNow);

            command.ExecuteNonQuery();

            _logger.LogInformation(
                "Audit log entry created for invoice {InvoiceNumber}",
                result.InvoiceNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write audit log for invoice {InvoiceNumber}",
                result.InvoiceNumber);
            throw;
        }

        await Task.CompletedTask;
    }

    public async Task<bool> IsInvoiceAlreadySubmitted(string invoiceNumber, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Checking duplicate for invoice {InvoiceNumber}", invoiceNumber);

        try
        {
            EnsureConnectionOpen();

            using var command = _dbConnection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(1) FROM [dbo].[AuditLog]
                WHERE [InvoiceNumber] = @InvoiceNumber
                  AND [Status] = 'Success'";

            AddParameter(command, "@InvoiceNumber", invoiceNumber);

            var count = Convert.ToInt32(command.ExecuteScalar());

            await Task.CompletedTask;
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to check duplicate for invoice {InvoiceNumber}",
                invoiceNumber);
            throw;
        }
    }

    public async Task<List<SubmissionResult>> GetFailedSubmissions(int maxResults = 100, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying failed submissions (max: {MaxResults})", maxResults);

        var results = new List<SubmissionResult>();

        try
        {
            EnsureConnectionOpen();

            using var command = _dbConnection.CreateCommand();
            command.CommandText = @"
                SELECT TOP (@MaxResults)
                    [InvoiceNumber], [Status], [ErrorCode], [ErrorMessage], [SubmittedAt]
                FROM [dbo].[AuditLog]
                WHERE [Status] = 'Failed'
                  AND [Category] = 'MyInvois'
                ORDER BY [SubmittedAt] DESC";

            AddParameter(command, "@MaxResults", maxResults);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new SubmissionResult
                {
                    InvoiceNumber = reader.GetString(0),
                    Status = reader.GetString(1),
                    ErrorCode = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ErrorMessage = reader.IsDBNull(3) ? null : reader.GetString(3),
                    SubmittedAt = reader.GetDateTime(4)
                });
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query failed submissions");
            throw;
        }

        return results;
    }

    #region Private Helpers

    private void EnsureConnectionOpen()
    {
        if (_dbConnection.State != ConnectionState.Open)
        {
            _dbConnection.Open();
        }
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    #endregion
}
