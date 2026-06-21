using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace WebWayCMS.Presentation.Components.Account;

/// <summary>
/// Resolves the current Identity user from the HttpContext for the Static-SSR Identity components,
/// redirecting to an error page if the signed-in principal has no backing user. Adapted from the
/// standard ASP.NET Core Blazor Web App (Individual Accounts) template.
/// </summary>
internal sealed class IdentityUserAccessor(UserManager<IdentityUser> userManager, IdentityRedirectManager redirectManager)
{
    public async Task<IdentityUser> GetRequiredUserAsync(HttpContext context)
    {
        var user = await userManager.GetUserAsync(context.User);

        if (user is null)
        {
            redirectManager.RedirectToWithStatus(
                "Account/InvalidUser",
                $"Error: Unable to load user with ID '{userManager.GetUserId(context.User)}'.",
                context);
        }

        return user;
    }
}
