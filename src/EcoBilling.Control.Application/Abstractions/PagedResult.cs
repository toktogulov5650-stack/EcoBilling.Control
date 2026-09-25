namespace EcoBilling.Control.Application.Abstractions;

/// <summary>A page of results plus the total count across every page, for pagination.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);
