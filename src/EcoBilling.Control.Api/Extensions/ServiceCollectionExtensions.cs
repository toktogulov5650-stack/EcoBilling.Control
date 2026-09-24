using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Districts.ActivateDistrict;
using EcoBilling.Control.Application.Districts.CreateDistrict;
using EcoBilling.Control.Application.Districts.DeactivateDistrict;
using EcoBilling.Control.Application.Districts.ResolveDistrict;
using EcoBilling.Control.Application.Districts.UpdateDistrict;
using EcoBilling.Control.Infrastructure;
using EcoBilling.Control.Infrastructure.Districts;
using EcoBilling.Control.Infrastructure.Persistence;
using EcoBilling.Control.Infrastructure.Persistence.Districts;
using Microsoft.EntityFrameworkCore;

namespace EcoBilling.Control.Api.Extensions;

/// <summary>Composition-root wiring for the Api project. No business logic lives here.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationHandlers(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<ResolveDistrictQuery, ResolveDistrictResult>, ResolveDistrictHandler>();
        services.AddScoped<ICommandHandler<CreateDistrictCommand, CreateDistrictResult>, CreateDistrictHandler>();
        services.AddScoped<ICommandHandler<UpdateDistrictCommand, Unit>, UpdateDistrictHandler>();
        services.AddScoped<ICommandHandler<ActivateDistrictCommand, Unit>, ActivateDistrictHandler>();
        services.AddScoped<ICommandHandler<DeactivateDistrictCommand, Unit>, DeactivateDistrictHandler>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }

    /// <summary>Registers <see cref="ControlDbContext"/> and its PostgreSQL-backed repositories.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Missing required connection string 'ConnectionStrings:Database'. " +
                "Set it via configuration or the ConnectionStrings__Database environment variable; " +
                "never commit a real one to appsettings.json.");

        services.AddDbContext<ControlDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDistrictRepository, DistrictRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        return services;
    }

    /// <summary>Registers the configured district host allowlist (Q18).</summary>
    public static IServiceCollection AddDistrictHostAllowlist(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DistrictHostAllowlistOptions>(configuration.GetSection(DistrictHostAllowlistOptions.SectionName));
        services.AddSingleton<IDistrictHostAllowlist, DistrictHostAllowlist>();

        return services;
    }
}
