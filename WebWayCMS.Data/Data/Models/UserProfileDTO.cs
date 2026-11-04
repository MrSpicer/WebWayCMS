namespace WebWayCMS.Data.Models;

/// <summary>
/// CMS-owned data about an ASP.NET Identity user, kept 1:1 beside <c>AspNetUsers</c> so the user type stays
/// the framework's <c>IdentityUser</c>. Not versioned. A user without a row has an empty profile (users that
/// predate the table need no backfill); <c>IUserAdminService</c> creates the row on first write.
/// </summary>
public record UserProfileDTO
{
    /// <summary>The Identity user's id (PK, FK to AspNetUsers, cascade delete).</summary>
    public string UserId { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    /// <summary>The last successful sign-in (any method: password, 2FA, external, passkey).</summary>
    public DateTime? LastLoginUtc { get; set; }

    /// <summary>Soft-deleted by an Admin: locked, hidden from the user list by default, restorable or purgeable.</summary>
    public bool IsDeleted { get; set; }

    public DateTime? DeletedUtc { get; set; }

    /// <summary>The Admin who soft-deleted the user.</summary>
    public string? DeletedBy { get; set; }

    /// <summary>When an Admin invited the user (null for self-registered or seeded users).</summary>
    public DateTime? InvitedUtc { get; set; }

    /// <summary>The Admin who invited the user.</summary>
    public string? InvitedBy { get; set; }
}
