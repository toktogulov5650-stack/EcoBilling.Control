namespace EcoBilling.Control.Application.Administrators;

/// <summary>
/// Refresh token lifetime (14 days, confirmed). Shared by Login and
/// RefreshAdministratorSession, the two scenarios that issue a token, so the value
/// exists in exactly one place.
/// </summary>
internal static class RefreshTokenPolicy
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);
}
