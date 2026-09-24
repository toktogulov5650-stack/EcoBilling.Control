using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Districts.ResolveDistrict;

namespace EcoBilling.Control.Api.Extensions;

/// <summary>Composition-root wiring for the Api project. No business logic lives here.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationHandlers(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<ResolveDistrictQuery, ResolveDistrictResult>, ResolveDistrictHandler>();

        return services;
    }

    /// <summary>
    /// TEMPORARY. Registers <see cref="TemporaryInMemoryDistrictRepository"/> in place of
    /// the real persistence Stage 4 introduces. Remove this method and its call site in
    /// <c>Program.cs</c> once that repository exists.
    /// </summary>
    public static IServiceCollection AddTemporaryInMemoryPersistence(this IServiceCollection services)
    {
        services.AddSingleton<TemporaryInMemoryDistrictRepository>();
        services.AddSingleton<IDistrictRepository>(sp => sp.GetRequiredService<TemporaryInMemoryDistrictRepository>());

        return services;
    }
}
