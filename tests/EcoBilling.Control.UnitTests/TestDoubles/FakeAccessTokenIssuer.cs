using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Administrators;

namespace EcoBilling.Control.UnitTests.TestDoubles;

internal sealed class FakeAccessTokenIssuer(TimeSpan lifetime) : IAccessTokenIssuer
{
    public List<AdministratorId> IssuedFor { get; } = [];

    public AccessToken Issue(Administrator administrator, DateTimeOffset now)
    {
        IssuedFor.Add(administrator.Id);

        return new AccessToken($"access-token-for:{administrator.Id}", now + lifetime);
    }
}
