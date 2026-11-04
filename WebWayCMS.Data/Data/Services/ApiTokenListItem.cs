using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>A token row with its owner's email, for the admin listing.</summary>
public sealed record ApiTokenListItem(ApiTokenDTO Token, string? UserEmail);
