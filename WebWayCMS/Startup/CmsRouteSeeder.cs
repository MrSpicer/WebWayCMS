using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

using WebWayCMS.Attributes;
using WebWayCMS.Controllers;
using WebWayCMS.Controllers.Admin;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Routing;
using WebWayCMS.ViewComponents;

namespace WebWayCMS.Startup;

[ExcludeFromCodeCoverage]
internal static class CmsRouteSeeder
{
    internal static WebApplication EnsureCodeBasedRoutesSeeded(this WebApplication app, bool throwOnError = false)
    {
        if (CmsStartupHelpers.IsSkipped("WEBWAYCMS_SKIP_CODEBASEDROUTES"))
        {
            Log.ForContext(typeof(CmsRouteSeeder)).Information("Skipping code-based route seeding due to WEBWAYCMS_SKIP_CODEBASEDROUTES=true");
            return app;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = Log.ForContext(typeof(CmsRouteSeeder));

        try
        {
            var routeService = services.GetRequiredService<ICMSRouteService>();
            var existingRoutes = routeService.GetActiveRoutesAsync().GetAwaiter().GetResult();

            var existingByPattern = existingRoutes
                .GroupBy(r => r.Pattern, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var assemblies = CmsStartupHelpers.SeedAssemblies(
                services,
                typeof(GenericPageController).Assembly,
                typeof(AdminContentController).Assembly,
                typeof(ContentZoneViewComponent).Assembly);

            foreach (var assembly in assemblies)
            {
                try
                {
                    SeedAssemblyCodeBasedRoutes(assembly, routeService, existingByPattern, logger);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to scan assembly {Assembly} for code-based routes", assembly.FullName);
                }
            }

            var routeRegistry = services.GetRequiredService<ICMSRouteRegistry>();
            routeRegistry.Invalidate();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occurred seeding code-based routes.");
            if (throwOnError)
                throw;
        }

        return app;
    }

    private static void SeedAssemblyCodeBasedRoutes(
        Assembly assembly,
        ICMSRouteService routeService,
        Dictionary<string, CMSRouteDTO> existingByPattern,
        ILogger logger)
    {
        var controllerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                && typeof(Microsoft.AspNetCore.Mvc.Controller).IsAssignableFrom(t)
                && !typeof(Microsoft.AspNetCore.Mvc.ViewComponent).IsAssignableFrom(t));

        foreach (var type in controllerTypes)
        {
            var attributes = type.GetCustomAttributes<CmsRouteAttribute>();
            if (!attributes.Any())
                continue;

            var controllerName = CmsStartupHelpers.GetControllerName(type);

            foreach (var attr in attributes)
            {
                var pattern = NormalizeRoutePattern(attr.Pattern);

                if (existingByPattern.TryGetValue(pattern, out var existing))
                {
                    // The row already exists, so the rest of the attribute is not re-applied — but a
                    // NavigationName added to an attribute after first boot would otherwise never
                    // reach the database, leaving the route invisible to the navigation widgets.
                    // Fill a blank name only; an admin-set one wins.
                    BackfillNavigationName(existing, attr.NavigationName, routeService, logger);
                    continue;
                }

                var defaults = new Dictionary<string, string>
                {
                    { "controller", controllerName },
                    { "action", attr.Action ?? "Index" }
                };

                if (!string.IsNullOrWhiteSpace(attr.Defaults))
                {
                    try
                    {
                        var extra = JsonSerializer.Deserialize<Dictionary<string, string>>(attr.Defaults);
                        if (extra != null)
                        {
                            foreach (var kvp in extra)
                                defaults[kvp.Key] = kvp.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warning(ex, "Failed to parse Defaults JSON for route '{Pattern}'", pattern);
                    }
                }

                var dataTokens = new Dictionary<string, string>
                {
                    { "RouteSource", "CodeBased" }
                };

                if (!string.IsNullOrWhiteSpace(attr.DataTokens))
                {
                    try
                    {
                        var extra = JsonSerializer.Deserialize<Dictionary<string, string>>(attr.DataTokens);
                        if (extra != null)
                        {
                            foreach (var kvp in extra)
                                dataTokens[kvp.Key] = kvp.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warning(ex, "Failed to parse DataTokens JSON for route '{Pattern}'", pattern);
                    }
                }

                var route = new CMSRouteDTO
                {
                    Pattern = pattern,
                    NavigationName = attr.NavigationName,
                    DefaultsJson = JsonSerializer.Serialize(defaults),
                    ConstraintsJson = attr.Constraints ?? "{}",
                    DataTokensJson = JsonSerializer.Serialize(dataTokens),
                    Order = attr.Order,
                    OwningContentType = "CodeBased"
                };

                try
                {
                    var result = routeService.UpsertAsync(route).GetAwaiter().GetResult();
                    if (result.Success)
                    {
                        existingByPattern[pattern] = route;
                        logger.Information("Seeded code-based route '{Pattern}' -> {Controller}.{Action}",
                            pattern, controllerName, attr.Action ?? "Index");
                    }
                    else
                    {
                        logger.Warning("Failed to seed code-based route '{Pattern}': {ErrorMessage}",
                            pattern, result.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to seed code-based route '{Pattern}'", pattern);
                }
            }
        }
    }

    private static void BackfillNavigationName(
        CMSRouteDTO existing, string? navigationName, ICMSRouteService routeService, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(navigationName) || !string.IsNullOrWhiteSpace(existing.NavigationName))
            return;

        existing.NavigationName = navigationName;

        try
        {
            var result = routeService.UpsertAsync(existing).GetAwaiter().GetResult();
            if (result.Success)
                logger.Information("Backfilled navigation name '{NavigationName}' on existing code-based route '{Pattern}'",
                    navigationName, existing.Pattern);
            else
                logger.Warning("Failed to backfill navigation name on code-based route '{Pattern}': {ErrorMessage}",
                    existing.Pattern, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to backfill navigation name on code-based route '{Pattern}'", existing.Pattern);
        }
    }

    internal static string NormalizeRoutePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return "/";

        pattern = pattern.Trim().ToLowerInvariant();

        if (!pattern.StartsWith('/'))
            pattern = "/" + pattern;

        if (pattern.Length > 1 && pattern.EndsWith('/'))
            pattern = pattern.TrimEnd('/');

        return pattern;
    }
}
