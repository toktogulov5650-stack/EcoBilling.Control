namespace EcoBilling.Control.Domain.Provisioning;

/// <summary>The kind of cross-service operation Control asked a district's EcoBilling instance to perform.</summary>
public enum ProvisioningOperationType
{
    DirectorCreation,
    PasswordReset,
}
