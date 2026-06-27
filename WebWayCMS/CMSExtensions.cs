using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Mcp;
using WebWayCMS.Routing;

namespace WebWayCMS;

public static class CMSExtensions
{
    public static WebApplication EnsureCMS(this WebApplication app, bool throwOnError = true)
    {
        app.ApplyCmsPendingMigrations(throwOnError);
        app.EnsureCmsRolesAndAdminSeeded(throwOnError);
        app.EnsureDefaultHomePage(throwOnError);
        app.ConfigureMiddleware(throwOnError);
        return app;
    }
    /// <summary>
    /// Applies any pending EF Core migrations for the CMS related contexts. Safe to call multiple times.
    /// Controlled by optional environment variable WEBWAYCMS_APPLY_MIGRATIONS (default true) or ASPNETCORE_ENVIRONMENT.
    /// </summary>
    /// <param name="app">The WebApplication.</param>
    /// <param name="throwOnError">If true, rethrows the exception after logging. Defaults to true (startup should fail if migrations fail).</param>
    /// <returns>The same <see cref="WebApplication"/> instance for chaining.</returns>
    // Relational EF Core migrations require a live database (the InMemory provider cannot run them),
    // so this method and its migration helpers are validated by running the app, not by unit tests.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static WebApplication ApplyCmsPendingMigrations(this WebApplication app, bool throwOnError = true)
    {
        // Allow skipping via env var (e.g. for read-only replicas or integration tests)
        var skip = Environment.GetEnvironmentVariable("WEBWAYCMS_SKIP_MIGRATIONS");
        if (string.Equals(skip, "true", StringComparison.OrdinalIgnoreCase))
        {
            Log.ForContext(typeof(CMSExtensions)).Information("Skipping CMS migrations due to WEBWAYCMS_SKIP_MIGRATIONS=true");
            return app;
        }

        var logger = Log.ForContext(typeof(CMSExtensions));
        const int maxAttempts = 10;
        var delay = TimeSpan.FromSeconds(3);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var services = scope.ServiceProvider;
                // Order matters: ArticleContext owns the shared "Content" table, so it must migrate
                // before the other content contexts whose tables hold a foreign key into "Content".
                Migrate<ApplicationDbContext>(services, logger);
                Migrate<ArticleContext>(services, logger);
                Migrate<ContentBlockContext>(services, logger);
                Migrate<ContentZoneContext>(services, logger);
                Migrate<PageContext>(services, logger);
                return app;
            }
            catch (Exception ex) when (IsTransientDbStartupException(ex) && attempt < maxAttempts)
            {
                logger.Warning("Database not yet available (attempt {Attempt}/{Max}). Retrying in {Delay}s...",
                    attempt, maxAttempts, delay.TotalSeconds);
                Thread.Sleep(delay);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An error occurred migrating CMS databases.");
                if (throwOnError) throw;
                return app;
            }
        }

        return app;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static bool IsTransientDbStartupException(Exception ex)
    {
        // DNS not yet resolved or connection refused — typical Swarm startup race
        var inner = ex.InnerException;
        while (inner != null)
        {
            if (inner is System.Net.Sockets.SocketException) return true;
            inner = inner.InnerException;
        }
        return false;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static void Migrate<TContext>(IServiceProvider services, ILogger logger) where TContext : DbContext
    {
        var context = services.GetService<TContext>();
        if (context == null)
        {
            logger.Warning("DbContext {Context} not registered; skipping migrations.", typeof(TContext).Name);
            return;
        }
        var pending = context.Database.GetPendingMigrations().ToList();
        if (pending.Count == 0)
        {
            logger.Debug("No pending migrations for {Context}", typeof(TContext).Name);
        }
        else
        {
            logger.Information("Applying {Count} migrations for {Context}: {Migrations}", pending.Count, typeof(TContext).Name, string.Join(", ", pending));
        }
        context.Database.Migrate();
    }

    // Identity role/admin seeding requires a live Identity store and exercises framework failure
    // branches that are only meaningful against a real database; validated by running the app.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static WebApplication EnsureCmsRolesAndAdminSeeded(this WebApplication app, bool throwOnError = false)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("WEBWAYCMS_SKIP_ROLESEED"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Log.ForContext(typeof(CMSExtensions)).Information("Skipping role/admin seeding due to WEBWAYCMS_SKIP_ROLESEED=true");
            return app;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = Log.ForContext(typeof(CMSExtensions));

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
            Log.ForContext(typeof(CMSExtensions)).Error(ex, "An error occurred seeding roles/admin user.");
            if (throwOnError)
            {
                throw;
            }
        }

        return app;
    }

    // Default page seeding requires the page database and is validated by running the app.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static WebApplication EnsureDefaultHomePage(this WebApplication app, bool throwOnError = false)
    {
        // Allow skipping via env var
        if (string.Equals(Environment.GetEnvironmentVariable("WEBWAYCMS_SKIP_DEFAULTPAGE"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Log.ForContext(typeof(CMSExtensions)).Information("Skipping default home page seeding due to WEBWAYCMS_SKIP_DEFAULTPAGE=true");
            return app;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = Log.ForContext(typeof(CMSExtensions));

        try
        {
            var pageService = services.GetRequiredService<IPageService>();

            // Check if any pages exist
            var existingPages = pageService.GetByRouteAsync("/").GetAwaiter().GetResult();

            if (existingPages == null)
            {
                logger.Information("No page was found in database. Creating default Home page at route '/'.");

                var homePage = new PageDTO
                {
                    Route = "/",
                    ControllerName = "GenericPage",
                    ConfigurationJson = "{}",
                    ContentMeta = new ContentDTO
                    {
                        Id = Guid.NewGuid(),
                        Title = "Home",
                        Slug = "home",
                        IsPublished = true,
                        PublicationDate = DateTime.UtcNow,
                        CreationDate = DateTime.UtcNow,
                        ModificationDate = DateTime.UtcNow,
                        CreatedBy = Guid.Empty,
                        LastModifiedBy = Guid.Empty
                    }
                };

                var homePageResult = pageService.CreateAsync(homePage).GetAwaiter().GetResult();
                logger.Information("Created default Home page with ID {PageId}", homePageResult.ContentMeta.Id);

                var adminPage = new PageDTO
                {
                    Route = "/admin",
                    ControllerName = "GenericAdminPage",
                    ViewName = "Dashboard",
                    ConfigurationJson = "{}",
                    ContentMeta = new ContentDTO
                    {
                        Id = Guid.NewGuid(),
                        Title = "Admin",
                        Slug = "admin",
                        IsPublished = true,
                        PublicationDate = DateTime.UtcNow,
                        CreationDate = DateTime.UtcNow,
                        ModificationDate = DateTime.UtcNow,
                        CreatedBy = Guid.Empty,
                        LastModifiedBy = Guid.Empty
                    }
                };

                var adminPageResult = pageService.CreateAsync(adminPage).GetAwaiter().GetResult();
                logger.Information("Created default Admin page with ID {PageId}", adminPageResult.ContentMeta.Id);
            }
            else
            {
                logger.Debug("Pages already exist, skipping default home page creation.");
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred creating default home page.");
            if (throwOnError)
            {
                throw;
            }
        }

        return app;
    }

    private static WebApplication ConfigureMiddleware(this WebApplication app, bool throwOnError = false)
    {
        app.UseForwardedHeaders();
        app.UseHsts();
        app.UseHttpsRedirection();

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            await next();
        });

        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        // MCP server endpoint (opt-in via the "Mcp" config section; gated by its own API key).
        app.MapWebWayCmsMcp();

        app.MapRazorPages();

        // Attribute-routed controllers (e.g. AdminContentController's "admin/{contentType}")
        // must be registered as endpoints so they out-rank the catch-all page route below;
        // otherwise "/admin/page" is captured by the dynamic page route as a sub-route of the
        // "/admin" page instead of being handled by its controller.
        app.MapControllers();

        //todo: this should not be slug. pages have routes
        app.MapDynamicControllerRoute<PageRouteTransformer>("{**slug}");

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        return app;
    }
}