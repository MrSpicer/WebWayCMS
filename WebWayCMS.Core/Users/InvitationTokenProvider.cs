using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebWayCMS.Users;

/// <summary>
/// Token provider for invitation links (<see cref="ProviderName"/>, purpose <see cref="Purpose"/>). Its own
/// provider so an invitation can live longer (<see cref="UserManagementOptions.InvitationLifetimeDays"/>, default
/// 7 days) than a password-reset link (Identity's default provider, 1 day). The token is bound to the user's
/// security stamp, so it dies once the invited user sets a password (which rotates the stamp).
/// </summary>
public sealed class InvitationTokenProvider : DataProtectorTokenProvider<IdentityUser>
{
    public const string ProviderName = "Invitation";

    public const string Purpose = "AcceptInvitation";

    public InvitationTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<InvitationTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<IdentityUser>> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }
}
