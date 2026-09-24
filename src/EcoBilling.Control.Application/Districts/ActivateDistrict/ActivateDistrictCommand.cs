using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.ActivateDistrict;

public sealed record ActivateDistrictCommand(DistrictId DistrictId) : ICommand<Unit>;
