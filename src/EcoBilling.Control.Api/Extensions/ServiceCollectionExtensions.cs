using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using EcoBilling.Control.Api.Cors;
using EcoBilling.Control.Api.Endpoints;
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
using EcoBilling.Control.Infrastructure.Caching;
using EcoBilling.Control.Infrastructure.DistrictClients;
using EcoBilling.Control.Infrastructure.Districts;
using EcoBilling.Control.Infrastructure.Persistence;
using EcoBilling.Control.Infrastructure.Persistence.Administrators;
using EcoBilling.Control.Infrastructure.Persistence.Districts;
using EcoBilling.Control.Infrastructure.Persistence.Provisioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
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

    /// <summary>Registers the in-memory resolve cache (Stage 12, Q13).</summary>
    public static IServiceCollection AddDistrictCache(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<ICache, InMemoryCache>();

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

    /// <summary>
    /// Registers the two named IP-partitioned rate-limiting policies this deployment
    /// currently needs (Stage 10): resolves the "not ready for real/public traffic"
    /// flag the architecture doc carried on both endpoints since Stage 3/6. Partitioned
    /// by <see cref="System.Net.HttpConnectionInfo.RemoteIpAddress"/> -- correct only for
    /// a directly internet-facing deployment; if Control ever sits behind a reverse
    /// proxy or load balancer, every request will appear to come from the proxy's
    /// address unless ASP.NET Core's ForwardedHeaders middleware is separately
    /// configured and trusted. Not addressed here: the deployment topology itself is
    /// still an open question (architecture doc, section 3.1).
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ApiErrorResponse(
                        "rate_limit.exceeded",
                        "Too many requests. Please retry later.",
                        context.HttpContext.TraceIdentifier),
                    cancellationToken);
            };

            // Fixed window: raw request volume on the login endpoint itself, regardless
            // of which email each request targets -- catches distributed guessing across
            // many accounts from one source, which per-account lockout cannot.
            options.AddPolicy("admin-login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(httpContext),
                _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 20,
                    QueueLimit = 0,
                }));

            // Sliding, not fixed: ResolveDistrict has no account concept to rate-limit
            // by, and a sliding window avoids the fixed-window boundary-burst edge case
            // (2x the permitted rate at a window boundary) that a fixed window would allow.
            options.AddPolicy("resolve-district", httpContext => RateLimitPartition.GetSlidingWindowLimiter(
                ClientIp(httpContext),
                _ => new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    PermitLimit = 30,
                    QueueLimit = 0,
                }));
        });

        return services;
    }

    private static string ClientIp(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>Name of the single CORS policy this API registers, applied globally via <c>UseCors</c>.</summary>
    public const string CorsPolicyName = "Control";

    /// <summary>
    /// Registers the CORS policy required before any browser-hosted frontend can call this
    /// API cross-origin (Stage 18). Deny-by-default in every environment except one
    /// specific case: Development, with no <c>Cors:AllowedOrigins</c> configured at all,
    /// where any origin is allowed -- a local frontend dev server's port changes often
    /// enough that requiring it to be listed here would be pure friction, and Development
    /// is not internet-facing. An explicit list always wins, even in Development: a
    /// developer who deliberately configures it locally to rehearse the Production
    /// behavior is not silently overridden back to permissive. This API authenticates with
    /// a bearer token in the Authorization header, not cookies, so the policy never needs
    /// <c>AllowCredentials()</c> -- which cannot be combined with <c>AllowAnyOrigin()</c> anyway.
    /// </summary>
    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var corsOptions = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();

        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
        {
            if (corsOptions.AllowedOrigins.Length > 0)
            {
                policy.WithOrigins(corsOptions.AllowedOrigins).AllowAnyHeader().AllowAnyMethod();

                return;
            }

            if (environment.IsDevelopment())
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();

                return;
            }

            // Deny by default: an empty WithOrigins() call throws, so the policy is left
            // with nothing added -- every cross-origin browser request is rejected.
        }));

        return services;
    }
}
