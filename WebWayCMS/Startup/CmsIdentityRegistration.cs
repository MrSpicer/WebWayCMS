using System.Diagnostics.CodeAnalysis;

using AspNet.Security.OAuth.GitHub;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Services.Users;
using WebWayCMS.Identity;
using WebWayCMS.Users;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsIdentityRegistration
{
    internal static void ConfigureAuthorization(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDefaultIdentity<IdentityUser>(
                identityOptions =>
                {
                    identityOptions.SignIn.RequireConfirmedEmail = true;
                    identityOptions.SignIn.RequireConfirmedAccount = true;
                    identityOptions.Password.RequireDigit = true;
                    identityOptions.Password.RequireLowercase = true;
                    identityOptions.Password.RequireNonAlphanumeric = true;
                    identityOptions.Password.RequireUppercase = true;
                    identityOptions.Password.RequiredLength = 12;

                    identityOptions.User.RequireUniqueEmail = true;

                    // Schema version 3 is what adds the AspNetUserPasskeys table (passkey/WebAuthn support).
                    identityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3;

                    identityOptions.Lockout.AllowedForNewUsers = true;
                    identityOptions.Lockout.MaxFailedAccessAttempts = 5;
                    identityOptions.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                }
                )
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<CmsDbContext>()
            // Records each user's last sign-in on their profile.
            .AddSignInManager<CmsSignInManager>()
            // Invitation links: their own provider, so they outlive a 1-day password-reset link.
            .AddTokenProvider<InvitationTokenProvider>(InvitationTokenProvider.ProviderName);

        ConfigureUserManagement(services, configuration);
        ConfigureExternalLogins(services, configuration);
        ConfigureEmailSender(services, configuration);

        services.Configure<IdentityPasskeyOptions>(configuration.GetSection("Passkeys"));

        services.ConfigureApplicationCookie(cookieOptions =>
        {
            cookieOptions.Cookie.HttpOnly = true;
            cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
        });
    }

    /// <summary>
    /// The user-admin service (the sign-in manager records last logins through it in both modes), the invitation
    /// lifetime and the public registration switch. The admin-only pieces (the management model, account emails)
    /// are registered by <c>CmsAdminRegistration</c>.
    /// </summary>
    private static void ConfigureUserManagement(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserAdminService, UserAdminService>();
        // The invitation-acceptance logic behind the AcceptInvitation page (both modes; a rendering-only
        // host still serves the page, it just has no one inviting).
        services.AddScoped<Users.IAcceptInvitationService, Users.AcceptInvitationService>();

        services.AddOptions<UserManagementOptions>()
            .Bind(configuration.GetSection(UserManagementOptions.SectionName))
            .Validate(o => o.IsValid(), UserManagementOptions.ValidationMessage)
            .ValidateOnStart();
        services.AddOptions<InvitationTokenProviderOptions>()
            .Configure<Microsoft.Extensions.Options.IOptions<UserManagementOptions>>((tokens, userManagement) =>
                tokens.TokenLifespan = TimeSpan.FromDays(userManagement.Value.InvitationLifetimeDays));

        services.Configure<IdentityRegistrationOptions>(configuration.GetSection(IdentityRegistrationOptions.SectionName));
    }

    private static void ConfigureExternalLogins(IServiceCollection services, IConfiguration configuration)
    {
        var google = new GoogleAuthOptions();
        configuration.GetSection(GoogleAuthOptions.SectionName).Bind(google);

        var microsoft = new MicrosoftAuthOptions();
        configuration.GetSection(MicrosoftAuthOptions.SectionName).Bind(microsoft);

        var github = new GitHubAuthOptions();
        configuration.GetSection(GitHubAuthOptions.SectionName).Bind(github);

        var auth = services.AddAuthentication();

        if (!string.IsNullOrEmpty(google.ClientId) && !string.IsNullOrEmpty(google.ClientSecret))
        {
            auth.AddGoogle(options =>
            {
                options.ClientId = google.ClientId;
                options.ClientSecret = google.ClientSecret;
            });
        }

        if (!string.IsNullOrEmpty(microsoft.ClientId) && !string.IsNullOrEmpty(microsoft.ClientSecret))
        {
            auth.AddMicrosoftAccount(options =>
            {
                options.ClientId = microsoft.ClientId;
                options.ClientSecret = microsoft.ClientSecret;
            });
        }

        if (!string.IsNullOrEmpty(github.ClientId) && !string.IsNullOrEmpty(github.ClientSecret))
        {
            auth.AddGitHub(options =>
            {
                options.ClientId = github.ClientId;
                options.ClientSecret = github.ClientSecret;
            });
        }
    }

    private static void ConfigureEmailSender(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));

        var senderType = string.IsNullOrWhiteSpace(configuration["Smtp:Host"])
            ? typeof(LoggingEmailSender)
            : typeof(SmtpEmailSender);

        // AddDefaultIdentity seeds a NoOpEmailSender IEmailSender before this runs, so TryAdd* alone
        // would be a no-op. Replace that framework default with the real sender, but leave a
        // host-registered IEmailSender untouched (a host that registers its own sender keeps it).
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailSender));
        if (existing is null || existing.ImplementationType == typeof(NoOpEmailSender))
        {
            services.Replace(ServiceDescriptor.Singleton(typeof(IEmailSender), senderType));
        }
    }
}
