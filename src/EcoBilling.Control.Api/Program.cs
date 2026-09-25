using EcoBilling.Control.Api.Endpoints.Administration;
using EcoBilling.Control.Api.Endpoints.Public;
using EcoBilling.Control.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddApplicationHandlers();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddDistrictHostAllowlist(builder.Configuration);
builder.Services.AddAdministratorAuthentication(builder.Configuration);
builder.Services.AddAuditing();
builder.Services.AddDistrictClient(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

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
