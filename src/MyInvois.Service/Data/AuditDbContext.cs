namespace MyInvois.Service.Data;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core DbContext for the SQLite audit log database.
/// Follows WORKSPACE_RULES standard schema: indexes, check constraints, WAL mode.
/// ADR-014: SQLite via EF Core 8 (Phase 2, March 2026).
/// File path (production): ./data/audit.db (relative to AppContext.BaseDirectory)
/// File path (tests): named shared in-memory SQLite, e.g.
///   Data Source=AuditTests;Mode=Memory;Cache=Shared
/// plus a long-lived `_keepAlive` connection to keep the DB alive
/// across multiple CreateDbContext() calls.
/// </summary>
public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AuditLogEntity>(e =>
        {
            e.HasKey(x => x.AuditId);
            e.Property(x => x.Status).HasColumnType("TEXT");
            e.Property(x => x.Severity).HasColumnType("TEXT");

            // Check constraints (mirrors SQL Server CK_ constraints) — use ToTable() per EF Core 8
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Status",
                    "\"Status\" IN ('Success','Failed','Pending','Cancelled')");
                t.HasCheckConstraint("CK_Severity",
                    "\"Severity\" IN ('Info','Warning','Error','Critical')");
            });

            // Standard indexes (WORKSPACE_RULES schema)
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => new { x.UserId, x.Timestamp });
            e.HasIndex(x => new { x.ResourceType, x.ResourceId, x.Timestamp });
            e.HasIndex(x => x.Action);
            e.HasIndex(x => new { x.Status, x.Timestamp })
             .HasFilter("\"Status\" != 'Success'");

            // MyInvois-specific indexes (filtered — sparse)
            e.HasIndex(x => new { x.MyInvoisUUID, x.Timestamp })
             .HasFilter("\"MyInvoisUUID\" IS NOT NULL");
            e.HasIndex(x => new { x.InvoiceNumber, x.Timestamp })
             .HasFilter("\"InvoiceNumber\" IS NOT NULL");
            e.HasIndex(x => new { x.SubmissionBatchId, x.Timestamp })
             .HasFilter("\"SubmissionBatchId\" IS NOT NULL");
        });
    }
}
