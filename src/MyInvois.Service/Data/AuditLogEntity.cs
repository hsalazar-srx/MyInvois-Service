namespace MyInvois.Service.Data;

/// <summary>
/// EF Core entity for the SQLite audit log table.
/// Maps all columns from create-audit-table.sql to SQLite via EF Core 8.
/// ADR-014: Replaces SQL Server [dbo].[AuditLog] with SQLite Code-First (Phase 2, March 2026).
/// GUIDs stored as TEXT, DateTime stored as ISO 8601 TEXT (SQLite has no native datetime type).
/// </summary>
public class AuditLogEntity
{
    // Primary Key & Timestamp
    public string AuditId { get; set; } = Guid.NewGuid().ToString();        // GUID → TEXT
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");  // ISO 8601 UTC

    // Who (Actor)
    public string? UserId { get; set; }      // Windows username or service account
    public string? UserRole { get; set; }    // RBAC role (e.g., Finance_Invoicing)
    public string? IpAddress { get; set; }   // IPv4/IPv6

    // What (Action)
    public string Action { get; set; } = null!;     // e.g., "MyInvois_Submit"
    public string Category { get; set; } = null!;   // e.g., "Integration"
    public string Severity { get; set; } = null!;   // Info, Warning, Error, Critical

    // Where (Resource)
    public string ResourceType { get; set; } = null!;   // e.g., "Invoice"
    public string ResourceId { get; set; } = null!;     // e.g., invoice number
    public string? Endpoint { get; set; }               // API endpoint

    // Result
    public string Status { get; set; } = null!;     // Success, Failed, Pending, Cancelled
    public string? StatusCode { get; set; }          // HTTP status or custom code (e.g., DS302)
    public string? ErrorMessage { get; set; }        // Error details (if failed)

    // Payload (for forensics — no PII)
    public string? RequestPayload { get; set; }    // JSON: InvoiceNumber, TotalAmount, CurrencyCode only
    public string? ResponsePayload { get; set; }   // MyInvois API response JSON

    // Metadata
    public string? CorrelationId { get; set; }   // GUID as TEXT — for distributed tracing
    public int? Duration { get; set; }           // Execution time in milliseconds
    public int RetryCount { get; set; } = 0;

    // MyInvois-Specific Extensions
    public string? MyInvoisUUID { get; set; }          // UUID returned by MyInvois API
    public string? MyInvoisStatus { get; set; }        // Valid, Invalid, Submitted, Cancelled, Rejected
    public string? MyInvoisSubmissionId { get; set; }  // Submission reference number
    public string? InvoiceNumber { get; set; }         // MOVEX invoice number (OINVOH.IVNO)
    public string? InvoiceDate { get; set; }           // TEXT: yyyy-MM-dd (ISO 8601 date)
    public string? InvoiceType { get; set; }           // "Sales" or "Purchase"
    public decimal? TotalAmount { get; set; }          // Invoice total including tax (REAL)
    public decimal? TotalTax { get; set; }             // Total tax amount (REAL)
    public string? CurrencyCode { get; set; }          // ISO 4217: MYR, USD, SGD
    public decimal? ExchangeRate { get; set; }         // Exchange rate if non-MYR (REAL)
    public string? ValidationErrors { get; set; }      // JSON array of validation errors
    public string? SubmissionBatchId { get; set; }     // GUID as TEXT — groups batch submissions
}
