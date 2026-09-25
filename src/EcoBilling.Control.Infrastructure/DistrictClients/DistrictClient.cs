using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;
using EcoBilling.Control.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace EcoBilling.Control.Infrastructure.DistrictClients;

/// <summary>
/// Calls a district's own protected internal API (architecture doc, section 20). Timeout
/// and retry (10s per attempt, 3 attempts total, exponential backoff) are configured on
/// the named <c>HttpClient</c> itself via <c>Microsoft.Extensions.Http.Resilience</c>
/// (Stage 8, section 9.3, registered in <c>ServiceCollectionExtensions</c>). A per-district
/// circuit breaker (Stage 12 follow-up, Q17) wraps that already-retried call: 5
/// consecutive failed logical calls to the same district trip a 30-second break for that
/// district only -- built directly with Polly (<see cref="ResiliencePipelineBuilder{TResult}"/>),
/// not through the shared HttpClient pipeline, because that pipeline is one instance
/// shared by every district; a breaker built into it would trip for all districts at
/// once the moment any single one started failing. This class still only builds
/// requests, attaches the service assertion, and maps responses to <see cref="Result"/>
/// otherwise -- it does not implement retry logic itself.
/// </summary>
public sealed class DistrictClient(
    IHttpClientFactory httpClientFactory,
    IServiceAssertionIssuer serviceAssertionIssuer,
    IHttpContextAccessor httpContextAccessor,
    IClock clock) : IDistrictClient
{
    public const string HttpClientName = "DistrictClient";

    /// <summary>
    /// One circuit breaker pipeline per district, keyed by <see cref="District.NormalizedCode"/>
    /// and kept for the lifetime of the process -- <see cref="DistrictClient"/> itself is
    /// registered scoped (a fresh instance per call), so breaker state has to live
    /// somewhere longer-lived than an instance field to mean anything across calls.
    /// </summary>
    private static readonly ConcurrentDictionary<string, ResiliencePipeline<HttpResponseMessage>> CircuitBreakersByDistrict = new();

    public async Task<Result<DirectorCreationAcknowledged>> CreateDirectorAsync(
        District district, CreateDirectorRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var httpRequest = BuildRequest(
            HttpMethod.Post, district, "/internal/v1/directors", idempotencyKey,
            new { fullName = request.FullName, email = request.Email });

        var sendResult = await SendAsync(district, httpRequest, cancellationToken);

        if (sendResult.IsFailure)
        {
            return Result.Failure<DirectorCreationAcknowledged>(sendResult.Error);
        }

        using var response = sendResult.Value;

        // 200 OK: an idempotent replay of an already-processed request (architecture
        // doc, section 20.2 -- "operation.already_processed", accepted as a success
        // with the original result, not treated as an error). 201 Created: created
        // just now. Both carry the same response shape; Control does not need to
        // distinguish them any further than the status code already does.
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadFromJsonAsync<DirectorCreationResponseBody>(cancellationToken);

            return body is null
                ? Result.Failure<DirectorCreationAcknowledged>(ProvisioningOperationErrors.DistrictUnavailable)
                : Result.Success(new DirectorCreationAcknowledged(body.DirectorId));
        }

        return Result.Failure<DirectorCreationAcknowledged>(await MapErrorAsync(response, cancellationToken));
    }

    public async Task<Result> ResetDirectorPasswordAsync(
        District district, string directorEmail, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var httpRequest = BuildRequest(
            HttpMethod.Post, district, "/internal/v1/directors/reset-password", idempotencyKey,
            new { email = directorEmail });

        var sendResult = await SendAsync(district, httpRequest, cancellationToken);

        if (sendResult.IsFailure)
        {
            return Result.Failure(sendResult.Error);
        }

        using (var response = sendResult.Value)
        {
            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }

            return Result.Failure(await MapErrorAsync(response, cancellationToken));
        }
    }

    /// <summary>
    /// Sends the request through that district's own circuit breaker, wrapping the
    /// already-retried call the shared HttpClient pipeline makes -- one "attempt" from
    /// the breaker's perspective is one fully-retried logical call (up to 3 raw HTTP
    /// attempts already exhausted internally), not one raw HTTP attempt. Retrying
    /// against an open circuit would defeat the point of having one.
    /// </summary>
    private async Task<Result<HttpResponseMessage>> SendAsync(
        District district, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await GetCircuitBreaker(district.NormalizedCode).ExecuteAsync(
                async ct => await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct),
                cancellationToken);

            return Result.Success(response);
        }
        catch (HttpRequestException)
        {
            return Result.Failure<HttpResponseMessage>(ProvisioningOperationErrors.DistrictUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The resilience pipeline's own timeout, not the caller's cancellation.
            return Result.Failure<HttpResponseMessage>(ProvisioningOperationErrors.DistrictUnavailable);
        }
        catch (TimeoutRejectedException)
        {
            // Polly's own timeout-strategy exception, surfaced after the shared
            // pipeline's retries (Stage 8) are exhausted.
            return Result.Failure<HttpResponseMessage>(ProvisioningOperationErrors.DistrictUnavailable);
        }
        catch (BrokenCircuitException)
        {
            // The circuit for this specific district is currently open (Q17, Stage 12
            // follow-up) -- from the caller's point of view this is exactly the same
            // outcome as the district being unreachable, which is what tripped the
            // breaker in the first place.
            return Result.Failure<HttpResponseMessage>(ProvisioningOperationErrors.DistrictUnavailable);
        }
    }

    /// <summary>
    /// 5 consecutive failed logical calls to this district trip a 30-second break for
    /// this district only (Q17, matching the original Stage 0 recommendation). Polly v8
    /// has no literal "N consecutive failures" mode -- FailureRatio 1.0 with
    /// MinimumThroughput 5 is its documented equivalent: the breaker only trips if the
    /// last 5-or-more calls within the sampling window failed with none succeeding in
    /// between, since a single success would drop the ratio below 1.0.
    /// </summary>
    private static ResiliencePipeline<HttpResponseMessage> GetCircuitBreaker(string normalizedDistrictCode) =>
        CircuitBreakersByDistrict.GetOrAdd(normalizedDistrictCode, static _ => new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(response => (int)response.StatusCode >= 500),
                FailureRatio = 1.0,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
            })
            .Build());

    private HttpRequestMessage BuildRequest(
        HttpMethod method, District district, string path, string idempotencyKey, object body)
    {
        var request = new HttpRequestMessage(method, district.ApiBaseUrl + path)
        {
            Content = JsonContent.Create(body),
        };

        var now = clock.UtcNow;
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", serviceAssertionIssuer.Issue(district, now));
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString());

        return request;
    }

    private static async Task<Error> MapErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return ProvisioningOperationErrors.ServiceUnauthorized;
        }

        DistrictErrorResponseBody? body;

        try
        {
            body = await response.Content.ReadFromJsonAsync<DistrictErrorResponseBody>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            body = null;
        }

        return body?.Code switch
        {
            "director.already_exists" => ProvisioningOperationErrors.DirectorAlreadyExists,
            "director.not_found" => ProvisioningOperationErrors.DirectorNotFound,
            "validation.failed" => ProvisioningOperationErrors.ValidationFailed,
            "service.unauthorized" => ProvisioningOperationErrors.ServiceUnauthorized,
            _ => ProvisioningOperationErrors.DistrictUnavailable,
        };
    }

    /// <summary>The internal contract's success response body (architecture doc, section 20).</summary>
    private sealed record DirectorCreationResponseBody(string DirectorId, string OperationId, string Status);

    /// <summary>The internal contract's error envelope (architecture doc, section 20).</summary>
    private sealed record DistrictErrorResponseBody(string Code, string? Message);
}
