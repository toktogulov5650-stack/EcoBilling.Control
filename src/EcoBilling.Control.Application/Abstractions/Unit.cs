namespace EcoBilling.Control.Application.Abstractions;

/// <summary>
/// The response of a command that succeeds or fails but produces no value of its own,
/// for example Activate/Deactivate. Kept in Application rather than Domain because it
/// is CQRS plumbing, not a business concept.
/// </summary>
public readonly record struct Unit
{
    public static readonly Unit Value;
}
