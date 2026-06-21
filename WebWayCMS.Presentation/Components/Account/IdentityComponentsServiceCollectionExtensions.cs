using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace WebWayCMS.Presentation.Components.Account;

/// <summary>
/// Registers the services backing the Blazor Identity (account) components - the cascading auth
/// state, the redirect/user-accessor helpers, and the revalidating server auth-state provider.
/// Exposed publicly so the host project can wire the components that live in this RCL.
/// </summary>
public static class IdentityComponentsServiceCollectionExtensions
{
    public static IServiceCollection AddCmsBlazorIdentity(this IServiceCollection services)
    {
        services.AddCascadingAuthenticationState();
        services.AddScoped<IdentityUserAccessor>();
        services.AddScoped<IdentityRedirectManager>();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        services.AddSingleton<Microsoft.AspNetCore.Identity.IEmailSender<Microsoft.AspNetCore.Identity.IdentityUser>, IdentityEmailSender>();
        return services;
    }
}
