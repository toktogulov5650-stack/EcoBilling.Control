using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Common;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.UnitTests.Abstractions;

/// <summary>
/// Proves that the command/query/handler shape introduced in this stage actually
/// composes end to end, using throwaway fakes local to this test class. This is not a
/// test of any real scenario -- ResolveDistrict and the other scenarios arrive in
/// their own stages -- it only proves the plumbing they will be built on works.
/// </summary>
public sealed class HandlerShapeTests
{
    private sealed record Ping(string Message) : IQuery<string>;

    private sealed class PingHandler : IQueryHandler<Ping, string>
    {
        public Task<Result<string>> HandleAsync(Ping query, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success($"pong: {query.Message}"));
    }

    [Fact]
    public async Task QueryHandler_ReturnsASuccessResult()
    {
        IQueryHandler<Ping, string> handler = new PingHandler();

        var result = await handler.HandleAsync(new Ping("hi"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("pong: hi", result.Value);
    }

    private sealed record FailingQuery : IQuery<string>;

    private sealed class FailingQueryHandler : IQueryHandler<FailingQuery, string>
    {
        public Task<Result<string>> HandleAsync(FailingQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Failure<string>(new Error("test.failed", "Simulated failure.")));
    }

    [Fact]
    public async Task QueryHandler_CanReportABusinessFailureWithoutThrowing()
    {
        IQueryHandler<FailingQuery, string> handler = new FailingQueryHandler();

        var result = await handler.HandleAsync(new FailingQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("test.failed", result.Error.Code);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed record BumpByYear(int Amount) : ICommand<int>;

    private sealed class BumpByYearHandler(IClock clock) : ICommandHandler<BumpByYear, int>
    {
        public Task<Result<int>> HandleAsync(BumpByYear command, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(command.Amount + clock.UtcNow.Year));
    }

    [Fact]
    public async Task CommandHandler_CanDependOnIClockThroughTheAbstraction()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        ICommandHandler<BumpByYear, int> handler = new BumpByYearHandler(clock);

        var result = await handler.HandleAsync(new BumpByYear(5), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2031, result.Value);
    }

    [Fact]
    public void Unit_HasASingleValue()
    {
        Assert.Equal(Unit.Value, Unit.Value);
        Assert.Equal(default, Unit.Value);
    }

    private sealed class InMemoryDistrictRepository : IDistrictRepository
    {
        private readonly Dictionary<string, District> _byNormalizedCode = new(StringComparer.Ordinal);

        public void Seed(District district) => _byNormalizedCode[district.NormalizedCode] = district;

        public Task<District?> GetByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken) =>
            Task.FromResult(_byNormalizedCode.GetValueOrDefault(normalizedCode));
    }

    private static District CreateDistrict(string code) =>
        District.Create(
            DistrictId.New(),
            DistrictCode.Create(code).Value,
            "A district",
            TrustedApiUrl.Create("https://district.example.com").Value,
            DateTimeOffset.UtcNow).Value;

    [Fact]
    public async Task DistrictRepository_ReturnsTheSeededDistrictByItsNormalizedCode()
    {
        var repository = new InMemoryDistrictRepository();
        var district = CreateDistrict("BISHKEK-01");
        repository.Seed(district);

        var found = await repository.GetByNormalizedCodeAsync("BISHKEK-01", CancellationToken.None);

        Assert.Same(district, found);
    }

    [Fact]
    public async Task DistrictRepository_ReturnsNullForAnUnknownCode()
    {
        var repository = new InMemoryDistrictRepository();

        var found = await repository.GetByNormalizedCodeAsync("OSH-01", CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task DistrictRepository_DoesNotHideAnInactiveDistrict()
    {
        // The repository must return an inactive district rather than filtering it out,
        // so the calling scenario can distinguish district.not_found from
        // district.inactive itself.
        var repository = new InMemoryDistrictRepository();
        var district = CreateDistrict("BISHKEK-01");
        repository.Seed(district);

        var found = await repository.GetByNormalizedCodeAsync("BISHKEK-01", CancellationToken.None);

        Assert.NotNull(found);
        Assert.False(found.IsActive);
    }
}
