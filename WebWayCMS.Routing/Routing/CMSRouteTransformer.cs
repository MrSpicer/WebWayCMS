using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;
using WebWayCMS.Pages;

namespace WebWayCMS.Routing;

public class CMSRouteTransformer : DynamicRouteValueTransformer
{
    public const string PageDataItemKey = "CMS:PageData";
    public const string PageConfigItemKey = "CMS:PageConfig";

    private readonly ICMSRouteService _routeService;
    private readonly IPageControllerRegistry _registry;
    private readonly IContentStore<PageDTO> _pageStore;
    private readonly IPageHistoryService _pageHistory;

    private readonly CmsDeploymentOptions _deploymentOptions;

    public CMSRouteTransformer(
        ICMSRouteService routeService,
        IPageControllerRegistry registry,
        IContentStore<PageDTO> pageStore,
        IPageHistoryService pageHistory,
        CmsDeploymentOptions deploymentOptions)
    {
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _pageStore = pageStore ?? throw new ArgumentNullException(nameof(pageStore));
        _pageHistory = pageHistory ?? throw new ArgumentNullException(nameof(pageHistory));
        // Required, so a host that never registered the deployment mode fails at resolution
        // instead of silently serving drafts.
        _deploymentOptions = deploymentOptions ?? throw new ArgumentNullException(nameof(deploymentOptions));
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

        var previewNodeId = TryParsePreviewNodeId(path);
        if (previewNodeId.HasValue)
            return (await ResolvePreviewAsync(httpContext, previewNodeId.Value)) ?? null!;

        var historyPreview = TryParseHistoryPreview(path);
        if (historyPreview.HasValue)
            return (await ResolveHistoryPreviewAsync(httpContext, historyPreview.Value.PageNodeId, historyPreview.Value.EntryId)) ?? null!;

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
            PageDTO? page = null;

            // A route that names its page renders only while that page is visible: a page hidden by
            // containment (e.g. under an unpublished parent) is a 404, never the bare controller.
            if (route.OwningContentType == "Page" && route.OwningContentNodeId.HasValue)
            {
                page = await _pageStore.GetAsync(route.OwningContentNodeId.Value);
                if (page == null)
                    return null!;
            }
            else
            {
                var dataTokens = TryDeserialize<Dictionary<string, string>>(route.DataTokensJson);
                if (dataTokens != null
                    && dataTokens.TryGetValue("ParentPageNodeId", out var pageNodeStr)
                    && Guid.TryParse(pageNodeStr, out var pageNodeId))
                {
                    page = await _pageStore.GetAsync(pageNodeId);
                    if (page == null)
                        return null!;
                }
            }

            if (page != null)
            {
                if (StashPageControllerContext(httpContext, page) == null)
                    return null!;
            }
            else
            {
                var controllerInfo = _registry.GetByName(controllerName);
                if (controllerInfo == null)
                    return null!;
                if (controllerInfo.ConfigurationType != null)
                    httpContext.Items[PageConfigItemKey] =
                        DeserializeConfig(null, controllerInfo.ConfigurationType, null);
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

    private PageControllerInfo? StashPageControllerContext(HttpContext httpContext, PageDTO page)
    {
        var controllerInfo = _registry.GetByName(page.ControllerName);
        if (controllerInfo == null)
            return null;

        httpContext.Items[PageDataItemKey] = page;

        if (controllerInfo.ConfigurationType != null)
        {
            httpContext.Items[PageConfigItemKey] =
                DeserializeConfig(page.ConfigurationJson, controllerInfo.ConfigurationType, page.Version.EffectiveRelations());
        }

        return controllerInfo;
    }

    private static Guid? TryParsePreviewNodeId(string path)
    {
        const string prefix = "/_preview/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var nodeIdSegment = path[prefix.Length..];
        if (nodeIdSegment.Contains('/'))
            return null;

        return Guid.TryParse(nodeIdSegment, out var nodeId) ? nodeId : null;
    }

    private async Task<RouteValueDictionary?> ResolvePreviewAsync(HttpContext httpContext, Guid nodeId)
    {
        // Preview is an admin-mode feature: a rendering-only host never seeds the Admin/Editor roles
        // and has no admin pipeline around the mixed draft/published render it would produce.
        if (!_deploymentOptions.IsAdminMode)
            return null;

        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
            return null;

        if (!user.IsInRole(PreviewConstants.AdminRole) && !user.IsInRole(PreviewConstants.EditorRole))
            return null;

        var page = await _pageStore.GetCurrentDraftAsync(nodeId);
        if (page == null)
            return null;

        if (StashPageControllerContext(httpContext, page) == null)
            return null;

        // Drafts for the zones/widgets rendered inside this page — this request only, never site-wide.
        PreviewConstants.MarkDraftRequest(httpContext);

        httpContext.Items["CMS:RouteData"] = new CMSRouteDTO
        {
            Pattern = $"/_preview/{nodeId}",
            OwningContentType = "Page",
            OwningContentNodeId = nodeId
        };

        return new RouteValueDictionary
        {
            { "controller", page.ControllerName },
            { "action", "Index" }
        };
    }

    /// <summary>Parses <c>/_preview/{pageNodeId}/history/{entryId}</c>.</summary>
    private static (Guid PageNodeId, Guid EntryId)? TryParseHistoryPreview(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 4
            || !string.Equals("/" + segments[0], PreviewConstants.PathPrefix, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[2], PreviewConstants.HistorySegment, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(segments[1], out var pageNodeId)
            || !Guid.TryParse(segments[3], out var entryId))
            return null;

        return (pageNodeId, entryId);
    }

    /// <summary>
    /// Renders a page as one page-history entry captured it: the page and its zone items read the
    /// captured versions, everything else reads published. Admins only (admin mode), like the
    /// page-history screens that link here.
    /// </summary>
    private async Task<RouteValueDictionary?> ResolveHistoryPreviewAsync(HttpContext httpContext, Guid pageNodeId, Guid entryId)
    {
        var ct = httpContext.RequestAborted;
        if (!_deploymentOptions.IsAdminMode)
            return null;

        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated != true || !user.IsInRole(PreviewConstants.AdminRole))
            return null;

        var entry = await _pageHistory.GetAsync(entryId, ct);
        if (entry == null || entry.PageNodeId != pageNodeId)
            return null;

        // The entry's FKs keep its page version alive for as long as the entry exists; a null would be
        // a broken invariant — fall back to the not-found path rather than crash the request.
        var page = await _pageStore.GetVersionAsync(entry.PageVersionId, ct);
        if (page == null)
            return null;
        if (StashPageControllerContext(httpContext, page) == null)
            return null;

        var readSnapshot = await _pageHistory.GetReadSnapshotAsync(entryId, ct);
        if (readSnapshot == null)
            return null;
        PreviewConstants.MarkSnapshotRequest(httpContext, readSnapshot);

        httpContext.Items["CMS:RouteData"] = new CMSRouteDTO
        {
            Pattern = $"{PreviewConstants.PathPrefix}/{pageNodeId}/{PreviewConstants.HistorySegment}/{entryId}",
            OwningContentType = "Page",
            OwningContentNodeId = pageNodeId
        };

        return new RouteValueDictionary
        {
            { "controller", page.ControllerName },
            { "action", "Index" }
        };
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

    private static object DeserializeConfig(string? json, Type configurationType, IEnumerable<ContentRelation>? relations)
    {
        object config;
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            config = Activator.CreateInstance(configurationType)!;
        else
        {
            try
            {
                config = JsonSerializer.Deserialize(json, configurationType,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? Activator.CreateInstance(configurationType)!;
            }
            catch
            {
                config = Activator.CreateInstance(configurationType)!;
            }
        }

        if (relations != null)
        {
            RelationProperties.Write(
                config,
                relations.Select(r => new RelationValue(r.Name, r.TargetNodeId, r.Ordinal, r.Qualifier)),
                RelationProperties.ConfigPrefix);
        }
        return config;
    }
}
