using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.Application.Abstractions;

/// <summary>Access to the administrator account registry.</summary>
public interface IAdministratorRepository
{
    /// <summary>
    /// Looks up an administrator by normalized email, regardless of
    /// <see cref="Administrator.IsActive"/> -- the caller (the login scenario) decides
    /// how an inactive account is treated, mirroring <c>IDistrictRepository</c>'s
    /// <c>GetByNormalizedCodeAsync</c> (Stage 2).
    /// </summary>
    Task<Administrator?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up an administrator by id, tracked, so a mutation such as
    /// <c>RecordLogin</c> persists through <see cref="IUnitOfWork.SaveChangesAsync"/>
    /// with no separate update call (Stage 4/5's pattern).
    /// </summary>
    Task<Administrator?> GetByIdAsync(AdministratorId id, CancellationToken cancellationToken);

    /// <summary>Stages a new administrator for insertion. Nothing persists until <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
    void Add(Administrator administrator);
}
