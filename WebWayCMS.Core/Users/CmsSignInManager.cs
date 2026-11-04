using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using WebWayCMS.Data.Services.Users;

namespace WebWayCMS.Users;

/// <summary>
/// The CMS sign-in manager: Identity's, plus a last-login stamp on the user's profile. Every completed sign-in
/// (password, two-factor, recovery code, external login, passkey) ends in
/// <see cref="SignInWithClaimsAsync"/>, so recording it there covers them all. <see cref="RefreshSignInAsync"/>
/// (a cookie re-issue after a password, 2FA or profile change) ends there too but is not a sign-in, so it is
/// not recorded. A failure to record never fails the sign-in.
/// </summary>
public sealed class CmsSignInManager : SignInManager<IdentityUser>
{
    private readonly IUserAdminService _userAdmin;

    /// <summary>True while <see cref="RefreshSignInAsync"/> runs (the manager is scoped, one per request).</summary>
    private bool _refreshing;

    public CmsSignInManager(
        UserManager<IdentityUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<IdentityUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<IdentityUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<IdentityUser> confirmation,
        IUserAdminService userAdmin)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
        _userAdmin = userAdmin ?? throw new ArgumentNullException(nameof(userAdmin));
    }

    public override async Task RefreshSignInAsync(IdentityUser user)
    {
        _refreshing = true;
        try
        {
            await base.RefreshSignInAsync(user);
        }
        finally
        {
            _refreshing = false;
        }
    }

    public override async Task SignInWithClaimsAsync(
        IdentityUser user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        await base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);
        if (_refreshing)
            return;
        try
        {
            await _userAdmin.RecordLoginAsync(user.Id);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not record the last sign-in of user {UserId}.", user.Id);
        }
    }
}
