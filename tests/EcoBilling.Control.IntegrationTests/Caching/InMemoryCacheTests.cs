using EcoBilling.Control.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace EcoBilling.Control.IntegrationTests.Caching;

/// <summary>Verifies <see cref="InMemoryCache"/> against a real <see cref="IMemoryCache"/>. No PostgreSQL needed.</summary>
public sealed class InMemoryCacheTests
{
    private sealed record CachedValue(string Text);

    [Fact]
    public async Task SetThenGet_ReturnsTheSameValue()
    {
        var cache = new InMemoryCache(new MemoryCache(new MemoryCacheOptions()));

        await cache.SetAsync("key", new CachedValue("hello"), TimeSpan.FromMinutes(5), CancellationToken.None);
        var found = await cache.GetAsync<CachedValue>("key", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("hello", found.Text);
    }

    [Fact]
    public async Task Get_ReturnsNull_ForAMissingKey()
    {
        var cache = new InMemoryCache(new MemoryCache(new MemoryCacheOptions()));

        var found = await cache.GetAsync<CachedValue>("never-set", CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task Remove_MakesASubsequentGetReturnNull()
    {
        var cache = new InMemoryCache(new MemoryCache(new MemoryCacheOptions()));
        await cache.SetAsync("key", new CachedValue("hello"), TimeSpan.FromMinutes(5), CancellationToken.None);

        await cache.RemoveAsync("key", CancellationToken.None);

        Assert.Null(await cache.GetAsync<CachedValue>("key", CancellationToken.None));
    }

    [Fact]
    public async Task Remove_OnAKeyThatWasNeverSet_DoesNotThrow()
    {
        var cache = new InMemoryCache(new MemoryCache(new MemoryCacheOptions()));

        await cache.RemoveAsync("never-set", CancellationToken.None); // must not throw.
    }

    [Fact]
    public async Task Get_ReturnsNull_AfterTheTtlExpires()
    {
        var cache = new InMemoryCache(new MemoryCache(new MemoryCacheOptions()));

        await cache.SetAsync("key", new CachedValue("hello"), TimeSpan.FromMilliseconds(50), CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Assert.Null(await cache.GetAsync<CachedValue>("key", CancellationToken.None));
    }
}
