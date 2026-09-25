using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.Application.Districts.ListDistricts;

public sealed record ListDistrictsQuery(int Page, int PageSize) : IQuery<ListDistrictsResult>;
