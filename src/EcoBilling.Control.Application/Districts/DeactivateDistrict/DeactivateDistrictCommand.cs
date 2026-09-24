using EcoBilling.Control.Application.Abstractions;
using EcoBilling.Control.Domain.Districts;

namespace EcoBilling.Control.Application.Districts.DeactivateDistrict;

public sealed record DeactivateDistrictCommand(DistrictId DistrictId) : ICommand<Unit>;
