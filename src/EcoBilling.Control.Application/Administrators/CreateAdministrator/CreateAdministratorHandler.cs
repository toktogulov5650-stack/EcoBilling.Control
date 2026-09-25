using System.Text.Json;
using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Application.Auditing;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Application.Administrators.CreateAdministrator;

/// <summary>
/// Creates a system administrator, called only by the Provisioning CLI (Q4) -- there is
/// no HTTP endpoint for this and no authenticated caller, so every audit entry this
/// handler writes has <c>AdministratorId = null</c>: the actor is the ops process
/// itself, not another administrator. The entity being acted on -- the newly created
/// account -- is recorded via <c>EntityId</c> instead.
/// </summary>
public sealed class CreateAdministratorHandler(
    IAdministratorRepository administrators,
    IPasswordHasher passwordHasher,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateAdministratorCommand, CreateAdministratorResult>
{
    public async Task<Result<CreateAdministratorResult>> HandleAsync(
        CreateAdministratorCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var emailResult = AdministratorEmail.Create(command.Email);

        if (emailResult.IsFailure)
        {
            return await FailAsync(command, emailResult.Error, now, cancellationToken);
        }

        var passwordResult = PlainTextPassword.Create(command.Password);

        if (passwordResult.IsFailure)
        {
            return await FailAsync(command, passwordResult.Error, now, cancellationToken);
        }

        var existing = await administrators.GetByNormalizedEmailAsync(emailResult.Value.Normalized, cancellationToken);

        if (existing is not null)
        {
            return await FailAsync(command, AdministratorErrors.EmailConflict, now, cancellationToken);
        }

        var passwordHash = passwordHasher.Hash(passwordResult.Value.Reveal());

        var administratorResult = Administrator.Create(
            AdministratorId.New(),
            emailResult.Value,
            command.FullName,
            passwordHash,
            now);

        if (administratorResult.IsFailure)
        {
            return await FailAsync(command, administratorResult.Error, now, cancellationToken);
        }

        var administrator = administratorResult.Value;

        administrators.Add(administrator);

        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            AdministratorId: null,
            AuditActions.AdministratorCreated,
            "Administrator",
            administrator.Id.Value.ToString(),
            BeforeData: null,
            AfterData: null, // no snapshot needed -- the new account's existence and id are the fact being recorded, and PasswordHash never belongs in an audit entry regardless.
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateAdministratorResult(administrator.Id, administrator.NormalizedEmail));
    }

    private async Task<Result<CreateAdministratorResult>> FailAsync(
        CreateAdministratorCommand command,
        Error error,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        auditWriter.Write(new AuditEntry(
            Guid.CreateVersion7(),
            AdministratorId: null,
            AuditActions.AdministratorCreateFailed,
            "Administrator",
            EntityId: null,
            BeforeData: null,
            AfterData: JsonSerializer.Serialize(new { attemptedEmail = command.Email, errorCode = error.Code }),
            CorrelationId: null,
            IpAddress: null,
            UserAgent: null,
            now));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Failure<CreateAdministratorResult>(error);
    }
}
