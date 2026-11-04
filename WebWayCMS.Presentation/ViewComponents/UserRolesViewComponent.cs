using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.Users;
using WebWayCMS.Models.Users;

namespace WebWayCMS.ViewComponents;

/// <summary>
/// Role management widget (Admin only): every role with its member count, and forms to create, rename and
/// delete custom roles (built-in Admin / Editor / User are fixed; a role with members can't be deleted). Only
/// the admin and zone-editor views ship, so it renders nothing on a public page or for anyone but an Admin. It
/// steps aside on a widget route, such as the Users widget's management screen on the same page.
/// </summary>
[ContentZoneComponent(
    DisplayName = "User Roles",
    Description = "Lists roles with their member counts; creates, renames and deletes custom roles.",
    Category = "Users",
    IconClass = "fa-user-tag",
    Order = 2
)]
public class UserRolesViewComponent : WidgetViewComponent
{
    private readonly IUserAdminService _users;

    public UserRolesViewComponent(IUserAdminService users)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var mode = RenderModeFor(null);
        if (mode == WidgetRenderMode.Edit)
            return ModeView((WidgetConfiguration?)null, new UserRolesViewModel());

        if (mode is not (WidgetRenderMode.Admin or WidgetRenderMode.AdminList) || !HttpContext.User.IsInRole(UserProfileRules.AdminRole))
            return Content(string.Empty);

        // A widget route (e.g. the Users widget's edit/{userId} management screen) takes the page over.
        if (IsWidgetRouteRequest())
            return Content(string.Empty);

        return ModeView((WidgetConfiguration?)null, new UserRolesViewModel
        {
            Roles = await _users.ListRolesAsync(HttpContext.RequestAborted),
            CurrentUrl = HttpContext.Request.Path + HttpContext.Request.QueryString,
        });
    }
}
