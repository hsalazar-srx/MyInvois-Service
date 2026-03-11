namespace MyInvois.Service.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Design-time factory for EF Core tooling (dotnet ef migrations add / update).
/// Reads connection string from CONNECTIONSTRINGS__AUDITLOG environment variable
/// or falls back to ./data/audit.db.
/// ADR-014: SQLite via EF Core 8 (Phase 2, March 2026).
/// </summary>
public class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__AUDITLOG")
            ?? "Data Source=./data/audit.db";

        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new AuditDbContext(options);
    }
}
