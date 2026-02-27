namespace MyInvois.Service.Configuration;

/// <summary>
/// Processing Configuration
/// Maps to appsettings.json["Processing"]
/// </summary>
public class ProcessingSettings
{
    /// <summary>
    /// Enable batch processing (scheduled job)
    /// </summary>
    public bool EnableBatchProcessing { get; set; } = true;
    
    /// <summary>
    /// Day of month to run monthly submission (1-31)
    /// </summary>
    public int MonthlySubmissionDay { get; set; } = 1;
    
    /// <summary>
    /// Submission window in hours from MonthlySubmissionDay
    /// E.g., if day=1 and window=2, submission runs on 1st between 00:00 and 02:00
    /// </summary>
    public int SubmissionWindowHours { get; set; } = 2;
    
    /// <summary>
    /// Maximum invoices to process in a single run
    /// </summary>
    public int MaxInvoicesPerRun { get; set; } = 1000;
}

