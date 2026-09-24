using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.DeactivateDistrict;

/// <summary>
/// Deactivates a district. Idempotent by design, symmetric with
/// <see cref="EcoBilling.Control.Application.Districts.ActivateDistrict.ActivateDistrictHandler"/>:
/// deactivating an already-inactive district succeeds as a no-op.
/// </summary>
public sealed class DeactivateDistrictHandler(
    IDistrictRepository districts,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<DeactivateDistrictCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeactivateDistrictCommand command, CancellationToken cancellationToken)
    {
        var district = await districts.GetByIdAsync(command.DistrictId, cancellationToken);

        if (district is null)
        {
            return Result.Failure<Unit>(DistrictErrors.NotFound);
        }

        if (!district.IsActive)
        {
            return Result.Success(Unit.Value);
        }

        var deactivateResult = district.Deactivate(clock.UtcNow);

        if (deactivateResult.IsFailure)
        {
            return Result.Failure<Unit>(deactivateResult.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Unit.Value);
    }
}
