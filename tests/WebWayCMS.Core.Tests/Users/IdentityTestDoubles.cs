using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

using NSubstitute;

namespace WebWayCMS.Core.Tests.Users;

/// <summary>Substitutes for Identity's managers (classes with virtual members and many constructor arguments).</summary>
internal static class IdentityTestDoubles
{
    public static UserManager<IdentityUser> UserManager() =>
        Substitute.For<UserManager<IdentityUser>>(
            Substitute.For<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);

    public static SignInManager<IdentityUser> SignInManager(UserManager<IdentityUser> users) =>
        Substitute.For<SignInManager<IdentityUser>>(
            users,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<IdentityUser>>(),
            null, null, null, null);
}
