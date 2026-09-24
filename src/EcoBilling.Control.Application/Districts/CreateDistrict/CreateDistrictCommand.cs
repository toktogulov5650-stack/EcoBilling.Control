using EcoBilling.Control.Application.Abstractions;

namespace EcoBilling.Control.Application.Districts.CreateDistrict;

/// <summary>Registers a new district in the central registry. Starts inactive (Domain, Stage 1).</summary>
public sealed record CreateDistrictCommand(string? Code, string? Name, string? ApiBaseUrl)
    : ICommand<CreateDistrictResult>;
