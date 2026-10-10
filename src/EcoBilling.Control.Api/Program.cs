using EcoBilling.Control.Api.Endpoints.Administration;
using EcoBilling.Control.Api.Endpoints.Public;
using EcoBilling.Control.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);
var isCloudRun = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("K_SERVICE"));
const string FrontendCorsPolicy = "Frontend";

// Cloud Run injects the port assigned to the ingress container through PORT.
// Keep the Docker/local default when the variable is absent, but never assume that
// Cloud Run will always be configured to use 8080.
var cloudRunPort = Environment.GetEnvironmentVariable("PORT");

if (!string.IsNullOrWhiteSpace(cloudRunPort))
{
    if (!int.TryParse(cloudRunPort, out var port) || port is < 1 or > 65535)
    {
        throw new InvalidOperationException("The PORT environment variable must contain a valid TCP port.");
    }

    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddOpenApi();
var allowedFrontendOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .GetChildren()
    .Select(origin => origin.Value)
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin!)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy =>
{
    if (allowedFrontendOrigins.Length > 0)
    {
        policy.WithOrigins(allowedFrontendOrigins);
    }

    policy
        .WithHeaders("Authorization", "Content-Type", "Accept")
        .WithMethods("GET", "POST", "PUT", "OPTIONS")
        .WithExposedHeaders("Location", "Retry-After")
        .SetPreflightMaxAge(TimeSpan.FromHours(1));
}));
builder.Services.AddApplicationHandlers();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddDistrictHostAllowlist(builder.Configuration);
builder.Services.AddAdministratorAuthentication(builder.Configuration);
builder.Services.AddAuditing();
builder.Services.AddDistrictClient(builder.Configuration);
builder.Services.AddApiRateLimiting();

var app = builder.Build();

// Cloud Run terminates TLS before forwarding requests to the container over HTTP.
// The OpenAPI generator derives its server URL from Request.Scheme, so set the known
// external scheme explicitly instead of trusting a client-controlled forwarded header.
if (isCloudRun)
{
    app.Use((context, next) =>
    {
        context.Request.Scheme = Uri.UriSchemeHttps;

        return next(context);
    });
}

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "EcoBilling Control API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "EcoBilling Control API";
});

// Cloud Run terminates TLS before forwarding the request to the container and already
// redirects public HTTP requests to HTTPS. Redirecting the internal HTTP request again
// would make the application depend on forwarded-header trust configuration.
if (!isCloudRun)
{
    app.UseHttpsRedirection();
}

app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/", () => Results.Redirect("/swagger"))
    .ExcludeFromDescription();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("Health");

app.MapResolveDistrict();
app.MapAdministratorAuthEndpoints();
app.MapWhoAmI();
app.MapDistrictAdministrationEndpoints();
app.MapAuditEndpoints();
app.MapProvisioningEndpoints();

app.Run();

public partial class Program;
