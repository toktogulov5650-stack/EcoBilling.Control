using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

namespace EcoBilling.Control.Infrastructure.Auditing;

/// <summary>PostgreSQL-backed <see cref="IAuditWriter"/>.</summary>
/// <remarks>
/// Enriches the entry with request-context fields (<c>CorrelationId</c>,
/// <c>IpAddress</c>, <c>UserAgent</c>) here, from the ambient <c>HttpContext</c> --
/// Application never sees these, so every handler that constructs an
/// <see cref="AuditEntry"/> leaves them null and this class fills them in immediately
/// before staging. When there is no HTTP request at all (the Provisioning CLI), the
/// injected <see cref="IHttpContextAccessor"/> simply has a null
/// <see cref="IHttpContextAccessor.HttpContext"/>, and all three stay null -- exactly
/// the intended behavior, no special-casing needed.
/// </remarks>
public sealed class AuditWriter(ControlDbContext dbContext, IHttpContextAccessor httpContextAccessor) : IAuditWriter
{
    public void Write(AuditEntry entry)
    {
        var httpContext = httpContextAccessor.HttpContext;

        var enriched = entry with
        {
            CorrelationId = httpContext?.TraceIdentifier,
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = httpContext?.Request.Headers["User-Agent"].ToString(),
        };

        dbContext.Set<AuditEntry>().Add(enriched);
    }
}
