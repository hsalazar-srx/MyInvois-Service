namespace MyInvois.Service.Configuration;

/// <summary>
/// Batch Processing Configuration
/// Maps to appsettings.json["BatchProcessing"]
/// </summary>
public class BatchConfiguration
{
    /// <summary>
    /// Batch size for sales invoices (typically monthly submission as single batch)
    /// </summary>
    public int SalesBatchSize { get; set; } = 100;
    
    /// <summary>
    /// Batch size for purchase invoices (split into multiple batches to handle volume)
    /// </summary>
    public int PurchaseBatchSize { get; set; } = 50;
    
    /// <summary>
    /// Delay between batches in milliseconds (safety margin for rate limiting)
    /// Rate limit: 100 requests/minute = 1 request per 0.6 seconds
    /// </summary>
    public int DelayBetweenBatchesMs { get; set; } = 600;
    
    /// <summary>
    /// Maximum retry attempts for failed submissions
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>
    /// Delay before retrying in seconds (exponential backoff applied)
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 5;
    
    /// <summary>
    /// Circuit breaker threshold: number of failures before opening circuit
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 5;
    
    /// <summary>
    /// Circuit breaker duration in seconds (how long to wait before retrying)
    /// </summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 60;
}

