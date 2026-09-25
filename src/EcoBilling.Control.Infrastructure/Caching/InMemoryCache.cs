using EcoBilling.Control.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace EcoBilling.Control.Infrastructure.Caching;

/// <summary>
/// In-process <see cref="ICache"/> (Stage 12, Q13). Deliberately not Redis: a single
/// Control instance today has no need to share cache state across replicas, and nothing
/// in <see cref="ICache"/>'s own shape assumes an in-process store, so swapping this
/// implementation for a distributed one later needs no caller to change.
/// </summary>
public sealed class InMemoryCache(IMemoryCache cache) : ICache
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class =>
        Task.FromResult(cache.Get<T>(key));

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken) where T : class
    {
        cache.Set(key, value, ttl);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cache.Remove(key);

        return Task.CompletedTask;
    }
}
