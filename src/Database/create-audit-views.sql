-- ====================================================================
-- MyInvois Service - Audit Log Views
-- Database: SRX_AuditLog
-- Version: 1.0
-- Date: February 5, 2026
-- ====================================================================

USE SRX_AuditLog;
GO

-- View: Failed submissions (for retry dashboard/operations)
IF OBJECT_ID('dbo.vw_MyInvois_FailedSubmissions', 'V') IS NOT NULL
    DROP VIEW [dbo].[vw_MyInvois_FailedSubmissions];
GO

CREATE VIEW [dbo].[vw_MyInvois_FailedSubmissions]
AS
SELECT 
    [AuditId],
    [Timestamp],
    [InvoiceNumber],
    [InvoiceDate],
    [InvoiceType],
    [TotalAmount],
    [CurrencyCode],
    [Status],
    [ErrorMessage],
    [ValidationErrors],
    [RetryCount],
    [MyInvoisStatus],
    [MyInvoisUUID],
    DATEDIFF(HOUR, [Timestamp], GETUTCDATE()) AS [HoursSinceFailed]
FROM [dbo].[AuditLog]
WHERE [Category] = 'MyInvois'
  AND [Status] IN ('Failed', 'Pending')
  AND [RetryCount] < 3      -- Only show invoices that can still be retried
  AND [Timestamp] > DATEADD(DAY, -7, GETUTCDATE()); -- Last 7 days only
GO

-- View: Monthly submission summary
IF OBJECT_ID('dbo.vw_MyInvois_MonthlySummary', 'V') IS NOT NULL
    DROP VIEW [dbo].[vw_MyInvois_MonthlySummary];
GO

CREATE VIEW [dbo].[vw_MyInvois_MonthlySummary]
AS
SELECT 
    YEAR([InvoiceDate]) AS [Year],
    MONTH([InvoiceDate]) AS [Month],
    [InvoiceType],
    COUNT(*) AS [TotalInvoices],
    SUM(CASE WHEN [Status] = 'Success' THEN 1 ELSE 0 END) AS [SuccessCount],
    SUM(CASE WHEN [Status] = 'Failed' THEN 1 ELSE 0 END) AS [FailedCount],
    SUM(CASE WHEN [Status] = 'Pending' THEN 1 ELSE 0 END) AS [PendingCount],
    CAST(SUM(CASE WHEN [Status] = 'Success' THEN 1 ELSE 0 END) AS DECIMAL(5,2)) 
        / COUNT(*) * 100 AS [SuccessRate],
    SUM([TotalAmount]) AS [TotalAmount],
    AVG([Duration]) AS [AvgDurationMs],
    MAX([Timestamp]) AS [LastSubmission]
FROM [dbo].[AuditLog]
WHERE [Category] = 'MyInvois'
  AND [Action] = 'MyInvois_Submit'
  AND [InvoiceDate] IS NOT NULL
GROUP BY YEAR([InvoiceDate]), MONTH([InvoiceDate]), [InvoiceType];
GO

-- View: Duplicate detection (invoices submitted multiple times)
IF OBJECT_ID('dbo.vw_MyInvois_DuplicateSubmissions', 'V') IS NOT NULL
    DROP VIEW [dbo].[vw_MyInvois_DuplicateSubmissions];
GO

CREATE VIEW [dbo].[vw_MyInvois_DuplicateSubmissions]
AS
SELECT 
    [InvoiceNumber],
    COUNT(*) AS [SubmissionCount],
    MIN([Timestamp]) AS [FirstSubmission],
    MAX([Timestamp]) AS [LastSubmission],
    SUM(CASE WHEN [Status] = 'Success' THEN 1 ELSE 0 END) AS [SuccessfulSubmissions],
    STRING_AGG([MyInvoisUUID], ',') AS [MyInvoisUUIDs]
FROM [dbo].[AuditLog]
WHERE [Category] = 'MyInvois'
  AND [Action] = 'MyInvois_Submit'
  AND [InvoiceNumber] IS NOT NULL
GROUP BY [InvoiceNumber]
HAVING COUNT(*) > 1;
GO

-- View: Error frequency (for trend analysis)
IF OBJECT_ID('dbo.vw_MyInvois_ErrorFrequency', 'V') IS NOT NULL
    DROP VIEW [dbo].[vw_MyInvois_ErrorFrequency];
GO

CREATE VIEW [dbo].[vw_MyInvois_ErrorFrequency]
AS
SELECT 
    [ErrorMessage],
    [StatusCode],
    COUNT(*) AS [Frequency],
    MIN([Timestamp]) AS [FirstOccurrence],
    MAX([Timestamp]) AS [LastOccurrence],
    DATEDIFF(HOUR, MAX([Timestamp]), GETUTCDATE()) AS [HoursSinceLast]
FROM [dbo].[AuditLog]
WHERE [Category] = 'MyInvois'
  AND [Status] = 'Failed'
  AND [ErrorMessage] IS NOT NULL
  AND [Timestamp] > DATEADD(DAY, -30, GETUTCDATE())
GROUP BY [ErrorMessage], [StatusCode]
ORDER BY [Frequency] DESC;
GO

-- View: Batch processing metrics
IF OBJECT_ID('dbo.vw_MyInvois_BatchMetrics', 'V') IS NOT NULL
    DROP VIEW [dbo].[vw_MyInvois_BatchMetrics];
GO

CREATE VIEW [dbo].[vw_MyInvois_BatchMetrics]
AS
SELECT 
    [SubmissionBatchId],
    COUNT(*) AS [InvoicesInBatch],
    SUM(CASE WHEN [Status] = 'Success' THEN 1 ELSE 0 END) AS [SuccessCount],
    SUM(CASE WHEN [Status] = 'Failed' THEN 1 ELSE 0 END) AS [FailedCount],
    CAST(SUM(CASE WHEN [Status] = 'Success' THEN 1 ELSE 0 END) AS DECIMAL(5,2)) 
        / COUNT(*) * 100 AS [SuccessRate],
    AVG([Duration]) AS [AvgDurationMs],
    MAX([Duration]) AS [MaxDurationMs],
    MIN([Timestamp]) AS [BatchStartTime],
    MAX([Timestamp]) AS [BatchEndTime]
FROM [dbo].[AuditLog]
WHERE [Category] = 'MyInvois'
  AND [SubmissionBatchId] IS NOT NULL
GROUP BY [SubmissionBatchId];
GO

PRINT 'Views created successfully.';
GO

