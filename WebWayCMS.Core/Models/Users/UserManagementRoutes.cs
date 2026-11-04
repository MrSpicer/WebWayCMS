using Microsoft.AspNetCore.WebUtilities;

using WebWayCMS.Data.Services.Users;

namespace WebWayCMS.Models.Users;

/// <summary>Where the user-management forms post (<c>AdminUserManagementController</c>).</summary>
public static class UserManagementRoutes
{
    public const string ActionBase = "/wadmin/usermanagement";

    /// <summary>The Users page's URL in the CMS's own seed content, where a form post with no return URL lands.</summary>
    public const string UsersPagePath = "/wadmin/users";

    /// <summary>The Roles page's URL in the CMS's own seed content, where a role form post with no return URL lands.</summary>
    public const string RolesPagePath = "/wadmin/roles";

    /// <summary>The literal segment before the user id in the Users widget's route (<c>edit/{userId:guid}</c>).</summary>
    public const string DetailSegment = "edit";

    public static string Action(string action) => $"{ActionBase}/{action}";

    /// <summary>A user's management screen under the Users page at <paramref name="listUrl"/>.</summary>
    public static string DetailUrl(string listUrl, string userId) => $"{listUrl.TrimEnd('/')}/{DetailSegment}/{userId}";

    /// <summary>The list URL with its filters and offset (unset ones left out).</summary>
    public static string ListUrl(string listUrl, string? role, UserStatus? status, int skip)
    {
        var query = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(role))
            query["role"] = role;
        if (status != null)
            query["status"] = status.Value.ToString().ToLowerInvariant();
        if (skip > 0)
            query["skip"] = skip.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var path = string.IsNullOrEmpty(listUrl) ? "/" : listUrl;
        return QueryHelpers.AddQueryString(path, query);
    }
}
