using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.UpdateDistrict;

/// <summary>
/// Applies a partial update to a district. Validation runs before any mutation, and
/// nothing persists until the final <see cref="IUnitOfWork.SaveChangesAsync"/> call, so a
/// failure partway through (e.g. a valid new name but a disallowed new host) leaves the
/// tracked entity's in-memory changes unsaved rather than requiring an explicit rollback.
/// </summary>
public sealed class UpdateDistrictHandler(
    IDistrictRepository districts,
    IDistrictHostAllowlist allowlist,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<UpdateDistrictCommand, Unit>
{
    private static readonly Error NothingToUpdate = new(
        "validation.failed",
        "At least one of name or apiBaseUrl must be supplied.");

    public async Task<Result<Unit>> HandleAsync(UpdateDistrictCommand command, CancellationToken cancellationToken)
    {
        if (command.Name is null && command.ApiBaseUrl is null)
        {
            return Result.Failure<Unit>(NothingToUpdate);
        }

        var district = await districts.GetByIdAsync(command.DistrictId, cancellationToken);

        if (district is null)
        {
            return Result.Failure<Unit>(DistrictErrors.NotFound);
        }

        if (command.Name is not null)
        {
            var renameResult = district.Rename(command.Name, clock.UtcNow);

            if (renameResult.IsFailure)
            {
                return Result.Failure<Unit>(renameResult.Error);
            }
        }

        if (command.ApiBaseUrl is not null)
        {
            var urlResult = TrustedApiUrl.Create(command.ApiBaseUrl);

            if (urlResult.IsFailure)
            {
                return Result.Failure<Unit>(urlResult.Error);
            }

            var url = urlResult.Value;

            if (!allowlist.IsAllowed(url.Host))
            {
                return Result.Failure<Unit>(DistrictErrors.HostNotAllowed);
            }

            district.ChangeApiBaseUrl(url, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Unit.Value);
    }
}
