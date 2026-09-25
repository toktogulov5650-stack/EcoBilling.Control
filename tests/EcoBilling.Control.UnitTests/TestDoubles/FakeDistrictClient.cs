using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.UnitTests.TestDoubles;

/// <summary>
/// In-memory <see cref="IDistrictClient"/> whose response is scripted per test via
/// <see cref="CreateDirectorResult"/>/<see cref="ResetDirectorPasswordResult"/> -- no real
/// HTTP, matching the same reasoning as every other Fake in this folder: handler tests
/// exercise Application's own orchestration, not a district's internal API.
/// </summary>
internal sealed class FakeDistrictClient : IDistrictClient
{
    public Result<DirectorCreationAcknowledged> CreateDirectorResult { get; set; } =
        Result.Success(new DirectorCreationAcknowledged("director-id"));

    public Result ResetDirectorPasswordResult { get; set; } = Result.Success();

    public List<(District District, CreateDirectorRequest Request, string IdempotencyKey)> CreateDirectorCalls { get; } = [];

    public List<(District District, string DirectorEmail, string IdempotencyKey)> ResetDirectorPasswordCalls { get; } = [];

    public Task<Result<DirectorCreationAcknowledged>> CreateDirectorAsync(
        District district, CreateDirectorRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        CreateDirectorCalls.Add((district, request, idempotencyKey));

        return Task.FromResult(CreateDirectorResult);
    }

    public Task<Result> ResetDirectorPasswordAsync(
        District district, string directorEmail, string idempotencyKey, CancellationToken cancellationToken)
    {
        ResetDirectorPasswordCalls.Add((district, directorEmail, idempotencyKey));

        return Task.FromResult(ResetDirectorPasswordResult);
    }
}
