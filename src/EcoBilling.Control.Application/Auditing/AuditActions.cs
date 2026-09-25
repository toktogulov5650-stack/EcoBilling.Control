namespace EcoBilling.Control.Application.Auditing;

/// <summary>Stable action identifiers used in <c>AuditEntry.Action</c>.</summary>
/// <remarks>
/// Not every mutation is audited (Stage 7 decision): routine refresh-token rotation and
/// logout are deliberately excluded as noise, not signal -- only the security-relevant
/// reuse-detection case for sessions is audited.
/// </remarks>
public static class AuditActions
{
    public const string DistrictCreated = "district.created";
    public const string DistrictCreateFailed = "district.create_failed";
    public const string DistrictUpdated = "district.updated";
    public const string DistrictUpdateFailed = "district.update_failed";
    public const string DistrictActivated = "district.activated";
    public const string DistrictActivateFailed = "district.activate_failed";
    public const string DistrictDeactivated = "district.deactivated";
    public const string DistrictDeactivateFailed = "district.deactivate_failed";

    public const string AdministratorLoginSucceeded = "administrator.login_succeeded";
    public const string AdministratorLoginFailed = "administrator.login_failed";
    public const string AdministratorLockedOut = "administrator.locked_out";
    public const string AdministratorSessionReuseDetected = "administrator.session_reuse_detected";
    public const string AdministratorCreated = "administrator.created";
    public const string AdministratorCreateFailed = "administrator.create_failed";

    public const string ProvisioningDirectorCreationSucceeded = "provisioning.director_creation_succeeded";
    public const string ProvisioningDirectorCreationFailed = "provisioning.director_creation_failed";
    public const string ProvisioningPasswordResetSucceeded = "provisioning.password_reset_succeeded";
    public const string ProvisioningPasswordResetFailed = "provisioning.password_reset_failed";
}
