using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data.DbContexts;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsIdentityRegistration
{
    internal static void ConfigureAuthorization(IServiceCollection services)
    {
        services.AddDefaultIdentity<IdentityUser>(
                identityOptions =>
                {
                    identityOptions.SignIn.RequireConfirmedEmail = true;
                    identityOptions.Password.RequireDigit = true;
                    identityOptions.Password.RequireLowercase = true;
                    identityOptions.Password.RequireNonAlphanumeric = true;
                    identityOptions.Password.RequireUppercase = true;
                    identityOptions.Password.RequiredLength = 12;

                    identityOptions.Lockout.AllowedForNewUsers = true;
                    identityOptions.Lockout.MaxFailedAccessAttempts = 5;
                    identityOptions.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                }
                )
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<CmsDbContext>()
            .AddDefaultUI();

        services.ConfigureApplicationCookie(cookieOptions =>
        {
            cookieOptions.Cookie.HttpOnly = true;
            cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookieOptions.Cookie.SameSite = SameSiteMode.Strict;
        });
    }
}
