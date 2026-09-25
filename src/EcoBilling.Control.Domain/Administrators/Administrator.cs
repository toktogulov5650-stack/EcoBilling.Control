using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Domain.Administrators;

/// <summary>A system administrator: the only kind of account EcoBilling.Control stores locally.</summary>
public sealed class Administrator
{
    public const int MaxFullNameLength = 200;

    private Administrator(
        AdministratorId id,
        AdministratorEmail email,
        string fullName,
        string passwordHash,
        DateTimeOffset createdAt)
    {
        Id = id;
        Email = email.Original;
        NormalizedEmail = email.Normalized;
        FullName = fullName;
        PasswordHash = passwordHash;
        // Starts active: unlike District, there is no ActivateAdministrator scenario in
        // this stage, so an administrator that started inactive could never be enabled.
        IsActive = true;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>For EF Core materialization only (Stage 4's pattern) -- never called by application code.</summary>
    private Administrator()
    {
    }

    public AdministratorId Id { get; }

    /// <summary>The address as entered, for display.</summary>
    public string Email { get; } = null!;

    /// <summary>The address the unique index is built on.</summary>
    public string NormalizedEmail { get; } = null!;

    public string FullName { get; private set; } = null!;

    /// <summary>
    /// Never exposed through any API response (brief's mandate) -- enforced by never
    /// including it in a DTO anywhere, not by anything on this property itself.
    /// </summary>
    public string PasswordHash { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public static Result<Administrator> Create(
        AdministratorId id,
        AdministratorEmail email,
        string? fullName,
        string passwordHash,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (id == AdministratorId.Empty)
        {
            throw new ArgumentException("An administrator id must not be empty.", nameof(id));
        }

        var normalizedName = NormalizeFullName(fullName);

        return normalizedName is null
            ? Result.Failure<Administrator>(AdministratorErrors.InvalidFullName)
            : Result.Success(new Administrator(id, email, normalizedName, passwordHash, now));
    }

    /// <summary>Stamps the login timestamp. Does not check <see cref="IsActive"/> -- that decision belongs to the login scenario, not this mutator.</summary>
    public void RecordLogin(DateTimeOffset now)
    {
        LastLoginAt = now;
        UpdatedAt = now;
    }

    private static string? NormalizeFullName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        var trimmed = fullName.Trim();

        return trimmed.Length > MaxFullNameLength ? null : trimmed;
    }
}
