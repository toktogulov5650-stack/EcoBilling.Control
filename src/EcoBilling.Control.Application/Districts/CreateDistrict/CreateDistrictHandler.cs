using System.Text.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Auditing;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.CreateDistrict;

/// <summary>
/// Creates a district. Cheap, in-memory checks run before any repository call. Both the
/// success and every failure path are audited (Stage 7 decision) -- a failed attempt
/// ("tried to create X, got district.host_not_allowed") is itself useful signal.
/// </summary>
public sealed class CreateDistrictHandler(
    IDistrictRepository districts,
    IDistrictHostAllowlist allowlist,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateDistrictCommand, CreateDistrictResult>
{
    public async Task<Result<CreateDistrictResult>> HandleAsync(
        CreateDistrictCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var codeResult = DistrictCode.Create(command.Code);

        if (codeResult.IsFailure)
        {
            return await FailAsync(command, codeResult.Error, now, cancellationToken);
        }

        var urlResult = TrustedApiUrl.Create(command.ApiBaseUrl);

        if (urlResult.IsFailure)
        {
            return await FailAsync(command, urlResult.Error, now, cancellationToken);
        }

        var url = urlResult.Value;

        if (!allowlist.IsAllowed(url.Host))
        {
            return await FailAsync(command, DistrictErrors.HostNotAllowed, now, cancellationToken);
        }

        var existing = await districts.GetByNormalizedCodeAsync(codeResult.Value.Normalized, cancellationToken);

        if (existing is not null)
        {
            return await FailAsync(command, DistrictErrors.CodeConflict, now, cancellationToken);
        }

        var districtResult = District.Create(DistrictId.New(), codeResult.Value, command.Name, url, now);

        if (districtResult.IsFailure)
        {
            return await FailAsync(command, districtResult.Error, now, cancellationToken);
        }

        var district = districtResult.Value;

        districts.Add(district);

        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.DistrictCreated,
            "District",
            district.Id.Value.ToString(),
            BeforeData: null,
            AfterData: Snapshot(district),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateDistrictResult(district.Id, district.NormalizedCode));
    }

    private async Task<Result<CreateDistrictResult>> FailAsync(
        CreateDistrictCommand command,
        Error error,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.DistrictCreateFailed,
            "District",
            EntityId: null,
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new
            {
                attemptedCode = command.Code,
                attemptedName = command.Name,
                attemptedApiBaseUrl = command.ApiBaseUrl,
                errorCode = error.Code,
            }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Failure<CreateDistrictResult>(error);
    }

    private static string Snapshot(District district) =>
        JsonSerializer.Serialize(new DistrictAuditSnapshot(
            district.Code, district.Name, district.ApiBaseUrl.ToString(), district.Status.ToString()));
}
