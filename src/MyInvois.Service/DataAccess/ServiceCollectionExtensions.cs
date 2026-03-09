namespace MyInvois.Service.DataAccess;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyInvois.Service.Configuration;
using MyInvois.Service.Data;
using MyInvois.Service.Services;

// Uses skill: architecture/configuration-management v1.0+

/// <summary>
/// DI registration for MOVEX data access layer.
/// Resolves IInvoiceDataSource and IPartyDataProvider based on MovexDbSettings configuration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register MOVEX DB2 data access services.
    /// Reads MovexDb:DataSourceStrategy and MovexDb:PartyDataSource from configuration
    /// to select the appropriate implementations.
    /// </summary>
    public static IServiceCollection AddMovexDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind settings
        services.Configure<MovexDbSettings>(configuration.GetSection("MovexDb"));
        services.Configure<ForeignPartyDefaultsSettings>(configuration.GetSection("ForeignPartyDefaults"));

        var settings = configuration.GetSection("MovexDb").Get<MovexDbSettings>()
            ?? new MovexDbSettings();

        // Register IInvoiceDataSource based on strategy
        switch (settings.DataSourceStrategy)
        {
            case "DirectQuery":
                services.AddScoped<IInvoiceDataSource, DirectQueryDataSource>();
                break;
            case "StoredProcedure":
                services.AddScoped<IInvoiceDataSource, StoredProcedureDataSource>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown MovexDb:DataSourceStrategy '{settings.DataSourceStrategy}'. " +
                    "Valid values: 'DirectQuery', 'StoredProcedure'.");
        }

        // Register IPartyDataProvider based on configuration
        switch (settings.PartyDataSource)
        {
            case "Placeholder":
                services.AddScoped<IPartyDataProvider, PlaceholderPartyDataProvider>();
                break;
            case "MovexMaster":
                services.AddScoped<IPartyDataProvider, MovexMasterPartyDataProvider>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown MovexDb:PartyDataSource '{settings.PartyDataSource}'. " +
                    "Valid values: 'Placeholder', 'MovexMaster'.");
        }

        return services;
    }

    /// <summary>
    /// Register SQLite audit logging services (ADR-014).
    /// Configures IDbContextFactory&lt;AuditDbContext&gt; and IAuditLogger.
    /// WAL mode must be enabled by the host after DI is built:
    ///   ctx.Database.EnsureCreated();
    ///   ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    /// </summary>
    public static IServiceCollection AddAuditLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContextFactory<AuditDbContext>(options =>
            options.UseSqlite(
                configuration.GetConnectionString("AuditLog")
                ?? "Data Source=./data/audit.db"));

        services.AddScoped<IAuditLogger, AuditLogger>();

        return services;
    }
}
