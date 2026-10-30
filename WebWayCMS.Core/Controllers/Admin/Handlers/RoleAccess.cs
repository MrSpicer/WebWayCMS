using System.Security.Claims;

namespace WebWayCMS.Controllers.Admin.Handlers;

/// <summary>
/// The one rule for a handler's <see cref="IAdminCrudHandler.WriteRoles"/> /
/// <see cref="IAdminCrudHandler.PublishRoles"/>, shared by the admin controllers and the MCP tools.
/// </summary>
public static class RoleAccess
{
    /// <summary>
    /// Whether <paramref name="user"/> holds one of <paramref name="roles"/>; a null list means Admin only.
    /// </summary>
    public static bool Allows(string[]? roles, ClaimsPrincipal? user)
    {
        if (user == null) return false;

        return roles == null
            ? user.IsInRole(PreviewConstants.AdminRole)
            : roles.Any(user.IsInRole);
    }
}
