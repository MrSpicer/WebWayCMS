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
using WebWayCMS.Controllers;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Data.DbContexts;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Mcp;
using WebWayCMS.Routing;
using WebWayCMS.Services;
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
        app.EnsurePageControllerRegistrationsSeeded(throwOnError);
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
        app.EnsurePageControllerRegistrationsSeeded(throwOnError);
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

        app.MapDynamicControllerRoute<CMSRouteTransformer>("{**slug}");

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
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = Log.ForContext(typeof(CMSExtensions));

        try
        {
            var seeder = services.GetRequiredService<IDefaultContentSeeder>();
            seeder.SeedDefaultPagesAsync().GetAwaiter().GetResult();
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

    // ─── Page controller registration seeding ─────────────────────────────────

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static WebApplication EnsurePageControllerRegistrationsSeeded(this WebApplication app, bool throwOnError = false)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("WEBWAYCMS_SKIP_DEFAULTPAGECONTROLLERS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Log.ForContext(typeof(CMSExtensions)).Information("Skipping page controller registration seeding due to WEBWAYCMS_SKIP_DEFAULTPAGECONTROLLERS=true");
            return app;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = Log.ForContext(typeof(CMSExtensions));

        try
        {
            var contentService = services.GetRequiredService<IContentService<PageControllerRegistrationDTO>>();
            var pageControllerService = services.GetRequiredService<IPageControllerRegistrationService>();
            var existing = pageControllerService.GetActiveAsync().GetAwaiter().GetResult();

            var existingNames = new HashSet<string>(
                existing.Select(p => p.ControllerName),
                StringComparer.OrdinalIgnoreCase);

            var assemblies = new[]
            {
                typeof(GenericPageController).Assembly,
                typeof(AdminContentController).Assembly,
                Assembly.GetEntryAssembly()!
            }.Where(a => a != null).Distinct();

            foreach (var assembly in assemblies)
            {
                try
                {
                    SeedAssemblyPageControllers(assembly, contentService, existingNames, logger);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to scan assembly {Assembly} for page controller registrations", assembly.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred seeding page controller registrations.");
            if (throwOnError)
                throw;
        }

        return app;
    }

    private static void SeedAssemblyPageControllers(
        Assembly assembly,
        IContentService<PageControllerRegistrationDTO> contentService,
        HashSet<string> existingNames,
        ILogger logger)
    {
        var controllerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                && typeof(Microsoft.AspNetCore.Mvc.Controller).IsAssignableFrom(t)
                && !typeof(Microsoft.AspNetCore.Mvc.ViewComponent).IsAssignableFrom(t));

        foreach (var type in controllerTypes)
        {
            var attribute = type.GetCustomAttribute<PageControllerAttribute>();
            if (attribute == null)
                continue;

            var controllerName = GetControllerName(type);
            if (existingNames.Contains(controllerName))
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
                    logger.Warning(ex, "Failed to build property definitions for page controller '{ControllerName}'", controllerName);
                }
            }

            var dto = new PageControllerRegistrationDTO
            {
                ContentMeta = new ContentDTO
                {
                    Id = Guid.NewGuid(),
                    Title = attribute.DisplayName ?? FormPropertyBuilder.InsertSpaces(controllerName),
                    Slug = controllerName.ToLowerInvariant(),
                    IsPublished = true,
                    PublicationDate = DateTime.UtcNow,
                    CreationDate = DateTime.UtcNow,
                    ModificationDate = DateTime.UtcNow,
                    CreatedBy = Guid.Empty,
                    LastModifiedBy = Guid.Empty,
                },
                ControllerName = controllerName,
                ControllerTypeName = type.FullName ?? type.Name,
                DisplayName = string.IsNullOrEmpty(attribute.DisplayName)
                    ? FormPropertyBuilder.InsertSpaces(controllerName)
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
                existingNames.Add(controllerName);
                logger.Information("Seeded page controller registration '{ControllerName}' as '{DisplayName}'", controllerName, dto.DisplayName);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to seed page controller registration '{ControllerName}'", controllerName);
            }
        }
    }

    private static string GetControllerName(Type type)
    {
        const string suffix = "Controller";
        var name = type.Name;
        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[..^suffix.Length]
            : name;
    }
}
