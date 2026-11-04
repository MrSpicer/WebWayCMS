using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebWayCMS.Presentation.Areas.Identity.Pages.Account
{
    /// <summary>
    /// Shown after a registration (password or external login) when the account needs email confirmation.
    /// Replaces the Identity UI default page, which looks the user up by the <c>email</c> query value — it
    /// answered "Unable to load user" for an unknown address (revealing which addresses have accounts) and
    /// could show the raw confirmation link. This page does neither: it only tells the user to check their mail.
    /// </summary>
    [AllowAnonymous]
    public class RegisterConfirmationModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
