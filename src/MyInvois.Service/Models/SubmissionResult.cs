namespace MyInvois.Service.Models;

/// <summary>
/// Submission Result - Wraps the outcome of invoice submission
/// </summary>
public class SubmissionResult
{
    /// <summary>
    /// Unique submission ID (GUID)
    /// </summary>
    public string SubmissionId { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// MOVEX invoice number (source document)
    /// </summary>
    public string InvoiceNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Status: Pending, Success, Failed, Cancelled
    /// </summary>
    public string Status { get; set; } = "Pending";
    
    /// <summary>
    /// MyInvois UUID (returned from MyInvois API on success)
    /// </summary>
    public string? MyInvoisUUID { get; set; }
    
    /// <summary>
    /// MyInvois submission status (Valid, Invalid, Submitted, Cancelled, Rejected, etc.)
    /// </summary>
    public string? MyInvoisStatus { get; set; }
    
    /// <summary>
    /// Submission reference from MyInvois
    /// </summary>
    public string? SubmissionReference { get; set; }
    
    /// <summary>
    /// HTTP Status Code (200, 400, 429, 500, etc.)
    /// </summary>
    public int? HttpStatusCode { get; set; }
    
    /// <summary>
    /// Error code from MyInvois API (e.g., "DS302" for duplicate)
    /// </summary>
    public string? ErrorCode { get; set; }
    
    /// <summary>
    /// Error message (technical or user-friendly)
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Number of retries attempted
    /// </summary>
    public int RetryCount { get; set; } = 0;
    
    /// <summary>
    /// Time taken for submission in milliseconds
    /// </summary>
    public long DurationMs { get; set; }
    
    /// <summary>
    /// Timestamp when submission was made
    /// </summary>
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Full response from MyInvois API (for forensics)
    /// </summary>
    public string? RawResponse { get; set; }
    
    /// <summary>
    /// Invoice data that was submitted (for audit trail)
    /// </summary>
    public MyInvoiceDocument? InvoiceData { get; set; }
    
    /// <summary>
    /// Success flag
    /// </summary>
    public bool IsSuccess => Status == "Success";
}

/// <summary>
/// Batch Processing Result - Summary of a batch submission
/// </summary>
public class BatchResult
{
    /// <summary>
    /// Batch ID (GUID)
    /// </summary>
    public string BatchId { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Batch type: Sales or Purchase
    /// </summary>
    public string BatchType { get; set; } = string.Empty;
    
    /// <summary>
    /// Total invoices in batch
    /// </summary>
    public int TotalInvoices { get; set; }
    
    /// <summary>
    /// Successful submissions
    /// </summary>
    public int SuccessCount { get; set; }
    
    /// <summary>
    /// Failed submissions
    /// </summary>
    public int FailedCount { get; set; }
    
    /// <summary>
    /// Skipped (e.g., duplicates)
    /// </summary>
    public int SkippedCount { get; set; }
    
    /// <summary>
    /// Success rate (percentage)
    /// </summary>
    public decimal SuccessRate => TotalInvoices > 0 ? (decimal)SuccessCount / TotalInvoices * 100 : 0;
    
    /// <summary>
    /// Submission details
    /// </summary>
    public List<SubmissionResult> Submissions { get; set; } = new();
    
    /// <summary>
    /// Batch start time
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Batch end time
    /// </summary>
    public DateTime? CompletedAt { get; set; }
    
    /// <summary>
    /// Total duration in seconds
    /// </summary>
    public long DurationSeconds => CompletedAt.HasValue 
        ? (long)(CompletedAt.Value - StartedAt).TotalSeconds 
        : 0;
    
    /// <summary>
    /// Error summary (common errors across batch)
    /// </summary>
    public string? ErrorSummary { get; set; }
}

/// <summary>
/// OAuth Token Response from MyInvois
/// </summary>
public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    
    public string TokenType { get; set; } = "Bearer";
    
    public int ExpiresIn { get; set; } = 3600; // Default 1 hour
    
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Check if token is still valid (with 5-minute buffer)
    /// </summary>
    public bool IsValid => DateTime.UtcNow < IssuedAt.AddSeconds(ExpiresIn - 300);
}

