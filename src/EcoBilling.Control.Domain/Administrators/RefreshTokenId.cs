namespace EcoBilling.Control.Domain.Administrators;

/// <summary>Strongly-typed identifier for a refresh token.</summary>
public readonly record struct RefreshTokenId(Guid Value)
{
    public static RefreshTokenId Empty => new(Guid.Empty);

    public static RefreshTokenId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
