using System.Text;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Administrators.CreateAdministrator;
using EcoBilling.Control.Application.Administrators.Login;
using EcoBilling.Control.Application.Administrators.LogoutAdministratorSession;
using EcoBilling.Control.Application.Administrators.RefreshAdministratorSession;
using EcoBilling.Control.Application.Auditing.ListAuditEntries;
using EcoBilling.Control.Application.Districts.ActivateDistrict;
using EcoBilling.Control.Application.Districts.CreateDistrict;
using EcoBilling.Control.Application.Districts.DeactivateDistrict;
using EcoBilling.Control.Application.Districts.GetDistrict;
using EcoBilling.Control.Application.Districts.ListDistricts;
using EcoBilling.Control.Application.Districts.ResolveDistrict;
using EcoBilling.Control.Application.Districts.UpdateDistrict;
using EcoBilling.Control.Infrastructure;
using EcoBilling.Control.Infrastructure.Auditing;
using EcoBilling.Control.Infrastructure.Authentication;
using EcoBilling.Control.Infrastructure.Districts;
using EcoBilling.Control.Infrastructure.Persistence;
using EcoBilling.Control.Infrastructure.Persistence.Administrators;
using EcoBilling.Control.Infrastructure.Persistence.Districts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

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
        services.AddScoped<ICommandHandler<AdministratorLoginCommand, AdministratorLoginResult>, AdministratorLoginHandler>();
        services.AddScoped<ICommandHandler<RefreshAdministratorSessionCommand, RefreshAdministratorSessionResult>, RefreshAdministratorSessionHandler>();
        services.AddScoped<ICommandHandler<LogoutAdministratorSessionCommand, Unit>, LogoutAdministratorSessionHandler>();
        services.AddScoped<ICommandHandler<CreateAdministratorCommand, CreateAdministratorResult>, CreateAdministratorHandler>();
        services.AddScoped<IQueryHandler<GetDistrictQuery, GetDistrictResult>, GetDistrictHandler>();
        services.AddScoped<IQueryHandler<ListDistrictsQuery, ListDistrictsResult>, ListDistrictsHandler>();
        services.AddScoped<IQueryHandler<ListAuditEntriesQuery, ListAuditEntriesResult>, ListAuditEntriesHandler>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }

    /// <summary>Registers the audit trail writer/reader (Stage 7) and the ambient <see cref="IHttpContextAccessor"/> it enriches entries from.</summary>
    public static IServiceCollection AddAuditing(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditRepository, AuditRepository>();

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
        services.AddScoped<IAdministratorRepository, AdministratorRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
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

    /// <summary>
    /// Registers password hashing, JWT access-token issuance, the JWT Bearer
    /// authentication scheme that validates incoming tokens, and the named
    /// <c>SystemAdmin</c> authorization policy.
    /// </summary>
    /// <remarks>
    /// <c>SystemAdmin</c> is registered as a distinct named policy even though it is
    /// today equivalent to bare authentication: there is exactly one kind of
    /// authenticated principal in this system. Naming it lets a future role split
    /// tighten this one policy definition instead of every endpoint's attribute.
    /// </remarks>
    public static IServiceCollection AddAdministratorAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
        {
            throw new InvalidOperationException(
                $"Missing required configuration '{JwtOptions.SectionName}:SigningKey'. " +
                "Set it via configuration or an environment variable; never commit a real one to appsettings.json.");
        }

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearerOptions =>
            {
                // Without this, the handler remaps short claim names ("sub", "email")
                // to legacy long-form XML-namespace claim types by default -- a
                // well-known .NET gotcha. Disabled so ClaimsPrincipal exposes exactly
                // the claim names JwtAccessTokenIssuer put in the token.
                bearerOptions.MapInboundClaims = false;

                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    // Deliberately tight, not JwtBearer's 5-minute default: the access
                    // token itself lives only 15 minutes, so a 5-minute skew would let
                    // an "expired" token keep working for a third of its own lifetime.
                    // Issuer and validator are the same process/deployment, so clock
                    // drift between them should be near zero.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("SystemAdmin", policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("token_type", "access"));

        return services;
    }
}
