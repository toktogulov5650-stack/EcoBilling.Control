using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.GetDistrict;

public sealed record GetDistrictQuery(DistrictId DistrictId) : IQuery<GetDistrictResult>;
