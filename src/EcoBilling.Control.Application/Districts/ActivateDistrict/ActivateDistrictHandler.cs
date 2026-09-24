using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.ActivateDistrict;

/// <summary>
/// Activates a district. Idempotent by design (Stage 1 decision): activating an
/// already-active district succeeds as a no-op, without calling
/// <see cref="District.Activate"/> or writing anything. Domain's own invariant (activating
/// an active district is an error) is unchanged and still enforced -- this handler simply
/// avoids ever reaching that path on a repeat call, so an admin retrying the same request
/// doesn't see a spurious failure.
/// </summary>
public sealed class ActivateDistrictHandler(
    IDistrictRepository districts,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ActivateDistrictCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ActivateDistrictCommand command, CancellationToken cancellationToken)
    {
        var district = await districts.GetByIdAsync(command.DistrictId, cancellationToken);

        if (district is null)
        {
            return Result.Failure<Unit>(DistrictErrors.NotFound);
        }

        if (district.IsActive)
        {
            return Result.Success(Unit.Value);
        }

        var activateResult = district.Activate(clock.UtcNow);

        if (activateResult.IsFailure)
        {
            return Result.Failure<Unit>(activateResult.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Unit.Value);
    }
}
