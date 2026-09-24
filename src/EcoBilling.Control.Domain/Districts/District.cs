using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Domain.Districts;

/// <summary>
/// A district in the central registry: a routing code and the trusted address of that
/// district's own EcoBilling instance. Control stores no operational data of a district.
/// </summary>
public sealed class District
{
    public const int MaxNameLength = 200;

    private District(
        DistrictId id,
        DistrictCode code,
        string name,
        TrustedApiUrl apiBaseUrl,
        DateTimeOffset createdAt)
    {
        Id = id;
        Code = code.Original;
        NormalizedCode = code.Normalized;
        Name = name;
        ApiBaseUrl = apiBaseUrl;
        Status = DistrictStatus.Inactive;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>
    /// For EF Core materialization only (Stage 4). Never called by application code --
    /// it is private, and nothing outside this class can reach it. EF Core populates
    /// every property directly through its backing field when reading a row, bypassing
    /// this constructor's (empty) body entirely.
    /// </summary>
    private District()
    {
        // The `= null!` defaults below exist only to satisfy the nullable-reference
        // analyzer for this constructor; EF Core overwrites every field before the
        // entity is handed to any caller, and the validating Create() factory below
        // is the only other path that constructs a District, so these values are
        // never actually observed as null.
    }

    public DistrictId Id { get; }

    /// <summary>The code as entered, for display.</summary>
    public string Code { get; } = null!;

    /// <summary>The code the unique index is built on.</summary>
    public string NormalizedCode { get; } = null!;

    public string Name { get; private set; } = null!;

    public TrustedApiUrl ApiBaseUrl { get; private set; } = null!;

    public DistrictStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    public bool IsActive => Status == DistrictStatus.Active;

    /// <summary>
    /// Creates a district. It starts <see cref="DistrictStatus.Inactive"/> and has to be
    /// activated explicitly, so a district whose EcoBilling instance is not deployed yet
    /// can never route live traffic.
    /// </summary>
    public static Result<District> Create(
        DistrictId id,
        DistrictCode code,
        string? name,
        TrustedApiUrl apiBaseUrl,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(apiBaseUrl);

        if (id == DistrictId.Empty)
        {
            throw new ArgumentException("A district id must not be empty.", nameof(id));
        }

        var normalizedName = NormalizeName(name);

        return normalizedName is null
            ? Result.Failure<District>(DistrictErrors.InvalidName)
            : Result.Success(new District(id, code, normalizedName, apiBaseUrl, now));
    }

    public Result Activate(DateTimeOffset now)
    {
        if (Status == DistrictStatus.Active)
        {
            return Result.Failure(DistrictErrors.AlreadyActive);
        }

        Status = DistrictStatus.Active;
        ActivatedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (Status == DistrictStatus.Inactive)
        {
            return Result.Failure(DistrictErrors.AlreadyInactive);
        }

        Status = DistrictStatus.Inactive;
        DeactivatedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    public Result Rename(string? name, DateTimeOffset now)
    {
        var normalizedName = NormalizeName(name);

        if (normalizedName is null)
        {
            return Result.Failure(DistrictErrors.InvalidName);
        }

        Name = normalizedName;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Changes the trusted API address. Callers are responsible for auditing the change
    /// and invalidating the resolve cache; both are added in later stages.
    /// </summary>
    public Result ChangeApiBaseUrl(TrustedApiUrl apiBaseUrl, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(apiBaseUrl);

        ApiBaseUrl = apiBaseUrl;
        UpdatedAt = now;

        return Result.Success();
    }

    private static string? NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();

        return trimmed.Length > MaxNameLength ? null : trimmed;
    }
}
