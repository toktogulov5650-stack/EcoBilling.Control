using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Infrastructure.Authentication;
using EcoBilling.Control.Infrastructure.DistrictClients;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EcoBilling.Control.IntegrationTests.DistrictClients;

/// <summary>
/// Verifies <see cref="DistrictClient"/>'s request building and response mapping against
/// a fake <see cref="HttpMessageHandler"/> -- no real network call and, deliberately, no
/// real district server: <see cref="TrustedApiUrl"/> (Stage 1's SSRF protection) rejects
/// loopback/localhost addresses outright, so there is no legitimate
/// <see cref="District.ApiBaseUrl"/> a locally-hosted fake server could use anyway. A
/// fake transport handler tests exactly the same request/response mapping logic a real
/// network call would exercise, without that conflict. Timeout and retry
/// (Microsoft.Extensions.Http.Resilience) are wired only in DI (ServiceCollectionExtensions),
/// not here -- this class is tested directly, bypassing that pipeline, the same way
/// <c>ServiceAssertionIssuerTests</c> tests the issuer directly.
/// </summary>
public sealed class DistrictClientTests
{
    private const string SigningKeyPem = """
        -----BEGIN EC PRIVATE KEY-----
        MHcCAQEEIM/Zmsn7TVLlASCs4fwrBHO+Z4+RMX0S/798BLR39S9soAoGCCqGSM49
        AwEHoUQDQgAErNpguOsMTV/j7ZfEW7Y4Lbtbl1gwdXQ7kEB9ZQWhTxZDRvANZoN2
        9tsaUpXl4W92V86YoF+I9ffOdEDkDNVhYA==
        -----END EC PRIVATE KEY-----
        """;

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private static District NewDistrict(string code = "BISHKEK-01") =>
        District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value,
            "Bishkek district",
            TrustedApiUrl.Create("https://district-01.example.com/api").Value,
            Now).Value;

    private sealed class RecordingHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return respond(request, LastRequestBody);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private static DistrictClient NewClient(HttpMessageHandler handler) =>
        new(
            new StubHttpClientFactory(handler),
            new ServiceAssertionIssuer(Options.Create(new ServiceAssertionOptions { SigningKeyPem = SigningKeyPem })),
            new HttpContextAccessor(),
            new StubClock(Now));

    private sealed class StubClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body) =>
        new(statusCode) { Content = JsonContent.Create(body) };

    [Fact]
    public async Task CreateDirectorAsync_SendsExpectedMethodPathBodyAndHeaders()
    {
        var handler = new RecordingHandler((_, _) => JsonResponse(
            HttpStatusCode.Created, new { directorId = "director-1", operationId = "op-1", status = "created" }));
        var district = NewDistrict();
        var client = NewClient(handler);

        await client.CreateDirectorAsync(district, new CreateDirectorRequest("Director Name", "director@example.com"), "idem-key-1", CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal("https://district-01.example.com/api/internal/v1/directors", handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("\"fullName\":\"Director Name\"", handler.LastRequestBody);
        Assert.Contains("\"email\":\"director@example.com\"", handler.LastRequestBody);
        Assert.Equal("idem-key-1", handler.LastRequest.Headers.GetValues("Idempotency-Key").Single());
        Assert.NotEmpty(handler.LastRequest.Headers.GetValues("X-Correlation-Id"));

        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Equal("ecobilling-control", jwt.Issuer);
        Assert.Contains(district.NormalizedCode, jwt.Audiences);
    }

    [Fact]
    public async Task CreateDirectorAsync_On201Created_ReturnsTheDirectorId()
    {
        var handler = new RecordingHandler((_, _) => JsonResponse(
            HttpStatusCode.Created, new { directorId = "director-1", operationId = "op-1", status = "created" }));
        var client = NewClient(handler);

        var result = await client.CreateDirectorAsync(
            NewDistrict(), new CreateDirectorRequest("Director", "director@example.com"), "idem-key-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("director-1", result.Value.DirectorId);
    }

    [Fact]
    public async Task CreateDirectorAsync_On200OkIdempotentReplay_IsAcceptedAsSuccess_NotAnError()
    {
        // architecture doc, section 20.2: operation.already_processed is an accepted
        // result, not an error -- folded here into "200 OK carries the same success body
        // 201 would have", so DistrictClient needs no special-case branch for it at all.
        var handler = new RecordingHandler((_, _) => JsonResponse(
            HttpStatusCode.OK, new { directorId = "director-1", operationId = "op-1", status = "created" }));
        var client = NewClient(handler);

        var result = await client.CreateDirectorAsync(
            NewDistrict(), new CreateDirectorRequest("Director", "director@example.com"), "idem-key-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("director-1", result.Value.DirectorId);
    }

    [Fact]
    public async Task CreateDirectorAsync_On409DirectorAlreadyExists_MapsToThatError()
    {
        var handler = new RecordingHandler((_, _) => JsonResponse(
            HttpStatusCode.Conflict, new { code = "director.already_exists", message = "A director already exists." }));
        var client = NewClient(handler);

        var result = await client.CreateDirectorAsync(
            NewDistrict(), new CreateDirectorRequest("Director", "director@example.com"), "idem-key-1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("director.already_exists", result.Error.Code);
    }

    [Fact]
    public async Task CreateDirectorAsync_On401_MapsToServiceUnauthorized_RegardlessOfBody()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = NewClient(handler);

        var result = await client.CreateDirectorAsync(
            NewDistrict(), new CreateDirectorRequest("Director", "director@example.com"), "idem-key-1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("service.unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task CreateDirectorAsync_On400ValidationFailed_MapsToThatError()
    {
        var handler = new RecordingHandler((_, _) => JsonResponse(
            HttpStatusCode.BadRequest, new { code = "validation.failed", message = "Invalid email." }));
        var client = NewClient(handler);

        var result = await client.CreateDirectorAsync(
            NewDistrict(), new CreateDirectorRequest("Director", "not-an-email"), "idem-key-1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation.failed", result.Error.Code);
    }

    [Fact]
    public async Task CreateDirectorAsync_OnAnUnrecognizedServerError_MapsToDistrictUnavailable()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = NewClient(handler);

        var result = await client.CreateDirectorAsync(
            NewDistrict(), new CreateDirectorRequest("Director", "director@example.com"), "idem-key-1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.unavailable", result.Error.Code);
    }

    [Fact]
    public async Task CreateDirectorAsync_WhenTheTransportThrows_MapsToDistrictUnavailable()
    {
        var client = NewClient(new ThrowingHandler(new HttpRequestException("connection refused")));

        var result = await client.CreateDirectorAsync(
            NewDistrict(), new CreateDirectorRequest("Director", "director@example.com"), "idem-key-1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("district.unavailable", result.Error.Code);
    }

    [Fact]
    public async Task ResetDirectorPasswordAsync_SendsExpectedPathAndBody_AndSucceeds()
    {
        var handler = new RecordingHandler((_, _) => JsonResponse(
            HttpStatusCode.Accepted, new { operationId = "op-2", status = "in_progress" }));
        var client = NewClient(handler);

        var result = await client.ResetDirectorPasswordAsync(NewDistrict(), "director@example.com", "idem-key-2", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://district-01.example.com/api/internal/v1/directors/reset-password", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("\"email\":\"director@example.com\"", handler.LastRequestBody);
        Assert.Equal("idem-key-2", handler.LastRequest.Headers.GetValues("Idempotency-Key").Single());
    }

    [Fact]
    public async Task ResetDirectorPasswordAsync_On404DirectorNotFound_MapsToThatError()
    {
        var handler = new RecordingHandler((_, _) => JsonResponse(
            HttpStatusCode.NotFound, new { code = "director.not_found", message = "No such director." }));
        var client = NewClient(handler);

        var result = await client.ResetDirectorPasswordAsync(NewDistrict(), "unknown@example.com", "idem-key-2", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("director.not_found", result.Error.Code);
    }

    // Circuit breaker tests (Q17 follow-up) deliberately use their own district codes,
    // never reused elsewhere in this file: CircuitBreakersByDistrict is static and keyed
    // by NormalizedCode, so a test that trips a breaker for "BISHKEK-01" would otherwise
    // leak that state into every other test in this class using the same default code.

    [Fact]
    public async Task CreateDirectorAsync_AfterFiveConsecutiveServerErrors_TripsTheCircuitBreaker_WithoutAnySixthCallReachingTheHandler()
    {
        var callCount = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            callCount++;

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        var client = NewClient(handler);
        var district = NewDistrict("CIRCBREAK-01");

        for (var i = 0; i < 5; i++)
        {
            var result = await client.CreateDirectorAsync(
                district, new CreateDirectorRequest("Director", "director@example.com"), $"idem-{i}", CancellationToken.None);
            Assert.Equal("district.unavailable", result.Error.Code);
        }

        Assert.Equal(5, callCount); // sanity: all 5 genuinely reached the handler.

        var sixthResult = await client.CreateDirectorAsync(
            district, new CreateDirectorRequest("Director", "director@example.com"), "idem-6", CancellationToken.None);

        Assert.True(sixthResult.IsFailure);
        Assert.Equal("district.unavailable", sixthResult.Error.Code);
        Assert.Equal(5, callCount); // unchanged -- the open breaker short-circuited before the handler was ever invoked.
    }

    [Fact]
    public async Task CreateDirectorAsync_ATrippedBreakerForOneDistrict_DoesNotAffectAnotherDistrict()
    {
        var failingHandler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var failingClient = NewClient(failingHandler);
        var failingDistrict = NewDistrict("CIRCBREAK-02");

        for (var i = 0; i < 6; i++) // 5 to trip, a 6th to prove it stays open.
        {
            await failingClient.CreateDirectorAsync(
                failingDistrict, new CreateDirectorRequest("Director", "director@example.com"), $"idem-{i}", CancellationToken.None);
        }

        var healthyHandler = new RecordingHandler((_, _) => JsonResponse(
            HttpStatusCode.Created, new { directorId = "director-id", operationId = "op-1", status = "created" }));
        var healthyClient = NewClient(healthyHandler);
        var healthyDistrict = NewDistrict("CIRCBREAK-03");

        var result = await healthyClient.CreateDirectorAsync(
            healthyDistrict, new CreateDirectorRequest("Director", "director@example.com"), "idem-other", CancellationToken.None);

        Assert.True(result.IsSuccess); // a different district's breaker is a separate, never-tripped entry.
    }

    [Fact]
    public async Task CreateDirectorAsync_FourConsecutiveServerErrors_DoNotTripTheBreakerYet()
    {
        var callCount = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            callCount++;

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        var client = NewClient(handler);
        var district = NewDistrict("CIRCBREAK-04");

        for (var i = 0; i < 4; i++)
        {
            await client.CreateDirectorAsync(
                district, new CreateDirectorRequest("Director", "director@example.com"), $"idem-{i}", CancellationToken.None);
        }

        Assert.Equal(4, callCount); // below MinimumThroughput -- the 5th call must still reach the handler, not be short-circuited.
    }

    [Fact]
    public async Task CreateDirectorAsync_BusinessRejections_NeverCountTowardTheCircuitBreaker()
    {
        // 409/400/401/404 are the district correctly responding, not failing --
        // repeated business rejections must never trip the breaker.
        var callCount = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            callCount++;

            return JsonResponse(HttpStatusCode.Conflict, new { code = "director.already_exists", message = "Already exists." });
        });
        var client = NewClient(handler);
        var district = NewDistrict("CIRCBREAK-05");

        for (var i = 0; i < 10; i++)
        {
            await client.CreateDirectorAsync(
                district, new CreateDirectorRequest("Director", "director@example.com"), $"idem-{i}", CancellationToken.None);
        }

        Assert.Equal(10, callCount); // every single call reached the handler -- none were short-circuited.
    }
}
