using EcoBilling.Control.Domain.Common;

namespace EcoBilling.Control.Domain.Administrators;

/// <summary>A system administrator: the only kind of account EcoBilling.Control stores locally.</summary>
public sealed class Administrator
{
    public const int MaxFullNameLength = 200;

    /// <summary>Consecutive failed attempts that trigger a lockout (Stage 10).</summary>
    private const int FailedAttemptsBeforeLockout = 5;

    /// <summary>The escalating lockout duration never exceeds this, however many times in a row it has triggered.</summary>
    private static readonly TimeSpan MaxLockoutDuration = TimeSpan.FromMinutes(30);

    /// <summary>
    /// If this long passes with no further failed attempts, the next failure starts
    /// escalation over from tier 1 -- prevents an attacker "banking" a high escalation
    /// tier against an administrator who simply hasn't attempted a login in a while.
    /// </summary>
    private static readonly TimeSpan EscalationResetWindow = TimeSpan.FromHours(24);

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
        FailedLoginAttempts = 0;
        ConsecutiveLockouts = 0;
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

    /// <summary>Null while not locked out. Set on the failure that trips <see cref="FailedAttemptsBeforeLockout"/>; cleared on the next successful login.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>
    /// Consecutive failures since the last successful login or the last time this
    /// reached <see cref="FailedAttemptsBeforeLockout"/> and triggered a lockout --
    /// whichever happened more recently. Reset to 0 by either event, not a running total.
    /// </summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>
    /// The escalation tier: how many lockouts in a row have triggered without an
    /// intervening successful login (or a 24-hour gap with no failed attempts,
    /// <see cref="EscalationResetWindow"/>). Determines the next lockout's duration.
    /// </summary>
    public int ConsecutiveLockouts { get; private set; }

    /// <summary>When the most recent failed attempt happened, for <see cref="EscalationResetWindow"/>'s 24-hour check.</summary>
    public DateTimeOffset? LastFailedLoginAt { get; private set; }

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;

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

    /// <summary>
    /// Stamps the login timestamp and fully resets lockout bookkeeping (Stage 10: a
    /// successful login clears the failed-attempt counter, the escalation tier, any
    /// active lockout, and the escalation-reset clock). Does not check
    /// <see cref="IsActive"/> or <see cref="IsLockedOut"/> -- those decisions belong to
    /// the login scenario, not this mutator.
    /// </summary>
    public void RecordLogin(DateTimeOffset now)
    {
        LastLoginAt = now;
        FailedLoginAttempts = 0;
        ConsecutiveLockouts = 0;
        LockedUntil = null;
        LastFailedLoginAt = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records a failed login attempt. A no-op while already locked out (Stage 10,
    /// deliberate: an attacker hammering the endpoint during an active lockout must not
    /// be able to extend it or advance escalation further -- the same reasoning that
    /// makes a lockout meaningful in the first place). Returns whether this specific
    /// call is the one that just triggered a new lockout, so the caller knows whether to
    /// audit it as such.
    /// </summary>
    public bool RecordFailedLoginAttempt(DateTimeOffset now)
    {
        if (IsLockedOut(now))
        {
            return false;
        }

        if (LastFailedLoginAt is null || now - LastFailedLoginAt > EscalationResetWindow)
        {
            FailedLoginAttempts = 0;
            ConsecutiveLockouts = 0;
        }

        FailedLoginAttempts++;
        LastFailedLoginAt = now;
        UpdatedAt = now;

        if (FailedLoginAttempts < FailedAttemptsBeforeLockout)
        {
            return false;
        }

        ConsecutiveLockouts++;
        var lockoutDuration = TimeSpan.FromMinutes(Math.Min(MaxLockoutDuration.TotalMinutes, Math.Pow(2, ConsecutiveLockouts - 1)));
        LockedUntil = now + lockoutDuration;
        FailedLoginAttempts = 0; // each lockout is triggered by its own fresh batch of failures, not a running total.

        return true;
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
