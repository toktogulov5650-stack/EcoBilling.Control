using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.UnitTests.TestDoubles;

internal sealed class FakeAdministratorRepository : IAdministratorRepository
{
    private readonly Dictionary<string, Administrator> _byNormalizedEmail = new(StringComparer.Ordinal);
    private readonly Dictionary<AdministratorId, Administrator> _byId = [];

    public List<Administrator> Added { get; } = [];

    public void Seed(Administrator administrator)
    {
        _byNormalizedEmail[administrator.NormalizedEmail] = administrator;
        _byId[administrator.Id] = administrator;
    }

    public Task<Administrator?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        Task.FromResult(_byNormalizedEmail.GetValueOrDefault(normalizedEmail));

    public Task<Administrator?> GetByIdAsync(AdministratorId id, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public void Add(Administrator administrator)
    {
        Added.Add(administrator);
        Seed(administrator);
    }
}
