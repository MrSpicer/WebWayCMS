using Microsoft.AspNetCore.Identity;

namespace WebWayCMS.Users;

/// <summary>The invitation provider's data-protection options (its own protector name and lifespan).</summary>
public sealed class InvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InvitationTokenProviderOptions()
    {
        Name = "InvitationProtector";
        TokenLifespan = TimeSpan.FromDays(UserManagementOptions.DefaultInvitationLifetimeDays);
    }
}
