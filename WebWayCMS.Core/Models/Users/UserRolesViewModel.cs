using WebWayCMS.Data.Services.Users;

namespace WebWayCMS.Models.Users;

/// <summary>The User Roles widget.</summary>
public sealed class UserRolesViewModel
{
    public IReadOnlyList<RoleSummary> Roles { get; init; } = [];

    public string CurrentUrl { get; init; } = string.Empty;
}
