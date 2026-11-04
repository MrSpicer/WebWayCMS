using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using WebWayCMS.Users;

namespace WebWayCMS.Presentation.Areas.Identity.Pages.Account;

/// <summary>
/// Where an Admin's invitation email lands: the invited user sets their password, which also confirms their
/// email (the link proved they own the address). The link stops working once the password is set, when it
/// expires, or when the account is locked or deleted; every bad link gets the same message.
/// </summary>
[AllowAnonymous]
public class AcceptInvitationModel : PageModel
{
    internal const string InvalidLinkMessage =
        "This invitation link is invalid or has expired. Ask an administrator to send you a new one.";

    private readonly IAcceptInvitationService _invitations;

    public AcceptInvitationModel(IAcceptInvitationService invitations)
    {
        _invitations = invitations ?? throw new ArgumentNullException(nameof(invitations));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>The invited address, shown above the form.</summary>
    public string? Email { get; set; }

    /// <summary>Set after a successful POST: the page then shows the "you can log in" message.</summary>
    public bool Accepted { get; set; }

    /// <summary>Set when the link can't be used: the page shows only <see cref="InvalidLinkMessage"/>.</summary>
    public bool Invalid { get; set; }

    public class InputModel
    {
        [Required]
        public string? UserId { get; set; }

        [Required]
        public string? Code { get; set; }

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string? Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string? ConfirmPassword { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? userId = null, string? code = null)
    {
        var preview = await _invitations.PreviewAsync(userId, code);
        if (preview == null)
        {
            Invalid = true;
            return Page();
        }

        Email = preview.Email;
        Input = new InputModel { UserId = userId, Code = code };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var preview = await _invitations.PreviewAsync(Input.UserId, Input.Code);
        if (preview == null)
        {
            Invalid = true;
            return Page();
        }

        Email = preview.Email;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _invitations.AcceptAsync(Input.UserId, Input.Code, Input.Password!);
        if (result.LinkInvalid)
        {
            Invalid = true;
            return Page();
        }

        if (result.ErrorMessage is { } message)
        {
            ModelState.AddModelError(string.Empty, message);
            return Page();
        }

        if (result.Errors.Count > 0)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return Page();
        }

        Accepted = true;
        return Page();
    }
}
