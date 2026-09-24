using EcoBilling.Control.Api.Endpoints.Public;
using EcoBilling.Control.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddApplicationHandlers();
builder.Services.AddTemporaryInMemoryPersistence();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("Health");

app.MapResolveDistrict();

app.Run();

public partial class Program;
