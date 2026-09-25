using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.UnitTests.TestDoubles;

/// <summary>In-memory <see cref="ICache"/>, matching the reasoning behind every other Fake in this folder.</summary>
internal sealed class FakeCache : ICache
{
    private readonly Dictionary<string, object> _entries = [];

    /// <summary>Keys passed to <see cref="RemoveAsync"/>, in call order -- lets a test assert exactly what was invalidated.</summary>
    public List<string> RemovedKeys { get; } = [];

    public void Seed<T>(string key, T value) where T : class => _entries[key] = value;

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class =>
        Task.FromResult(_entries.TryGetValue(key, out var value) ? (T)value : null);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken) where T : class
    {
        _entries[key] = value;

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        RemovedKeys.Add(key);
        _entries.Remove(key);

        return Task.CompletedTask;
    }
}
