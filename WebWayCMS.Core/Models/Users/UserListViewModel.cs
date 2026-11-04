using WebWayCMS.Data.Services.Users;

namespace WebWayCMS.Models.Users;

/// <summary>The Users widget in List mode.</summary>
public sealed class UserListViewModel
{
    public UserListPage Page { get; init; } = new([], 0, 0, UserListQuery.DefaultTake);

    /// <summary>Every role, for the role filter and the Invite form.</summary>
    public IReadOnlyList<RoleSummary> Roles { get; init; } = [];

    public string? RoleFilter { get; init; }

    public UserStatus? StatusFilter { get; init; }

    public bool ShowRoles { get; init; } = true;

    public bool ShowStatus { get; init; } = true;

    public bool ShowLastLogin { get; init; } = true;

    public bool ShowInvite { get; init; } = true;

    /// <summary>The Users page URL (no query), which the filters and pager build on.</summary>
    public string ListUrl { get; init; } = string.Empty;

    /// <summary>The request's own URL (path + query), where the forms send the browser back.</summary>
    public string CurrentUrl { get; init; } = string.Empty;

    public string DetailUrl(string userId) => UserManagementRoutes.DetailUrl(ListUrl, userId);

    public string? PreviousPageUrl => Page.Skip > 0
        ? UserManagementRoutes.ListUrl(ListUrl, RoleFilter, StatusFilter, Math.Max(0, Page.Skip - Page.Take))
        : null;

    public string? NextPageUrl => Page.Skip + Page.Items.Count < Page.Total
        ? UserManagementRoutes.ListUrl(ListUrl, RoleFilter, StatusFilter, Page.Skip + Page.Take)
        : null;
}
