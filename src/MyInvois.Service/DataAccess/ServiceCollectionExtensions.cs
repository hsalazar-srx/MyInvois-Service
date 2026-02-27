namespace MyInvois.Service.DataAccess;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyInvois.Service.Configuration;

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
}
