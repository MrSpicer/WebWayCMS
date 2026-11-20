namespace WebWayCMS.Data.Models;

/// <summary>Limits and fixed names shared by the user-management service, its forms and the MCP tools.</summary>
public static class UserProfileRules
{
    /// <summary>Maximum length of a display, first or last name.</summary>
    public const int MaxNameLength = 256;

    /// <summary>Maximum length of a role name (Identity's <c>AspNetRoles.Name</c> column).</summary>
    public const int MaxRoleNameLength = 256;

    /// <summary>The Admin role (full access, including user management).</summary>
    public const string AdminRole = "Admin";

    /// <summary>The Editor role (content editing and draft preview, no user management).</summary>
    public const string EditorRole = "Editor";

    /// <summary>The role every self-registered or invited user starts with.</summary>
    public const string DefaultRole = "User";

    /// <summary>The roles the CMS seeds and depends on: never renamed or deleted.</summary>
    public static readonly IReadOnlyList<string> BuiltInRoles = [AdminRole, EditorRole, DefaultRole];

    /// <summary>Write/publish roles of a content type editors may change too (a new array per call).</summary>
    public static string[] AdminOrEditor => [AdminRole, EditorRole];

    public static bool IsBuiltInRole(string? role) =>
        role != null && BuiltInRoles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
