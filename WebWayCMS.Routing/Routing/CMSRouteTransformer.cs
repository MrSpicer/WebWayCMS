using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Pages;

namespace WebWayCMS.Routing;

public class CMSRouteTransformer : DynamicRouteValueTransformer
{
    public const string PageDataItemKey = "CMS:PageData";
    public const string PageConfigItemKey = "CMS:PageConfig";

    private readonly ICMSRouteService _routeService;
    private readonly IPageControllerRegistry _registry;
    private readonly IContentStore<PageDTO> _pageStore;

    public CMSRouteTransformer(
        ICMSRouteService routeService,
        IPageControllerRegistry registry,
        IContentStore<PageDTO> pageStore)
    {
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _pageStore = pageStore ?? throw new ArgumentNullException(nameof(pageStore));
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

            if (route.OwningContentType == "Page" && route.OwningContentNodeId.HasValue)
            {
                var page = await _pageStore.GetAsync(route.OwningContentNodeId.Value);
                if (page != null)
                    httpContext.Items[PageDataItemKey] = page;
            }
            else
            {
                var dataTokens = TryDeserialize<Dictionary<string, string>>(route.DataTokensJson);
                if (dataTokens != null
                    && dataTokens.TryGetValue("ParentPageNodeId", out var pageNodeStr)
                    && Guid.TryParse(pageNodeStr, out var pageNodeId))
                {
                    var page = await _pageStore.GetAsync(pageNodeId);
                    if (page != null)
                        httpContext.Items[PageDataItemKey] = page;
                }
            }

            if (controllerInfo.ConfigurationType != null)
            {
                var pageData = httpContext.Items[PageDataItemKey] as PageDTO;
                httpContext.Items[PageConfigItemKey] =
                    DeserializeConfig(pageData?.ConfigurationJson, controllerInfo.ConfigurationType);
            }
        }

        httpContext.Items["CMS:RouteData"] = route;

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

    private static object DeserializeConfig(string? json, Type configurationType)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return Activator.CreateInstance(configurationType)!;
        try
        {
            return JsonSerializer.Deserialize(json, configurationType,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? Activator.CreateInstance(configurationType)!;
        }
        catch
        {
            return Activator.CreateInstance(configurationType)!;
        }
    }
}
