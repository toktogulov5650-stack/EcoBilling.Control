namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// One row of the administrative audit trail.
/// </summary>
/// <remarks>
/// Not a Domain entity: it has no business invariants of its own -- it is
/// Infrastructure/observability data (the brief's Domain folder map has no "Audit"
/// folder; its Infrastructure map does have "Auditing/"). <see cref="Id"/> is a bare
/// <see cref="Guid"/>, not a typed-id wrapper: this id is never passed alongside another
/// Guid in a way a wrapper would need to disambiguate.
///
/// <see cref="AdministratorId"/> is nullable and has no foreign key to Administrators --
/// deliberately. Some events (a login attempt against an email that does not exist, or
/// CreateAdministrator run from the Provisioning CLI) have no administrator to attach
/// to. And unlike RefreshTokens, which cascade-delete with their administrator because
/// they are that administrator's operational child data, an audit row is historical
/// record that must remain valid independent of what later happens to the account it
/// mentions.
///
/// <see cref="BeforeData"/>/<see cref="AfterData"/> are small, explicitly-constructed
/// JSON snapshots built by the handler that writes the entry -- never a reflected dump
/// of a whole entity. That is what keeps <c>PasswordHash</c> and raw tokens out: there is
/// no field for them in any snapshot type a handler builds, not a filter that has to
/// remember to exclude them.
///
/// <see cref="CorrelationId"/>, <see cref="IpAddress"/> and <see cref="UserAgent"/> are
/// left null by every handler that constructs one of these -- Application has no
/// business knowing about HTTP. Infrastructure's <c>AuditWriter</c> fills them in from
/// the ambient <c>HttpContext</c> (via <c>IHttpContextAccessor</c>) immediately before
/// staging the entry, and leaves them null when there is no HTTP request at all (the
/// Provisioning CLI).
/// </remarks>
public sealed record AuditEntry(
    Guid Id,
    Guid? AdministratorId,
    string Action,
    string EntityType,
    string? EntityId,
    string? BeforeData,
    string? AfterData,
    string? CorrelationId,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedAt);
