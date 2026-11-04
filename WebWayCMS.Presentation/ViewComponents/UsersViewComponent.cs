using System.Security.Claims;

using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.Users;
using WebWayCMS.Interfaces;
using WebWayCMS.Models.Users;

namespace WebWayCMS.ViewComponents;

/// <summary>
/// List-capable, routable user-management widget (Admin only). The item's display mode picks the behaviour:
///
/// <list type="bullet">
/// <item><b>List</b> renders the user table (<c>?role=</c> / <c>?status=</c> filters, <c>?skip=</c> paging, the
/// Invite form) with the <c>adminlist.*</c> views. It owns the <c>edit/{userId:guid}</c> route, where it renders
/// that user's management screen with the <c>admin.*</c> views. It yields on another item's widget route.</item>
/// <item><b>Single</b> renders the management screen for the user on its own route, else
/// <see cref="UsersWidgetConfiguration.UserId"/>, else nothing.</item>
/// </list>
///
/// Only the admin and zone-editor views ship: on a public page (or for a visitor who is not an Admin) the widget
/// renders nothing. Its forms post to <c>AdminUserManagementController</c>, which re-checks the Admin role.
/// </summary>
[ContentZoneComponent(
    DisplayName = "Users",
    Description = "Lists users (role and status filters, paging, invitations) and manages a user on its own edit/{userId} route.",
    Category = "Users",
    ConfigurationType = typeof(UsersWidgetConfiguration),
    IconClass = "fa-users",
    Order = 1,
    SupportsList = true
)]
public class UsersViewComponent : WidgetViewComponent, IRoutableViewComponent
{
    internal const string RouteDataItemKey = "CMS:RouteData";
    internal const string UserIdRouteValue = "userId";

    private readonly IUserAdminService _users;

    public UsersViewComponent(IUserAdminService users)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    public async Task<IViewComponentResult> InvokeAsync(UsersWidgetConfiguration config)
    {
        config ??= new UsersWidgetConfiguration();
        var mode = RenderModeFor(config);

        // The zone editor shows a configuration card; it never lists real users.
        if (mode == WidgetRenderMode.Edit)
            return ModeView(config, config);

        // Admin pages only, and Admins only: nothing renders on a public page or for anyone else.
        if (mode is WidgetRenderMode.View or WidgetRenderMode.List || !HttpContext.User.IsInRole(UserProfileRules.AdminRole))
            return Content(string.Empty);

        var ct = HttpContext.RequestAborted;
        if (IsOwnWidgetRoute(config) && RouteUserId() is { } routeUserId)
            return await DetailAsync(config, routeUserId, ct);

        // Another item's widget route: that widget takes the page over.
        if (IsWidgetRouteRequest())
            return Content(string.Empty);

        if (DisplayModeFor(config) == WidgetDisplayMode.List)
            return await ListAsync(config, ct);

        return string.IsNullOrWhiteSpace(config.UserId)
            ? Content(string.Empty)
            : await DetailAsync(config, config.UserId.Trim(), ct);
    }

    private async Task<IViewComponentResult> ListAsync(UsersWidgetConfiguration config, CancellationToken ct)
    {
        var query = HttpContext.Request.Query;
        var role = string.IsNullOrWhiteSpace(query["role"]) ? null : query["role"].ToString();
        UserStatus? status = Enum.TryParse<UserStatus>(query["status"], ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
        var skip = int.TryParse(query["skip"], out var parsedSkip) ? Math.Max(0, parsedSkip) : 0;

        var page = await _users.ListAsync(new UserListQuery(role, status, skip, config.EffectivePageSize), ct);
        var vm = new UserListViewModel
        {
            Page = page,
            Roles = await _users.ListRolesAsync(ct),
            RoleFilter = role,
            StatusFilter = status,
            ShowRoles = config.ShowRoles,
            ShowStatus = config.ShowStatus,
            ShowLastLogin = config.ShowLastLogin,
            ShowInvite = config.ShowInvite,
            ListUrl = ListUrl(config),
            CurrentUrl = CurrentUrl(),
        };
        return ModeView(config, vm);
    }

    /// <summary>
    /// One user's management screen, rendered through a Single-mode copy of the configuration so a List item on
    /// its own route still derives the <c>admin.*</c> views.
    /// </summary>
    private async Task<IViewComponentResult> DetailAsync(UsersWidgetConfiguration config, string userId, CancellationToken ct)
    {
        var vm = new UserDetailViewModel
        {
            User = await _users.GetAsync(userId, ct),
            Roles = await _users.ListRolesAsync(ct),
            ListUrl = DisplayModeFor(config) == WidgetDisplayMode.List ? ListUrl(config) : string.Empty,
            CurrentUrl = CurrentUrl(),
            IsSelf = string.Equals(HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), userId, StringComparison.OrdinalIgnoreCase),
        };

        var single = new UsersWidgetConfiguration
        {
            ViewName = config.ViewName,
            ZoneItemNodeId = config.ZoneItemNodeId,
            Metadata = new WidgetMetadata
            {
                DisplayMode = WidgetDisplayMode.Single,
                AdminLabel = config.Metadata.AdminLabel,
                AdminNote = config.Metadata.AdminNote,
                CssClass = config.Metadata.CssClass,
                AnchorId = config.Metadata.AnchorId,
            },
        };
        return ModeView(single, vm);
    }

    /// <summary>
    /// The Users page URL: the matched route's literal prefix, without the trailing <c>/edit</c> segment when the
    /// request is this item's own <c>edit/{userId}</c> route.
    /// </summary>
    private string ListUrl(UsersWidgetConfiguration config)
    {
        if (HttpContext.Items[RouteDataItemKey] is not CMSRouteDTO route)
            return string.Empty;

        var braceIdx = route.Pattern.IndexOf('{');
        var prefix = (braceIdx >= 0 ? route.Pattern[..braceIdx] : route.Pattern).TrimEnd('/');
        var detailSuffix = "/" + UserManagementRoutes.DetailSegment;
        return IsOwnWidgetRoute(config) && prefix.EndsWith(detailSuffix, StringComparison.OrdinalIgnoreCase)
            ? prefix[..^detailSuffix.Length]
            : prefix;
    }

    private string CurrentUrl() => HttpContext.Request.Path + HttpContext.Request.QueryString;

    private string? RouteUserId()
        => ViewContext.RouteData.Values.TryGetValue(UserIdRouteValue, out var value) && value?.ToString() is { Length: > 0 } id
            ? id
            : null;

    string IRoutableViewComponent.ComponentName => "Users";

    Task<IReadOnlyList<CMSRouteDTO>> IRoutableViewComponent.GenerateRoutesAsync(
        string parentRoute, Guid contentZoneItemNodeId, WidgetDisplayMode displayMode, CancellationToken ct)
    {
        // Both modes own the management route: a List's rows link to {page}/edit/{id} and the List renders the
        // user there itself. (A List and a Single on one page would claim the same pattern; publish reports it.)
        // The constraint is inline: CMSRouteService parses it from the pattern text.
        var route = new CMSRouteDTO
        {
            Pattern = $"{UserManagementRoutes.DetailSegment}/{{{UserIdRouteValue}:guid}}",
            OwningContentNodeId = contentZoneItemNodeId,
            OwningContentType = "UsersWidget",
            Order = 1,
        };

        return Task.FromResult<IReadOnlyList<CMSRouteDTO>>([route]);
    }
}
