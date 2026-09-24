using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.CreateDistrict;

/// <summary>Creates a district. Cheap, in-memory checks run before any repository call.</summary>
public sealed class CreateDistrictHandler(
    IDistrictRepository districts,
    IDistrictHostAllowlist allowlist,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateDistrictCommand, CreateDistrictResult>
{
    public async Task<Result<CreateDistrictResult>> HandleAsync(
        CreateDistrictCommand command,
        CancellationToken cancellationToken)
    {
        var codeResult = DistrictCode.Create(command.Code);

        if (codeResult.IsFailure)
        {
            return Result.Failure<CreateDistrictResult>(codeResult.Error);
        }

        var urlResult = TrustedApiUrl.Create(command.ApiBaseUrl);

        if (urlResult.IsFailure)
        {
            return Result.Failure<CreateDistrictResult>(urlResult.Error);
        }

        var url = urlResult.Value;

        if (!allowlist.IsAllowed(url.Host))
        {
            return Result.Failure<CreateDistrictResult>(DistrictErrors.HostNotAllowed);
        }

        var existing = await districts.GetByNormalizedCodeAsync(codeResult.Value.Normalized, cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<CreateDistrictResult>(DistrictErrors.CodeConflict);
        }

        var districtResult = District.Create(DistrictId.New(), codeResult.Value, command.Name, url, clock.UtcNow);

        if (districtResult.IsFailure)
        {
            return Result.Failure<CreateDistrictResult>(districtResult.Error);
        }

        var district = districtResult.Value;

        districts.Add(district);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateDistrictResult(district.Id, district.NormalizedCode));
    }
}
