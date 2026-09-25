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
using EcoBilling.Control.Application.Provisioning.CreateDirector;
using EcoBilling.Control.Application.Provisioning.GetProvisioningOperation;
using EcoBilling.Control.Application.Provisioning.ResetDirectorPassword;
using EcoBilling.Control.Infrastructure;
using EcoBilling.Control.Infrastructure.Auditing;
using EcoBilling.Control.Infrastructure.Authentication;
using EcoBilling.Control.Infrastructure.DistrictClients;
using EcoBilling.Control.Infrastructure.Districts;
using EcoBilling.Control.Infrastructure.Persistence;
using EcoBilling.Control.Infrastructure.Persistence.Administrators;
using EcoBilling.Control.Infrastructure.Persistence.Districts;
using EcoBilling.Control.Infrastructure.Persistence.Provisioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using Polly;

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
        services.AddScoped<ICommandHandler<CreateDirectorCommand, CreateDirectorResult>, CreateDirectorHandler>();
        services.AddScoped<ICommandHandler<ResetDirectorPasswordCommand, ResetDirectorPasswordResult>, ResetDirectorPasswordHandler>();
        services.AddScoped<IQueryHandler<GetProvisioningOperationQuery, GetProvisioningOperationResult>, GetProvisioningOperationHandler>();
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
        services.AddScoped<IProvisioningOperationRepository, ProvisioningOperationRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        return services;
    }

    /// <summary>
    /// Registers the signed service-assertion issuer (Stage 8, section 9.1) and the named
    /// <c>HttpClient</c> <see cref="DistrictClient"/> calls a district's internal API
    /// through, with timeout + retry (10s per attempt, 3 attempts total, exponential
    /// backoff -- section 9.3) attached via <c>Microsoft.Extensions.Http.Resilience</c>.
    /// </summary>
    public static IServiceCollection AddDistrictClient(this IServiceCollection services, IConfiguration configuration)
    {
        var serviceAuthOptions = configuration.GetSection(ServiceAssertionOptions.SectionName).Get<ServiceAssertionOptions>()
            ?? new ServiceAssertionOptions();

        if (string.IsNullOrWhiteSpace(serviceAuthOptions.SigningKeyPem))
        {
            throw new InvalidOperationException(
                $"Missing required configuration '{ServiceAssertionOptions.SectionName}:SigningKeyPem'. " +
                "Set it via configuration or an environment variable; never commit a real one to appsettings.json.");
        }

        services.Configure<ServiceAssertionOptions>(configuration.GetSection(ServiceAssertionOptions.SectionName));
        services.AddSingleton<IServiceAssertionIssuer, ServiceAssertionIssuer>();
        services.AddScoped<IDistrictClient, DistrictClient>();

        services.AddHttpClient(DistrictClient.HttpClientName)
            .AddResilienceHandler("district-client", builder =>
            {
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2, // + the first attempt = 3 attempts total.
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(1),
                    UseJitter = true,
                });

                // Inside the retry (added second = inner), so every individual attempt
                // -- including each retry -- gets its own fresh 10-second budget, rather
                // than one 10-second budget shared across all 3 attempts.
                builder.AddTimeout(TimeSpan.FromSeconds(10));
            });

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
