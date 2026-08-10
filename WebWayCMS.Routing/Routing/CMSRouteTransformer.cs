using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

using WebWayCMS.Data.Services;
using WebWayCMS.Pages;

namespace WebWayCMS.Routing;

public class CMSRouteTransformer : DynamicRouteValueTransformer
{
    public const string SubRouteItemKey = "CMS:SubRoute";
    public const string PageDataItemKey = "CMS:PageData";
    public const string PageConfigItemKey = "CMS:PageConfig";

    private readonly ICMSRouteService _routeService;
    private readonly IPageControllerRegistry _registry;
    private readonly IPageService _pageService;

    public CMSRouteTransformer(
        ICMSRouteService routeService,
        IPageControllerRegistry registry,
        IPageService pageService)
    {
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _pageService = pageService ?? throw new ArgumentNullException(nameof(pageService));
    }

    public override async ValueTask<RouteValueDictionary> TransformAsync(
        HttpContext httpContext, RouteValueDictionary values)
    {
        var path = httpContext.Request.Path.Value;

        if (string.IsNullOrEmpty(path))
            path = "/";
        path = path.ToLowerInvariant();
        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');

        var match = await _routeService.MatchRouteAsync(path);
        if (match == null)
            return null!;

        var route = match.Route;

        var defaults = TryDeserialize<Dictionary<string, string>>(route.DefaultsJson)
            ?? new Dictionary<string, string>();

        if (!defaults.TryGetValue("controller", out var controllerName))
            return null!;

        var isCodeBased = string.Equals(route.OwningContentType, "CodeBased", StringComparison.OrdinalIgnoreCase);

        if (!isCodeBased)
        {
            var controllerInfo = _registry.GetByName(controllerName);
            if (controllerInfo == null)
                return null!;

            if (route.OwningContentType == "Page" && route.OwningContentMasterId.HasValue)
            {
                var pageVersion = await _pageService.GetAllVersionsAsync(
                    route.OwningContentMasterId.Value);
                var latestPage = pageVersion.FirstOrDefault();
                if (latestPage != null)
                    httpContext.Items[PageDataItemKey] = latestPage;
            }
            else
            {
                var dataTokens = TryDeserialize<Dictionary<string, string>>(route.DataTokensJson);
                if (dataTokens != null
                    && dataTokens.TryGetValue("ParentPageMasterId", out var pageMasterStr)
                    && Guid.TryParse(pageMasterStr, out var pageMasterId))
                {
                    var pageVersion = await _pageService.GetAllVersionsAsync(pageMasterId);
                    var latestPage = pageVersion.FirstOrDefault();
                    if (latestPage != null)
                        httpContext.Items[PageDataItemKey] = latestPage;
                }
            }

            if (controllerInfo.ConfigurationType != null)
            {
                var dataTokens = TryDeserialize<Dictionary<string, string>>(route.DataTokensJson);
                var configJson = dataTokens?.GetValueOrDefault("ConfigurationJson");
                if (!string.IsNullOrWhiteSpace(configJson))
                {
                    try
                    {
                        var config = JsonSerializer.Deserialize(configJson, controllerInfo.ConfigurationType,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        httpContext.Items[PageConfigItemKey] = config;
                    }
                    catch
                    {
                        httpContext.Items[PageConfigItemKey] = Activator.CreateInstance(controllerInfo.ConfigurationType);
                    }
                }
            }
        }

        httpContext.Items["CMS:RouteData"] = route;

        foreach (var kvp in match.RouteValues)
        {
            if (kvp.Key != "controller" && kvp.Key != "action")
                httpContext.Items[SubRouteItemKey] = kvp.Value;
        }

        var routeValues = new RouteValueDictionary
        {
            { "controller", controllerName },
            { "action", defaults.GetValueOrDefault("action", "Index") }
        };

        foreach (var kvp in match.RouteValues)
        {
            routeValues[kvp.Key] = kvp.Value;
        }

        return routeValues;
    }

    private static T? TryDeserialize<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}
