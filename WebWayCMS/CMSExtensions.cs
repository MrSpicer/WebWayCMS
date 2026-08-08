using System.Linq;
using System.Reflection;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Attributes;
using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Mcp;
using WebWayCMS.Routing;
using WebWayCMS.ViewComponents;

namespace WebWayCMS;

public static class CMSExtensions
{
    /// <summary>
    /// Applies pending migrations, seeds the default home page, and configures the rendering
    /// middleware pipeline. Does NOT seed admin roles/user, the admin page, or MCP.
    /// </summary>
    public static WebApplication EnsureCmsRendering(this WebApplication app, bool throwOnError = true)
    {
        app.ApplyCmsPendingMigrations(throwOnError);
        app.EnsureDefaultHomePage(false, throwOnError);
        app.EnsureWidgetRegistrationsSeeded(throwOnError);
        app.ConfigureRenderingPipeline(throwOnError);
        return app;
    }

    /// <summary>
    /// Applies pending migrations, seeds roles/admin user and default pages (home + admin),
    /// and configures the full admin middleware pipeline including MCP.
    /// </summary>
    public static WebApplication EnsureCmsAdmin(this WebApplication app, bool throwOnError = true)
    {
        app.ApplyCmsPendingMigrations(throwOnError);
        app.EnsureCmsRolesAndAdminSeeded(throwOnError);
        app.EnsureDefaultHomePage(true, throwOnError);
        app.EnsureWidgetRegistrationsSeeded(throwOnError);
        app.ConfigureAdminPipeline(throwOnError);
        return app;
    }

    /// <summary>
    /// Backwards-compatible entry point. Delegates to <see cref="EnsureCmsAdmin"/>.
    /// </summary>
    public static WebApplication EnsureCMS(this WebApplication app, bool throwOnError = true)
    {
        return EnsureCmsAdmin(app, throwOnError);
    }

    // ─── Middleware pipelines ─────────────────────────────────────────────────

    private static WebApplication ConfigureRenderingPipeline(this WebApplication app, bool throwOnError = false)
    {
        ConfigureSharedMiddleware(app);
        app.MapCmsEndpoints();
        return app;
    }

    private static WebApplication ConfigureAdminPipeline(this WebApplication app, bool throwOnError = false)
    {
        ConfigureSharedMiddleware(app);
        app.MapWebWayCmsMcp();
        app.MapCmsEndpoints();
        return app;
    }

    private static void ConfigureSharedMiddleware(WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseHsts();
        app.UseHttpsRedirection();

        var cspOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CspOptions>>().Value;
        var cspHeaderName = CspPolicyBuilder.HeaderName(cspOptions);
        var cspHeaderValue = CspPolicyBuilder.Build(cspOptions);

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            if (cspHeaderValue.Length > 0)
                context.Response.Headers[cspHeaderName] = cspHeaderValue;
            await next();
        });

        app.UseStaticFiles();

        app.UseRouting();

        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();
    }

    private static void MapCmsEndpoints(this WebApplication app)
    {
        app.MapRazorPages();

        app.MapControllers();

        app.MapDynamicControllerRoute<PageRouteTransformer>("{**slug}");

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");
    }

    // ─── Migration helpers ────────────────────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static WebApplication ApplyCmsPendingMigrations(this WebApplication app, bool throwOnError = true)
    {
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
                Migrate<CmsDbContext>(services, logger);
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

    // ─── Role / admin seeding ─────────────────────────────────────────────────

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

    // ─── Default page seeding ─────────────────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static WebApplication EnsureDefaultHomePage(this WebApplication app, bool seedAdminPage, bool throwOnError = false)
    {
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

                if (seedAdminPage)
                {
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

    // ─── Widget registration seeding ──────────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static WebApplication EnsureWidgetRegistrationsSeeded(this WebApplication app, bool throwOnError = false)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("WEBWAYCMS_SKIP_DEFAULTWIDGETS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Log.ForContext(typeof(CMSExtensions)).Information("Skipping widget registration seeding due to WEBWAYCMS_SKIP_DEFAULTWIDGETS=true");
            return app;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = Log.ForContext(typeof(CMSExtensions));

        try
        {
            var contentService = services.GetRequiredService<IContentService<WidgetRegistrationDTO>>();
            var widgetService = services.GetRequiredService<IWidgetRegistrationService>();
            var existing = widgetService.GetActiveAsync().GetAwaiter().GetResult();

            var existingNames = new HashSet<string>(
                existing.Select(w => w.ComponentName),
                StringComparer.OrdinalIgnoreCase);

            var assemblies = new[]
            {
                typeof(ContentZoneViewComponent).Assembly,
                Assembly.GetEntryAssembly()!
            }.Where(a => a != null).Distinct();

            foreach (var assembly in assemblies)
            {
                try
                {
                    SeedAssemblyWidgets(assembly, contentService, existingNames, logger);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to scan assembly {Assembly} for widget registrations", assembly.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred seeding widget registrations.");
            if (throwOnError)
                throw;
        }

        return app;
    }

    private static void SeedAssemblyWidgets(
        Assembly assembly,
        IContentService<WidgetRegistrationDTO> contentService,
        HashSet<string> existingNames,
        ILogger logger)
    {
        var viewComponentTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ViewComponent).IsAssignableFrom(t));

        foreach (var type in viewComponentTypes)
        {
            var attribute = type.GetCustomAttribute<ContentZoneComponentAttribute>();
            if (attribute == null)
                continue;

            var componentName = GetWidgetComponentName(type);
            if (existingNames.Contains(componentName))
                continue;

            var propertyDefinitionsJson = "[]";
            if (attribute.ConfigurationType != null)
            {
                try
                {
                    var properties = FormPropertyBuilder.BuildPropertyInfos(attribute.ConfigurationType);
                    propertyDefinitionsJson = JsonSerializer.Serialize(properties);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to build property definitions for widget '{ComponentName}'", componentName);
                }
            }

            var dto = new WidgetRegistrationDTO
            {
                ContentMeta = new ContentDTO
                {
                    Id = Guid.NewGuid(),
                    Title = attribute.DisplayName ?? FormPropertyBuilder.InsertSpaces(componentName),
                    Slug = componentName.ToLowerInvariant(),
                    IsPublished = true,
                    PublicationDate = DateTime.UtcNow,
                    CreationDate = DateTime.UtcNow,
                    ModificationDate = DateTime.UtcNow,
                    CreatedBy = Guid.Empty,
                    LastModifiedBy = Guid.Empty,
                },
                ComponentName = componentName,
                DisplayName = string.IsNullOrEmpty(attribute.DisplayName)
                    ? FormPropertyBuilder.InsertSpaces(componentName)
                    : attribute.DisplayName,
                Description = attribute.Description ?? string.Empty,
                Category = attribute.Category ?? "General",
                IconClass = attribute.IconClass ?? string.Empty,
                Order = attribute.Order,
                ConfigurationTypeName = attribute.ConfigurationType?.FullName,
                PropertyDefinitionsJson = propertyDefinitionsJson,
                IsActive = true,
            };

            try
            {
                contentService.CreateAsync(dto).GetAwaiter().GetResult();
                existingNames.Add(componentName);
                logger.Information("Seeded widget registration '{ComponentName}' as '{DisplayName}'", componentName, dto.DisplayName);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to seed widget registration '{ComponentName}'", componentName);
            }
        }
    }

    private static string GetWidgetComponentName(Type type)
    {
        const string suffix = "ViewComponent";
        var name = type.Name;
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[..^suffix.Length]
            : name;
    }
}
