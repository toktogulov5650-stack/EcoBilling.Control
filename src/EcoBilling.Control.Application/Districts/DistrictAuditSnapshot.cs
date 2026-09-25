namespace EcoBilling.Control.Application.Districts;

/// <summary>
/// A small, explicit snapshot of a district's auditable fields, serialized into
/// AuditEntry.BeforeData/AfterData. Never the whole entity -- District has nothing
/// sensitive today, but building this snapshot explicitly, field by field, is what keeps
/// it that way as a matter of practice, not accident.
/// </summary>
internal sealed record DistrictAuditSnapshot(string Code, string Name, string ApiBaseUrl, string Status);
