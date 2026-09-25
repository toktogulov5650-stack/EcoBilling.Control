using System.Text.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Auditing;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.UpdateDistrict;

/// <summary>
/// Applies a partial update to a district. Validation runs before any mutation, and
/// nothing persists until the final <see cref="IUnitOfWork.SaveChangesAsync"/> call, so a
/// failure partway through (e.g. a valid new name but a disallowed new host) leaves the
/// tracked entity's in-memory changes unsaved rather than requiring an explicit rollback.
/// Both success and every failure path are audited (Stage 7 decision).
/// </summary>
public sealed class UpdateDistrictHandler(
    IDistrictRepository districts,
    IDistrictHostAllowlist allowlist,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<UpdateDistrictCommand, Unit>
{
    private static readonly Error NothingToUpdate = new(
        "validation.failed",
        "At least one of name or apiBaseUrl must be supplied.");

    public async Task<Result<Unit>> HandleAsync(UpdateDistrictCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        if (command.Name is null && command.ApiBaseUrl is null)
        {
            return await FailAsync(command, entityId: null, before: null, NothingToUpdate, now, cancellationToken);
        }

        var district = await districts.GetByIdAsync(command.DistrictId, cancellationToken);

        if (district is null)
        {
            return await FailAsync(command, entityId: null, before: null, DistrictErrors.NotFound, now, cancellationToken);
        }

        // Captured before any mutation is attempted: if Rename succeeds but the later
        // URL step fails, the in-memory entity already reflects the new name even
        // though nothing was saved -- re-snapshotting `district` at failure time would
        // wrongly show that unsaved partial change as "before".
        var entityId = district.Id.Value.ToString();
        var before = Snapshot(district);

        if (command.Name is not null)
        {
            var renameResult = district.Rename(command.Name, now);

            if (renameResult.IsFailure)
            {
                return await FailAsync(command, entityId, before, renameResult.Error, now, cancellationToken);
            }
        }

        if (command.ApiBaseUrl is not null)
        {
            var urlResult = TrustedApiUrl.Create(command.ApiBaseUrl);

            if (urlResult.IsFailure)
            {
                return await FailAsync(command, entityId, before, urlResult.Error, now, cancellationToken);
            }

            var url = urlResult.Value;

            if (!allowlist.IsAllowed(url.Host))
            {
                return await FailAsync(command, entityId, before, DistrictErrors.HostNotAllowed, now, cancellationToken);
            }

            district.ChangeApiBaseUrl(url, now);
        }

        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.DistrictUpdated,
            "District",
            entityId,
            before,
            Snapshot(district),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Unit.Value);
    }

    private async Task<Result<Unit>> FailAsync(
        UpdateDistrictCommand command,
        string? entityId,
        string? before,
        Error error,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            command.CallingAdministratorId.Value,
            AuditActions.DistrictUpdateFailed,
            "District",
            entityId,
            before,
            JsonSerializer.Serialize(new
            {
                attemptedName = command.Name,
                attemptedApiBaseUrl = command.ApiBaseUrl,
                errorCode = error.Code,
            }),
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
