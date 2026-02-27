-- ====================================================================
-- MyInvois Service - SQL Server Audit Log Schema
-- Database: SRX_AuditLog
-- Version: 1.0
-- Date: February 5, 2026
-- ====================================================================

USE SRX_AuditLog;
GO

-- Create standard audit log table (if not exists)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AuditLog' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    PRINT 'Creating [dbo].[AuditLog] table...';
    
    CREATE TABLE [dbo].[AuditLog] (
        -- Primary Key & Timestamp
        [AuditId]           UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID(),
        [Timestamp]         DATETIME2(7)        NOT NULL DEFAULT SYSUTCDATETIME(),
        
        -- Who (Actor)
        [UserId]            NVARCHAR(100)       NULL,           -- Windows username or service account
        [UserRole]          NVARCHAR(50)        NULL,           -- RBAC role (e.g., Finance_Invoicing)
        [IpAddress]         NVARCHAR(45)        NULL,           -- IPv4/IPv6
        
        -- What (Action)
        [Action]            NVARCHAR(100)       NOT NULL,       -- e.g., "MyInvois_Submit", "M3_GetInvoice"
        [Category]          NVARCHAR(50)        NOT NULL,       -- e.g., "Integration", "Finance", "Inventory"
        [Severity]          NVARCHAR(20)        NOT NULL,       -- Info, Warning, Error, Critical
        
        -- Where (Resource)
        [ResourceType]      NVARCHAR(50)        NOT NULL,       -- e.g., "Invoice", "PurchaseOrder"
        [ResourceId]        NVARCHAR(100)       NOT NULL,       -- e.g., Invoice number, PO number
        [Endpoint]          NVARCHAR(500)       NULL,           -- API endpoint or service method
        
        -- Result
        [Status]            NVARCHAR(20)        NOT NULL,       -- Success, Failed, Pending
        [StatusCode]        NVARCHAR(50)        NULL,           -- HTTP status code or custom code
        [ErrorMessage]      NVARCHAR(MAX)       NULL,           -- Error details (if failed)
        
        -- Payload (for forensics)
        [RequestPayload]    NVARCHAR(MAX)       NULL,           -- Original request (JSON/XML)
        [ResponsePayload]   NVARCHAR(MAX)       NULL,           -- API response (JSON/XML)
        
        -- Metadata
        [CorrelationId]     UNIQUEIDENTIFIER    NULL,           -- For distributed tracing
        [Duration]          INT                 NULL,           -- Execution time in milliseconds
        [RetryCount]        INT                 NOT NULL DEFAULT 0,
        
        -- Constraints
        CONSTRAINT [PK_AuditLog] PRIMARY KEY CLUSTERED ([AuditId]),
        CONSTRAINT [CK_AuditLog_Status] CHECK ([Status] IN ('Success', 'Failed', 'Pending', 'Cancelled')),
        CONSTRAINT [CK_AuditLog_Severity] CHECK ([Severity] IN ('Info', 'Warning', 'Error', 'Critical'))
    );
    
    PRINT 'Table [dbo].[AuditLog] created successfully.';
END
ELSE
BEGIN
    PRINT 'Table [dbo].[AuditLog] already exists.';
END
GO

-- Add MyInvois-specific columns (if not exists)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLog') AND name = 'MyInvoisUUID')
BEGIN
    PRINT 'Adding MyInvois-specific columns...';
    
    ALTER TABLE [dbo].[AuditLog] ADD
        -- MyInvois submission tracking
        [MyInvoisUUID]          NVARCHAR(100)   NULL,       -- MyInvois document UUID (from response)
        [MyInvoisStatus]        NVARCHAR(50)    NULL,       -- Valid, Invalid, Submitted, Cancelled, Rejected
        [MyInvoisSubmissionId]  NVARCHAR(100)   NULL,       -- Submission reference number
        
        -- MOVEX invoice tracking
        [InvoiceNumber]         NVARCHAR(50)    NULL,       -- M3 invoice number (OINVOH.IVNO)
        [InvoiceDate]           DATE            NULL,       -- Invoice date (OINVOH.IVDT)
        [InvoiceType]           NVARCHAR(20)    NULL,       -- Sales, Purchase
        
        -- Financial tracking (for monitoring/reporting)
        [TotalAmount]           DECIMAL(18,2)   NULL,       -- Invoice total amount
        [TotalTax]              DECIMAL(18,2)   NULL,       -- Total tax amount
        [CurrencyCode]          NCHAR(3)        NULL,       -- MYR, USD, SGD, etc.
        [ExchangeRate]          DECIMAL(18,6)   NULL,       -- Exchange rate (if applicable)
        
        -- MyInvois validation tracking
        [ValidationErrors]      NVARCHAR(MAX)   NULL,       -- JSON array of validation errors
        [SubmissionBatchId]     UNIQUEIDENTIFIER NULL;      -- Groups invoices submitted together
    
    PRINT 'MyInvois columns added successfully.';
END
ELSE
BEGIN
    PRINT 'MyInvois columns already exist.';
END
GO

-- Create or refresh indexes
PRINT 'Creating indexes...';
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_Timestamp' AND object_id = OBJECT_ID('dbo.AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_Timestamp] 
        ON [dbo].[AuditLog] ([Timestamp] DESC);
    PRINT 'Index [IX_AuditLog_Timestamp] created.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_User' AND object_id = OBJECT_ID('dbo.AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_User] 
        ON [dbo].[AuditLog] ([UserId], [Timestamp] DESC);
    PRINT 'Index [IX_AuditLog_User] created.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_Resource' AND object_id = OBJECT_ID('dbo.AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_Resource] 
        ON [dbo].[AuditLog] ([ResourceType], [ResourceId], [Timestamp] DESC);
    PRINT 'Index [IX_AuditLog_Resource] created.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_Status' AND object_id = OBJECT_ID('dbo.AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_Status] 
        ON [dbo].[AuditLog] ([Status], [Timestamp] DESC) 
        WHERE [Status] <> 'Success';
    PRINT 'Index [IX_AuditLog_Status] created.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_Action' AND object_id = OBJECT_ID('dbo.AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_Action] 
        ON [dbo].[AuditLog] ([Action], [Timestamp] DESC);
    PRINT 'Index [IX_AuditLog_Action] created.';
END
GO

-- MyInvois-specific indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_MyInvoisUUID' AND object_id = OBJECT_ID('dbo.AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_MyInvoisUUID] 
        ON [dbo].[AuditLog] ([MyInvoisUUID], [Timestamp] DESC)
        WHERE [MyInvoisUUID] IS NOT NULL;
    PRINT 'Index [IX_AuditLog_MyInvoisUUID] created.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_InvoiceNumber' AND object_id = OBJECT_ID('dbo.AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_InvoiceNumber] 
        ON [dbo].[AuditLog] ([InvoiceNumber], [Timestamp] DESC)
        WHERE [InvoiceNumber] IS NOT NULL;
    PRINT 'Index [IX_AuditLog_InvoiceNumber] created.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_SubmissionBatch' AND object_id = OBJECT_ID('dbo.AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_SubmissionBatch] 
        ON [dbo].[AuditLog] ([SubmissionBatchId], [Timestamp] DESC)
        WHERE [SubmissionBatchId] IS NOT NULL;
    PRINT 'Index [IX_AuditLog_SubmissionBatch] created.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_AuditLog_MyInvoisStatus' AND object_id = OBJECT_ID('dbo.AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AuditLog_MyInvoisStatus] 
        ON [dbo].[AuditLog] ([MyInvoisStatus], [Timestamp] DESC)
        WHERE [MyInvoisStatus] IN ('Failed', 'Invalid', 'Pending');
    PRINT 'Index [IX_AuditLog_MyInvoisStatus] created.';
END
GO

PRINT '========================================';
PRINT 'Audit Log schema setup completed.';
PRINT '========================================';

