namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// A simple key-value cache. In-memory for now (Stage 12, Q13) -- Redis-swappable later
/// without touching any caller, since nothing here assumes an in-process store. TTL is a
/// safety net, not the primary invalidation mechanism: callers that mutate cached data
/// are expected to call <see cref="RemoveAsync"/> explicitly rather than waiting it out.
/// </summary>
public interface ICache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken) where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken);
}
