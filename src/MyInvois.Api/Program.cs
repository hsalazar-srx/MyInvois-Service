using Microsoft.EntityFrameworkCore;
using MyInvois.Api.Middleware;
using MyInvois.Service.Configuration;
using MyInvois.Service.DataAccess;
using MyInvois.Service.Services;
using Serilog;
using Serilog.Context;

// Uses skill: architecture/dotnet-api-design v1.0
// Implements ADR-001: standalone HTTP host for MyInvois data access, consumed by SM-Portal.

var builder = WebApplication.CreateBuilder(args);

// Localhost-only binding — this API is an internal service, not exposed externally.
// Production: IIS site with localhost binding on port 5051.
builder.WebHost.UseUrls("http://localhost:5051");

// Serilog structured logging with CorrelationId enrichment (workspace standard)
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithCorrelationId()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"));

builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection("ApiKeys"));
builder.Services.AddControllers();

// MOVEX DB2 data access — IInvoiceDataSource via DirectQueryDataSource.
// Connection string must be set via user-secrets (MovexDb:ConnectionString).
// Run from src/MyInvois.Api/:
//   dotnet user-secrets set "ApiKeys:Primary"           "<guid>"
//   dotnet user-secrets set "ApiKeys:Admin"             "<guid>"
//   dotnet user-secrets set "MovexDb:ConnectionString"  "DSN=AS400;UID=...;PWD=...;"
builder.Services.AddMovexDataAccess(builder.Configuration);

// Audit logging (SQLite/EF Core, ADR-014)
builder.Services.AddAuditLogging(builder.Configuration);

// MyInvois API settings — credentials and endpoint from user-secrets / appsettings.
builder.Services.Configure<MyInvoisApiSettings>(builder.Configuration.GetSection("MyInvoisApi"));

// Named HttpClient used by MyInvoiceSubmitter for all LHDN API calls.
builder.Services.AddHttpClient("MyInvois");

// Full invoice submission pipeline: Reader → Mapper → Submitter → Processor
builder.Services.AddMyInvoisSubmissionPipeline();

// Daily batch scheduler (BackgroundService) — fires at BatchScheduler:DailyRunHour each day.
// Set BatchScheduler:Enabled=false in dev/test to suppress background processing.
builder.Services.Configure<BatchSchedulerSettings>(builder.Configuration.GetSection("BatchScheduler"));
builder.Services.AddHostedService<DailyBatchHostedService>();

var app = builder.Build();

// Initialize SQLite audit database: create schema and enable WAL mode (ADR-014).
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MyInvois.Service.Data.AuditDbContext>>();
    using var ctx = dbFactory.CreateDbContext();
    ctx.Database.EnsureCreated();
    ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

// CorrelationId middleware — reads X-Correlation-Id from callers (SM-Portal forwards it),
// or generates a new one, and pushes it into the Serilog LogContext for end-to-end tracing.
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? context.TraceIdentifier;
    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        await next();
    }
});

app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

// Health probe — no auth, used for IIS application pool health checks
app.MapGet("/api/v1/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();
