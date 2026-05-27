namespace MyInvois.Service.Configuration;

/// <summary>
/// Configuration for the in-process daily batch scheduler (BackgroundService).
/// Maps to appsettings.json["BatchScheduler"].
/// </summary>
public class BatchSchedulerSettings
{
    /// <summary>
    /// Set false to disable the background scheduler entirely (e.g. in dev or when running on-demand only).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Hour (local time, 0–23) at which the daily batch fires.
    /// Default 02:00 — avoids peak LHDN API traffic windows.
    /// </summary>
    public int DailyRunHour { get; set; } = 2;

    /// <summary>
    /// Minute (0–59) at which the daily batch fires.
    /// </summary>
    public int DailyRunMinute { get; set; } = 0;

    /// <summary>
    /// Number of days back to include in the daily batch.
    /// 1 = yesterday only (default). Set to 2 for a safety overlap on weekends.
    /// </summary>
    public int LookbackDays { get; set; } = 1;
}
