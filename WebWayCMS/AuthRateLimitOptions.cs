namespace WebWayCMS;

/// <summary>
/// Auth endpoint rate-limit configuration, bound from the "AuthRateLimiting" configuration section and
/// applied by <see cref="AuthRateLimiting"/>. The defaults (5 requests per 60-second window, per client IP
/// and endpoint family) suit a public site; a test host that drives many account flows from one IP raises
/// <see cref="PermitLimit"/>. Both values must be at least 1 (checked at startup).
/// </summary>
public sealed class AuthRateLimitOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "AuthRateLimiting";

    /// <summary>Maximum requests allowed per window per client IP for a single auth endpoint family.</summary>
    public int PermitLimit { get; set; } = 5;

    /// <summary>Length, in seconds, of the fixed window over which <see cref="PermitLimit"/> applies.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Upper bound for <see cref="PermitLimit"/>, so a config typo cannot flood a bucket.</summary>
    public const int MaxPermitLimit = 10_000;

    /// <summary>Upper bound, in seconds, for <see cref="WindowSeconds"/> (a day).</summary>
    public const int MaxWindowSeconds = 86_400;

    /// <summary>Whether both values are usable: at least 1 and within the caps.</summary>
    public bool IsValid() =>
        PermitLimit is >= 1 and <= MaxPermitLimit && WindowSeconds is >= 1 and <= MaxWindowSeconds;
}
