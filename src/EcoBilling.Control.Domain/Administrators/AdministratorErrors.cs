using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Domain.Administrators;

/// <summary>Stable error codes for administrator and session rules.</summary>
public static class AdministratorErrors
{
    public static readonly Error InvalidEmail = new(
        "administrator.invalid_email",
        "The email address is not in a valid format.");

    public static readonly Error InvalidPassword = new(
        "administrator.invalid_password",
        "The password does not meet the minimum length requirement.");

    public static readonly Error InvalidFullName = new(
        "administrator.invalid_full_name",
        "The administrator's name is empty or too long.");

    public static readonly Error EmailConflict = new(
        "administrator.email_conflict",
        "An administrator with this email already exists.");

    // Deliberately never returned by the login endpoint itself (see AdministratorLoginHandler):
    // unknown email, wrong password and an inactive account all collapse onto
    // InvalidCredentials at the API boundary, so a caller cannot distinguish which one
    // occurred (doc, section 21.2: errors must not reveal whether a specific administrator
    // email exists). Kept distinct here only so a future audit trail can record which case
    // actually happened, even though the client-visible response never says.
    public static readonly Error InvalidCredentials = new(
        "administrator.invalid_credentials",
        "The email or password is incorrect.");

    public static readonly Error Inactive = new(
        "administrator.inactive",
        "The administrator account is not active.");

    // Deliberately never returned by the login endpoint itself either (Stage 10, same
    // reasoning as Inactive above): a lockout collapses onto InvalidCredentials at the
    // API boundary (AdministratorAuthEndpoints), so a caller cannot tell "wrong
    // password," "inactive account" and "temporarily locked out" apart. Kept distinct
    // here only so the audit trail can record which one actually happened.
    public static readonly Error LockedOut = new(
        "administrator.locked_out",
        "The administrator account is temporarily locked out after too many failed login attempts.");

    public static readonly Error InvalidRefreshToken = new(
        "administrator.invalid_refresh_token",
        "The refresh token is missing, expired, or has already been used.");
}
