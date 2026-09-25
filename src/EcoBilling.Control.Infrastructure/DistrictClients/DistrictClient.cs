using System.Net;
using System.Net.Http.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;
using EcoBilling.Control.Domain.Provisioning;
using EcoBilling.Control.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;

namespace EcoBilling.Control.Infrastructure.DistrictClients;

/// <summary>
/// Calls a district's own protected internal API (architecture doc, section 20). Timeout
/// and retry (10s per attempt, 3 attempts total, exponential backoff) are configured on
/// the named <c>HttpClient</c> itself via <c>Microsoft.Extensions.Http.Resilience</c>
/// (Stage 8, section 9.3, registered in <c>ServiceCollectionExtensions</c>) -- this class
/// only builds requests, attaches the service assertion, and maps responses to
/// <see cref="Result"/>; it does not implement retry logic itself.
/// </summary>
public sealed class DistrictClient(
    IHttpClientFactory httpClientFactory,
    IServiceAssertionIssuer serviceAssertionIssuer,
    IHttpContextAccessor httpContextAccessor,
    IClock clock) : IDistrictClient
{
    public const string HttpClientName = "DistrictClient";

    public async Task<Result<DirectorCreationAcknowledged>> CreateDirectorAsync(
        District district, CreateDirectorRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var httpRequest = BuildRequest(
            HttpMethod.Post, district, "/internal/v1/directors", idempotencyKey,
            new { fullName = request.FullName, email = request.Email });

        HttpResponseMessage response;

        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Result.Failure<DirectorCreationAcknowledged>(ProvisioningOperationErrors.DistrictUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The resilience pipeline's own timeout, not the caller's cancellation.
            return Result.Failure<DirectorCreationAcknowledged>(ProvisioningOperationErrors.DistrictUnavailable);
        }

        using (response)
        {
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
    }

    public async Task<Result> ResetDirectorPasswordAsync(
        District district, string directorEmail, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var httpRequest = BuildRequest(
            HttpMethod.Post, district, "/internal/v1/directors/reset-password", idempotencyKey,
            new { email = directorEmail });

        HttpResponseMessage response;

        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Result.Failure(ProvisioningOperationErrors.DistrictUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(ProvisioningOperationErrors.DistrictUnavailable);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }

            return Result.Failure(await MapErrorAsync(response, cancellationToken));
        }
    }

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
