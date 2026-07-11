using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Http;

namespace WebWayCMS;

/// <summary>
/// Rate-limiting policy for the Identity authentication endpoints (login, register, password reset).
/// Throttles per client IP to slow credential brute force and password-reset email flooding. Only the
/// auth endpoints are limited; every other request is unrestricted. The path-matching and partition
/// selection live here as pure logic so they can be unit-tested.
/// </summary>
public static class AuthRateLimiting
{
    /// <summary>Maximum requests allowed per window per client IP for an auth endpoint.</summary>
    public const int PermitLimit = 5;

    /// <summary>The fixed window over which <see cref="PermitLimit"/> applies.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // Identity scaffolded auth pages that accept credentials or trigger emails.
    private static readonly string[] LimitedPaths =
    {
        "/Identity/Account/Login",
        "/Identity/Account/Register",
        "/Identity/Account/ForgotPassword",
        "/Identity/Account/ResendEmailConfirmation",
    };

    /// <summary>Whether the given request path is a rate-limited auth endpoint.</summary>
    public static bool IsRateLimitedPath(PathString path) =>
        LimitedPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Selects the rate-limit partition for a request: a per-IP fixed-window limiter for auth
    /// endpoints, or no limiter otherwise.
    /// </summary>
    public static RateLimitPartition<string> GetPartition(HttpContext context)
    {
        if (!IsRateLimitedPath(context.Request.Path))
            return RateLimitPartition.GetNoLimiter("unlimited");

        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitLimit,
            Window = Window,
        });
    }
}
