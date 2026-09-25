using EcoBilling.Control.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EcoBilling.Control.Api.HealthChecks;

/// <summary>
/// Backs <c>GET /ready</c> (Stage 14) -- distinct from <c>GET /health</c>, which only
/// proves the process itself is running and answering requests. Readiness additionally
/// proves the one external dependency this service cannot function without (PostgreSQL)
/// is actually reachable, so a load balancer or orchestrator can tell "process is up but
/// its database connection is down" apart from "genuinely ready to serve traffic."
/// </summary>
public sealed class PostgresHealthCheck(ControlDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Could not connect to PostgreSQL.");
        }
        catch (Exception ex)
        {
            // The exception message could contain the connection string (host, database
            // name) in some driver failure paths -- never the password, which Npgsql
            // does not include in its own exception text, but host/database names are
            // not secrets either way. Not passed to HealthCheckResult regardless: the
            // health check response is unauthenticated, so nothing beyond "unhealthy"
            // is returned to the caller. Available to whoever reads server-side logs.
            return HealthCheckResult.Unhealthy("Could not connect to PostgreSQL.", ex);
        }
    }
}
