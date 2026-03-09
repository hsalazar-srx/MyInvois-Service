# Audit Logging Pattern

**Purpose:** Log all operations to workspace-compliant audit schema via EF Core + SQLite
**Storage:** SQLite via EF Core 8 (Phase 2, ADR-014) — Phase 1 used SQL Server
**Last Updated:** 2026-03-04

---

## Overview

The audit logger is implemented as `AuditLogger : IAuditLogger` using `IDbContextFactory<AuditDbContext>`
for thread-safe, per-operation database access. The `IAuditLogger` interface is unchanged from Phase 1.

**File path (production):** `./data/audit.db` (relative to `AppContext.BaseDirectory`)
**File path (tests):** `Data Source=:memory:` (in-memory SQLite, no file)

---

## IAuditLogger Interface (unchanged)

```csharp
public interface IAuditLogger
{
    Task LogSubmissionAsync(AuditEntry entry);
    Task<bool> IsInvoiceAlreadySubmittedAsync(string invoiceNumber);
    Task<IEnumerable<AuditEntry>> GetFailedSubmissionsAsync(int maxResults = 100);
}
```

---

## Complete Implementation Example

```csharp
/// <summary>
/// Logs MyInvois submission to SQLite audit database via EF Core.
/// IAuditLogger interface unchanged from Phase 1 (ADR-014).
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly IDbContextFactory<AuditDbContext> _contextFactory;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(IDbContextFactory<AuditDbContext> contextFactory, ILogger<AuditLogger> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task LogSubmissionAsync(AuditEntry entry)
    {
        using var ctx = _contextFactory.CreateDbContext();
        ctx.AuditLogs.Add(new AuditLogEntity
        {
            // ===== STANDARD FIELDS =====
            AuditId        = Guid.NewGuid().ToString(),
            Timestamp      = DateTime.UtcNow.ToString("O"),      // ISO 8601 UTC

            // Who
            UserId         = entry.UserId,                        // e.g., "SRXGLOBAL\\svc-myinvois"
            UserRole       = entry.UserRole,                      // "Service_MyInvoicing"
            IpAddress      = entry.IpAddress,

            // What
            Action         = entry.Action,                        // "MyInvois_Submit"
            Category       = entry.Category,                      // "Integration"
            Severity       = entry.Success ? "Info" : "Error",

            // Where
            ResourceType   = "Invoice",
            ResourceId     = entry.InvoiceNumber,
            Endpoint       = "POST /api/v1/submissions",

            // Result
            Status         = entry.Success ? "Success" : "Failed",
            StatusCode     = entry.StatusCode,                    // e.g., "200", "DS302"
            ErrorMessage   = entry.ErrorMessage,

            // Payload (no PII)
            RequestPayload = JsonSerializer.Serialize(new {
                entry.InvoiceNumber,
                entry.TotalAmount,
                entry.CurrencyCode
                // NO customer names, Tax IDs, etc.
            }),
            ResponsePayload = entry.ResponseJson,

            // Metadata
            CorrelationId  = entry.CorrelationId.ToString(),
            Duration       = entry.DurationMs,
            RetryCount     = entry.RetryCount,

            // ===== MYINVOIS-SPECIFIC EXTENSIONS =====
            MyInvoisUUID   = entry.MyInvoisUUID,
            InvoiceNumber  = entry.InvoiceNumber,
            InvoiceDate    = entry.InvoiceDate.ToString("yyyy-MM-dd"),
            TotalAmount    = entry.TotalAmount,
            CurrencyCode   = entry.CurrencyCode
        });

        await ctx.SaveChangesAsync();
    }

    public async Task<bool> IsInvoiceAlreadySubmittedAsync(string invoiceNumber)
    {
        using var ctx = _contextFactory.CreateDbContext();
        return await ctx.AuditLogs.AnyAsync(x =>
            x.InvoiceNumber == invoiceNumber &&
            x.Status == "Success");
    }

    public async Task<IEnumerable<AuditEntry>> GetFailedSubmissionsAsync(int maxResults = 100)
    {
        using var ctx = _contextFactory.CreateDbContext();
        return await ctx.AuditLogs
            .Where(x => x.Status == "Failed" && x.Category == "Integration")
            .OrderByDescending(x => x.Timestamp)
            .Take(maxResults)
            .Select(x => new AuditEntry { /* map fields */ })
            .ToListAsync();
    }
}
```

---

## DI Registration

```csharp
// ServiceCollectionExtensions.cs
services.AddDbContextFactory<AuditDbContext>(options =>
    options.UseSqlite(configuration.GetConnectionString("AuditLog")));

services.AddScoped<IAuditLogger, AuditLogger>();

// Startup: ensure DB created + WAL mode enabled
using var scope = app.Services.CreateScope();
var ctx = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
ctx.Database.EnsureCreated();
ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
```

---

## Query Examples (LINQ via EF Core)

### Failed Submissions in Last 24 Hours

```csharp
var cutoff = DateTime.UtcNow.AddHours(-24).ToString("O");
var failed = await ctx.AuditLogs
    .Where(x => x.Action == "MyInvois_Submit"
             && x.Status == "Failed"
             && string.Compare(x.Timestamp, cutoff) >= 0)
    .OrderByDescending(x => x.Timestamp)
    .ToListAsync();
```

### Success Rate (all-time)

```csharp
var all     = await ctx.AuditLogs.CountAsync(x => x.Action == "MyInvois_Submit");
var success = await ctx.AuditLogs.CountAsync(x => x.Action == "MyInvois_Submit" && x.Status == "Success");
var rate    = all > 0 ? (double)success / all * 100 : 0;
```

### Duplicate Detection (before submission)

```csharp
bool alreadySubmitted = await ctx.AuditLogs.AnyAsync(x =>
    x.InvoiceNumber == invoiceNumber &&
    x.Status == "Success");
```

---

## Integration Test Pattern

```csharp
// Use in-memory SQLite — no Mock<IDbConnection> needed
public class AuditLoggerIntegrationTests : IDisposable
{
    private readonly AuditDbContext _ctx;
    private readonly AuditLogger _sut;

    public AuditLoggerIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _ctx = new AuditDbContext(options);
        _ctx.Database.EnsureCreated();

        var factory = new TestDbContextFactory(_ctx);
        _sut = new AuditLogger(factory, NullLogger<AuditLogger>.Instance);
    }

    [Fact]
    public async Task LogSubmission_Success_InsertsRow()
    {
        await _sut.LogSubmissionAsync(new AuditEntry { /* ... */ });
        var count = await _ctx.AuditLogs.CountAsync();
        count.Should().Be(1);
    }

    public void Dispose() => _ctx.Dispose();
}
```

---

## ✅ Key Rules

### DO:
- ✅ Use `IDbContextFactory<AuditDbContext>` (thread-safe, one context per operation)
- ✅ Store timestamps as ISO 8601 UTC string (`DateTime.UtcNow.ToString("O")`)
- ✅ Store GUIDs as string (`.ToString()`)
- ✅ Log both success AND failure
- ✅ Include correlation ID for tracing
- ✅ Enable WAL mode on startup (`PRAGMA journal_mode=WAL`)

### DON'T:
- ❌ Log PII (Tax IDs, customer names, emails, phones)
- ❌ Use a single shared `DbContext` across async operations (not thread-safe)
- ❌ Use local time — always UTC
- ❌ Modify audit log rows after insert (immutable, append-only)
- ❌ Hardcode the connection string — use User Secrets / environment variable

---

## 🔍 What to Log vs Not Log

### ✅ Safe to Log:
- Invoice numbers
- Amounts and currency codes
- Status codes and error codes
- Timestamps (UTC)
- User Windows ID / service account
- Correlation IDs
- MyInvois UUID (public identifier)

### ❌ NEVER Log:
- Tax IDs (TIN, BRN)
- Customer names
- Email addresses
- Phone numbers
- Payment card details
- Passwords or API keys

---

## 🔗 Related

- [ADR-014 — SQLite Storage Decision](../memory/09-implementation-decisions.md)
- [decision-001-sqlite-audit-storage.md](../evidence/decision-001-sqlite-audit-storage.md)
- [WORKSPACE_RULES.md](../../.github/WORKSPACE_RULES.md) — Standard audit schema & SQLite conditions
- [ai/memory/02-data-model.md](../memory/02-data-model.md) — MyInvois-specific field extensions
