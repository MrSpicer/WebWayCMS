using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services.Users;
using WebWayCMS.Models.Users;
using WebWayCMS.Users;

namespace WebWayCMS.Controllers.Admin;

/// <summary>
/// The form posts of the Users and User Roles widgets. Each action runs one <see cref="IUserManagementModel"/>
/// operation as the signed-in Admin, flashes its message and redirects back to the widget's page
/// (<c>returnUrl</c>, local URLs only; else the seeded Users or Roles page). Deleting, purging and granting
/// Admin take the acting Admin's <c>CurrentPassword</c>.
/// </summary>
[Authorize(Roles = UserProfileRules.AdminRole)]
[Route(UserManagementRoutes.ActionBase)]
public class AdminUserManagementController : Controller
{
    internal const string DefaultReturnUrl = UserManagementRoutes.UsersPagePath;
    internal const string DefaultRolesReturnUrl = UserManagementRoutes.RolesPagePath;

    private readonly IUserManagementModel _model;

    public AdminUserManagementController(IUserManagementModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    private UserActor Actor(string? password = null) =>
        new(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, password);

    [HttpPost("invite")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(
        string? email, string? displayName, string? firstName, string? lastName,
        string[]? roles, string? currentPassword, string? returnUrl, CancellationToken ct)
    {
        var invite = new UserInvite(email ?? string.Empty, new UserProfileInput(displayName, firstName, lastName), roles ?? []);
        return Finish(await _model.InviteAsync(invite, Actor(currentPassword), ct), returnUrl);
    }

    [HttpPost("{userId}/update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        string userId, string? email, string? phoneNumber, string? displayName, string? firstName, string? lastName,
        string? returnUrl, CancellationToken ct)
    {
        var update = new UserUpdate(email ?? string.Empty, phoneNumber, new UserProfileInput(displayName, firstName, lastName));
        return Finish(await _model.UpdateAsync(userId, update, Actor(), ct), returnUrl);
    }

    [HttpPost("{userId}/roles")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Roles(string userId, string[]? roles, string? currentPassword, string? returnUrl, CancellationToken ct)
        => Finish(await _model.SetRolesAsync(userId, roles ?? [], Actor(currentPassword), ct), returnUrl);

    [HttpPost("{userId}/resend-invitation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendInvitation(string userId, string? returnUrl, CancellationToken ct)
        => Finish(await _model.ResendInvitationAsync(userId, Actor(), ct), returnUrl);

    [HttpPost("{userId}/send-reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendReset(string userId, string? returnUrl, CancellationToken ct)
        => Finish(await _model.SendPasswordResetAsync(userId, Actor(), ct), returnUrl);

    [HttpPost("{userId}/send-confirmation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendConfirmation(string userId, string? returnUrl, CancellationToken ct)
        => Finish(await _model.SendConfirmationAsync(userId, Actor(), ct), returnUrl);

    [HttpPost("{userId}/confirm-email")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmEmail(string userId, string? returnUrl, CancellationToken ct)
        => Finish(await _model.ConfirmEmailAsync(userId, Actor(), ct), returnUrl);

    [HttpPost("{userId}/lock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock(string userId, string? returnUrl, CancellationToken ct)
        => Finish(await _model.LockAsync(userId, Actor(), ct), returnUrl);

    [HttpPost("{userId}/unlock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(string userId, string? returnUrl, CancellationToken ct)
        => Finish(await _model.UnlockAsync(userId, Actor(), ct), returnUrl);

    [HttpPost("{userId}/sign-out")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutEverywhere(string userId, string? returnUrl, CancellationToken ct)
        => Finish(await _model.SignOutEverywhereAsync(userId, Actor(), ct), returnUrl);

    [HttpPost("{userId}/reset-2fa")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetTwoFactor(string userId, string? returnUrl, CancellationToken ct)
        => Finish(await _model.ResetTwoFactorAsync(userId, Actor(), ct), returnUrl);

    [HttpPost("{userId}/revoke-tokens")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeTokens(string userId, string? returnUrl, CancellationToken ct)
        => Finish(await _model.RevokeApiTokensAsync(userId, Actor(), ct), returnUrl);

    [HttpPost("{userId}/remove-passkeys")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePasskeys(string userId, string? returnUrl, CancellationToken ct)
        => Finish(await _model.RemovePasskeysAsync(userId, Actor(), ct), returnUrl);

    [HttpPost("{userId}/remove-login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLogin(string userId, string? loginProvider, string? providerKey, string? returnUrl, CancellationToken ct)
        => Finish(await _model.RemoveExternalLoginAsync(userId, loginProvider ?? string.Empty, providerKey ?? string.Empty, Actor(), ct), returnUrl);

    [HttpPost("{userId}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string userId, string? currentPassword, string? returnUrl, CancellationToken ct)
        => Finish(await _model.DeleteAsync(userId, Actor(currentPassword), ct), returnUrl);

    [HttpPost("{userId}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(string userId, string? returnUrl, CancellationToken ct)
        => Finish(await _model.RestoreAsync(userId, Actor(), ct), returnUrl);

    [HttpPost("{userId}/purge")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Purge(string userId, string? currentPassword, string? returnUrl, CancellationToken ct)
        => Finish(await _model.PurgeAsync(userId, Actor(currentPassword), ct), returnUrl);

    [HttpPost("roles/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRole(string? name, string? returnUrl, CancellationToken ct)
        => Finish(await _model.CreateRoleAsync(name ?? string.Empty, Actor(), ct), returnUrl, DefaultRolesReturnUrl);

    [HttpPost("roles/rename")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameRole(string? name, string? newName, string? returnUrl, CancellationToken ct)
        => Finish(await _model.RenameRoleAsync(name ?? string.Empty, newName ?? string.Empty, Actor(), ct), returnUrl, DefaultRolesReturnUrl);

    [HttpPost("roles/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRole(string? name, string? returnUrl, CancellationToken ct)
        => Finish(await _model.DeleteRoleAsync(name ?? string.Empty, Actor(), ct), returnUrl, DefaultRolesReturnUrl);

    private IActionResult Finish(UserManagementResult result, string? returnUrl, string fallback = DefaultReturnUrl)
    {
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : fallback);
    }
}
