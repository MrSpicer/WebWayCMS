using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsIdentitySeeder
{
    internal static WebApplication EnsureCmsRolesAndAdminSeeded(this WebApplication app, bool throwOnError = false)
    {
        if (CmsStartupHelpers.IsSkipped("WEBWAYCMS_SKIP_ROLESEED"))
        {
            Log.ForContext(typeof(CmsIdentitySeeder)).Information("Skipping role/admin seeding due to WEBWAYCMS_SKIP_ROLESEED=true");
            return app;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = Log.ForContext(typeof(CmsIdentitySeeder));

        try
        {
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
            var config = services.GetRequiredService<IConfiguration>();

            var roles = new[] { "Admin", "Editor", "User" };
            foreach (var role in roles)
            {
                var exists = roleManager.RoleExistsAsync(role).GetAwaiter().GetResult();
                if (!exists)
                {
                    var r = roleManager.CreateAsync(new IdentityRole(role)).GetAwaiter().GetResult();
                    if (!r.Succeeded)
                    {
                        logger.Warning("Failed to create role {Role}: {Errors}", role, string.Join(", ", r.Errors.Select(e => e.Description)));
                    }
                }
            }

            var adminEmail = config["AdminUser:Email"];
            var adminPassword = config["AdminUser:Password"];
            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                logger.Warning("Admin user not created - missing AdminUser:Email or AdminUser:Password configuration.");
                return app;
            }

            var admin = userManager.FindByEmailAsync(adminEmail).GetAwaiter().GetResult();
            if (admin == null)
            {
                admin = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
                var cr = userManager.CreateAsync(admin, adminPassword).GetAwaiter().GetResult();
                if (!cr.Succeeded)
                {
                    logger.Warning("Failed to create admin user {Email}: {Errors}", adminEmail, string.Join(", ", cr.Errors.Select(e => e.Description)));
                }
            }

            var inRole = userManager.IsInRoleAsync(admin, "Admin").GetAwaiter().GetResult();
            if (!inRole)
            {
                var ar = userManager.AddToRoleAsync(admin, "Admin").GetAwaiter().GetResult();
                if (!ar.Succeeded)
                {
                    logger.Warning("Failed to add admin user {Email} to Admin role: {Errors}", adminEmail, string.Join(", ", ar.Errors.Select(e => e.Description)));
                }
            }
        }
        catch (Exception ex)
        {
            Log.ForContext(typeof(CmsIdentitySeeder)).Error(ex, "An error occurred seeding roles/admin user.");
            if (throwOnError)
            {
                throw;
            }
        }

        return app;
    }
}
