using System.Text.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Auditing;
using EcoBilling.Control.Application.Districts;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.DeactivateDistrict;

/// <summary>
/// Deactivates a district. Idempotent by design, symmetric with
/// <see cref="EcoBilling.Control.Application.Districts.ActivateDistrict.ActivateDistrictHandler"/>:
/// deactivating an already-inactive district succeeds as a no-op, and every call is
/// audited (Stage 7), including that no-op.
/// </summary>
public sealed class DeactivateDistrictHandler(
    IDistrictRepository districts,
    IAuditWriter auditWriter,
    ICache cache,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<DeactivateDistrictCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeactivateDistrictCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var district = await districts.GetByIdAsync(command.DistrictId, cancellationToken);

        if (district is null)
        {
            return await FailAsync(command, DistrictErrors.NotFound, now, cancellationToken, entityId: null, before: null);
        }

        var entityId = district.Id.Value.ToString();
        var before = Snapshot(district);

        if (!district.IsActive)
        {
            WriteSuccessEntry(command, entityId, before, before, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await cache.RemoveAsync(DistrictCacheKeys.Resolve(district.NormalizedCode), cancellationToken);

            return Result.Success(Unit.Value);
        }

        var deactivateResult = district.Deactivate(now);

        if (deactivateResult.IsFailure)
        {
            return await FailAsync(command, deactivateResult.Error, now, cancellationToken, entityId, before);
        }

        WriteSuccessEntry(command, entityId, before, Snapshot(district), now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Deactivation is the case the architecture doc explicitly calls out (section
        // 19.1's TTL-vs-cache comparison table: "задержкой инвалидации кеша при
        // деактивации округа") -- a deactivated district must not keep resolving to a
        // now-untrusted address for up to 300 seconds after this call.
        await cache.RemoveAsync(DistrictCacheKeys.Resolve(district.NormalizedCode), cancellationToken);

        return Result.Success(Unit.Value);
    }

    private void WriteSuccessEntry(DeactivateDistrictCommand command, string entityId, string before, string after, DateTimeOffset now) =>
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.DistrictDeactivated,
            "District",
            entityId,
            before,
            after,
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

    private async Task<Result<Unit>> FailAsync(
        DeactivateDistrictCommand command,
        Error error,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string? entityId,
        string? before)
    {
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.DistrictDeactivateFailed,
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
