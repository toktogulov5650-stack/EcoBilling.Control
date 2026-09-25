using System.Text.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Auditing;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.ActivateDistrict;

/// <summary>
/// Activates a district. Idempotent by design (Stage 1 decision): activating an
/// already-active district succeeds as a no-op, without calling
/// <see cref="District.Activate"/>. Domain's own invariant (activating an active
/// district is an error) is unchanged and still enforced -- this handler simply avoids
/// ever reaching that path on a repeat call, so an admin retrying the same request
/// doesn't see a spurious failure.
/// </summary>
/// <remarks>
/// Every call is audited (Stage 7 decision), including the no-op: Before and After are
/// identical in that case, which itself signals "nothing changed" to a reader without
/// needing a separate flag.
/// </remarks>
public sealed class ActivateDistrictHandler(
    IDistrictRepository districts,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ActivateDistrictCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ActivateDistrictCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var district = await districts.GetByIdAsync(command.DistrictId, cancellationToken);

        if (district is null)
        {
            return await FailAsync(command, DistrictErrors.NotFound, now, cancellationToken, entityId: null, before: null);
        }

        var entityId = district.Id.Value.ToString();
        var before = Snapshot(district);

        if (district.IsActive)
        {
            WriteSuccessEntry(command, entityId, before, before, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(Unit.Value);
        }

        var activateResult = district.Activate(now);

        if (activateResult.IsFailure)
        {
            return await FailAsync(command, activateResult.Error, now, cancellationToken, entityId, before);
        }

        WriteSuccessEntry(command, entityId, before, Snapshot(district), now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Unit.Value);
    }

    private void WriteSuccessEntry(ActivateDistrictCommand command, string entityId, string before, string after, DateTimeOffset now) =>
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.DistrictActivated,
            "District",
            entityId,
            before,
            after,
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

    private async Task<Result<Unit>> FailAsync(
        ActivateDistrictCommand command,
        Error error,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string? entityId,
        string? before)
    {
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.DistrictActivateFailed,
            "District",
            entityId,
            before,
            JsonSerializer.Serialize(new { errorCode = error.Code }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Failure<Unit>(error);
    }

    private static string Snapshot(District district) =>
        JsonSerializer.Serialize(new DistrictAuditSnapshot(
            district.Code, district.Name, district.ApiBaseUrl.ToString(), district.Status.ToString()));
}
