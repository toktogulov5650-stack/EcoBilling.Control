using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Domain.Provisioning;

/// <summary>
/// Stable error codes for provisioning operations, including the inter-service catalogue
/// from the architecture doc (section 20.2) that <c>IDistrictClient</c> implementations
/// map district responses onto. Centralized here, not duplicated per call site, because
/// both Control's own state ("this operation already completed") and a district's HTTP
/// response ("409 director.already_exists") can produce the exact same error code.
/// </summary>
public static class ProvisioningOperationErrors
{
    public static readonly Error NotFound = new(
        "provisioning.operation_not_found",
        "No provisioning operation matches the supplied identifier.");

    public static readonly Error AlreadyCompleted = new(
        "provisioning.already_completed",
        "This provisioning operation has already completed.");

    // The four codes below are the architecture doc's section 20.2 inter-service
    // catalogue. They are reused verbatim, not redefined per Application scenario, since
    // EcoBilling's team relies on these exact strings too (architecture doc, section 20).
    public static readonly Error DirectorAlreadyExists = new(
        "director.already_exists",
        "A director already exists for this district.");

    public static readonly Error DirectorNotFound = new(
        "director.not_found",
        "No director matches the supplied email in this district.");

    public static readonly Error DistrictUnavailable = new(
        "district.unavailable",
        "The district's EcoBilling instance could not be reached.");

    public static readonly Error ServiceUnauthorized = new(
        "service.unauthorized",
        "The district rejected Control's service credential.");

    public static readonly Error ValidationFailed = new(
        "validation.failed",
        "The district rejected the request as invalid.");
}
