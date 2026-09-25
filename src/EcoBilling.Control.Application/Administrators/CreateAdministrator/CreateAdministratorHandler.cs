using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;
using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Application.Administrators.CreateAdministrator;

public sealed class CreateAdministratorHandler(
    IAdministratorRepository administrators,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateAdministratorCommand, CreateAdministratorResult>
{
    public async Task<Result<CreateAdministratorResult>> HandleAsync(
        CreateAdministratorCommand command,
        CancellationToken cancellationToken)
    {
        var emailResult = AdministratorEmail.Create(command.Email);

        if (emailResult.IsFailure)
        {
            return Result.Failure<CreateAdministratorResult>(emailResult.Error);
        }

        var passwordResult = PlainTextPassword.Create(command.Password);

        if (passwordResult.IsFailure)
        {
            return Result.Failure<CreateAdministratorResult>(passwordResult.Error);
        }

        var existing = await administrators.GetByNormalizedEmailAsync(emailResult.Value.Normalized, cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<CreateAdministratorResult>(AdministratorErrors.EmailConflict);
        }

        var passwordHash = passwordHasher.Hash(passwordResult.Value.Reveal());

        var administratorResult = Administrator.Create(
            AdministratorId.New(),
            emailResult.Value,
            command.FullName,
            passwordHash,
            clock.UtcNow);

        if (administratorResult.IsFailure)
        {
            return Result.Failure<CreateAdministratorResult>(administratorResult.Error);
        }

        var administrator = administratorResult.Value;

        administrators.Add(administrator);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateAdministratorResult(administrator.Id, administrator.NormalizedEmail));
    }
}
