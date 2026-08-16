using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Http;

namespace WebWayCMS;

/// <summary>
/// Rate-limiting policy for the Identity authentication endpoints (login, register, password reset,
/// external-login callback, passkey assertion). Throttles per client IP and per endpoint family, so a
/// busy external sign-up (Login GET → ExternalLogin POST → callback GET → confirmation POST) does not
/// exhaust the budget of a plain password login behind the same NAT egress IP. Only the auth endpoints
/// are limited; every other request is unrestricted. The path-matching and partition selection live here
/// as pure logic so they can be unit-tested.
/// </summary>
public static class AuthRateLimiting
{
    /// <summary>Maximum requests allowed per window per client IP for a single auth endpoint family.</summary>
    public const int PermitLimit = 5;

    /// <summary>The fixed window over which <see cref="PermitLimit"/> applies.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // Identity scaffolded auth pages that accept credentials or trigger emails, plus the anonymous
    // passkey challenge-generation endpoint. Prefix-match, case-insensitive.
    private static readonly string[] LimitedPaths =
    {
        "/Identity/Account/Login",
        "/Identity/Account/Register",
        "/Identity/Account/ForgotPassword",
        "/Identity/Account/ResetPassword",
        "/Identity/Account/ResendEmailConfirmation",
        "/Identity/Account/ExternalLogin",
        "/Identity/Account/PasskeyAssertion",
        "/Identity/Account/PasskeyRequestOptions",
    };

    /// <summary>
    /// Returns the matched limited-path prefix for the request path, or <c>null</c> when the path is not
    /// a rate-limited auth endpoint.
    /// </summary>
    public static string? MatchLimitedPath(PathString path) =>
        LimitedPaths.FirstOrDefault(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the given request path is a rate-limited auth endpoint.</summary>
    public static bool IsRateLimitedPath(PathString path) =>
        MatchLimitedPath(path) is not null;

    /// <summary>
    /// Selects the rate-limit partition for a request: a per-IP, per-endpoint-family fixed-window
    /// limiter for auth endpoints, or no limiter otherwise.
    /// </summary>
    public static RateLimitPartition<string> GetPartition(HttpContext context)
    {
        var matchedPrefix = MatchLimitedPath(context.Request.Path);
        if (matchedPrefix is null)
            return RateLimitPartition.GetNoLimiter("unlimited");

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"{ip}|{matchedPrefix}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitLimit,
            Window = Window,
        });
    }
}
