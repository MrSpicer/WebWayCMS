namespace WebWayCMS.Data.Models;

/// <summary>
/// A personal access token a user holds for the MCP endpoint. Not versioned. Only the SHA-256
/// hash of the token is stored; the plaintext is shown once, at creation.
/// </summary>
/// <remarks>
/// A token acts as its user with the user's <em>current</em> roles (resolved per request), so
/// changing a user's roles or locking the account takes effect on the next call.
/// </remarks>
public record ApiTokenDTO
{
    public Guid Id { get; set; }

    /// <summary>The owning ASP.NET Identity user's id (FK to AspNetUsers, cascade delete).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>A label the user chose, unique per user.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Lowercase hex SHA-256 of the plaintext token. Unique; the lookup key.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>The token's first characters, kept so a user can tell their tokens apart.</summary>
    public string Prefix { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    /// <summary>When the token stops working; null never expires.</summary>
    public DateTime? ExpiresUtc { get; set; }

    /// <summary>Last successful use, updated at most once per minute.</summary>
    public DateTime? LastUsedUtc { get; set; }

    /// <summary>When the token was revoked; a revoked token never validates again.</summary>
    public DateTime? RevokedUtc { get; set; }
}
