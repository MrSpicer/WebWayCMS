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

            var existingPatterns = new HashSet<string>(
                existingRoutes.Select(r => r.Pattern),
                StringComparer.OrdinalIgnoreCase);

            var assemblies = new[]
            {
                typeof(GenericPageController).Assembly,
                typeof(AdminContentController).Assembly,
                typeof(ContentZoneViewComponent).Assembly,
                Assembly.GetEntryAssembly()!
            }.Where(a => a != null).Distinct();

            foreach (var assembly in assemblies)
            {
                try
                {
                    SeedAssemblyCodeBasedRoutes(assembly, routeService, existingPatterns, logger);
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
        HashSet<string> existingPatterns,
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

                if (existingPatterns.Contains(pattern))
                    continue;

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
                    DefaultsJson = JsonSerializer.Serialize(defaults),
                    ConstraintsJson = attr.Constraints ?? "{}",
                    DataTokensJson = JsonSerializer.Serialize(dataTokens),
                    Order = attr.Order,
                    OwningContentType = "CodeBased"
                };

                try
                {
                    routeService.UpsertAsync(route).GetAwaiter().GetResult();
                    existingPatterns.Add(pattern);
                    logger.Information("Seeded code-based route '{Pattern}' -> {Controller}.{Action}",
                        pattern, controllerName, attr.Action ?? "Index");
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to seed code-based route '{Pattern}'", pattern);
                }
            }
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
