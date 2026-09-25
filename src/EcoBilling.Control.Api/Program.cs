using System.Diagnostics;
using EcoBilling.Control.Api.Endpoints.Administration;
using EcoBilling.Control.Api.Endpoints.Public;
using EcoBilling.Control.Api.Extensions;
using EcoBilling.Control.Api.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddApplicationHandlers();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddDistrictHostAllowlist(builder.Configuration);
builder.Services.AddAdministratorAuthentication(builder.Configuration);
builder.Services.AddAuditing();
builder.Services.AddDistrictClient(builder.Configuration);
builder.Services.AddApiRateLimiting();
builder.Services.AddDistrictCache();
builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres");

var app = builder.Build();

// Fails fast, the same pattern as the other required-configuration checks in this
// codebase (connection string, JWT signing key, service-assertion key): a wildcard
// AllowedHosts is fine for local development, where the actual deployment hostname
// isn't known yet either, but shipping it to production would defeat the Host-header
// validation this setting exists for (architecture doc, section 21.10; Stage 15).
// The real production hostname is not invented here -- deploying to production means
// explicitly setting AllowedHosts (env var override), not inheriting this default.
if (app.Environment.IsProduction() && app.Configuration["AllowedHosts"] is null or "*")
{
    throw new InvalidOperationException(
        "Missing required configuration 'AllowedHosts' for a Production deployment. " +
        "Set it via configuration or the AllowedHosts environment variable to the actual " +
        "hostname(s) this instance is served under; the wildcard default is only safe for Development.");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Only when Kestrel actually has an HTTPS endpoint configured (Stage 15): in the
// container, ASPNETCORE_HTTP_PORTS is the only port set (deploy/Dockerfile.api) --
// TLS termination happens at a reverse proxy in front of the container, not inside it.
// Calling this unconditionally there had this middleware trying to redirect every
// plain-HTTP request to an HTTPS port that was never configured.
if (!string.IsNullOrWhiteSpace(app.Configuration["ASPNETCORE_HTTPS_PORTS"]))
{
    app.UseHttpsRedirection();
}

// Structured request logging (Stage 14), deliberately minimal rather than the built-in
// HttpLogging middleware's broader surface: this only ever touches method, path, status
// code and duration -- never headers or bodies -- so there is no configuration switch
// anyone could later flip to start logging the Authorization header or a login/
// CreateDirector request body. The same secret-redaction discipline applied everywhere
// else in this codebase (passwords, tokens, signing keys never logged) is enforced here
// by construction, not by an allow-list that has to be kept correct over time.
app.Use(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();

    await next();

    app.Logger.LogInformation(
        "{Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode,
        stopwatch.ElapsedMilliseconds);
});

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("Health");

// Distinct from /health (Stage 14): proves PostgreSQL is actually reachable, not just
// that the process is running. An orchestrator should stop routing traffic here on a
// failed /ready without restarting the process the way a failed /health (liveness) would.
app.MapHealthChecks("/ready")
    .WithName("Ready");

app.MapResolveDistrict();
app.MapAdministratorAuthEndpoints();
app.MapWhoAmI();
app.MapDistrictAdministrationEndpoints();
app.MapAuditEndpoints();
app.MapProvisioningEndpoints();

app.Run();

public partial class Program;
