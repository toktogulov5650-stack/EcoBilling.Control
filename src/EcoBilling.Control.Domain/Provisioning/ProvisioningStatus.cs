namespace EcoBilling.Control.Domain.Provisioning;

/// <summary>The lifecycle of a <see cref="ProvisioningOperation"/>.</summary>
public enum ProvisioningStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
}
