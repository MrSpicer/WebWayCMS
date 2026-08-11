using System.Text.Json;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Interfaces;

namespace WebWayCMS.Services;

public sealed class RouteRegistrationService : IRouteRegistrationService
{
    private readonly ICMSRouteService _routeService;
    private readonly IEnumerable<IRoutableViewComponent> _routableWidgets;

    public RouteRegistrationService(
        ICMSRouteService routeService,
        IEnumerable<IRoutableViewComponent> routableWidgets)
    {
        _routeService = routeService ?? throw new ArgumentNullException(nameof(routeService));
        _routableWidgets = routableWidgets ?? throw new ArgumentNullException(nameof(routableWidgets));
    }

    public async Task RegisterContentRoutesAsync(
        IRoutableContent content, string routePattern, string controllerName, object configuration,
        Guid? viewModelId, Guid? viewModelMasterId, bool isPublished, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(routePattern))
            return;

        var defaults = new Dictionary<string, string>
        {
            { "controller", controllerName },
            { "action", "Index" }
        };

        var configJson = configuration != null
            ? JsonSerializer.Serialize(configuration)
            : "{}";

        var route = new CMSRouteDTO
        {
            Pattern = routePattern,
            DefaultsJson = JsonSerializer.Serialize(defaults),
            DataTokensJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                { "ConfigurationJson", configJson },
                { "RouteContentType", content.RouteContentType }
            }),
            OwningContentMasterId = viewModelMasterId,
            OwningContentType = content.RouteContentType,
            ContentMeta = new ContentDTO
            {
                Id = Guid.NewGuid(),
                MasterId = viewModelMasterId ?? Guid.NewGuid(),
                IsPublished = isPublished,
                Title = routePattern,
                Slug = routePattern.TrimStart('/'),
                PublicationDate = DateTime.UtcNow,
                CreationDate = DateTime.UtcNow,
                ModificationDate = DateTime.UtcNow,
                CreatedBy = Guid.Empty,
                LastModifiedBy = Guid.Empty
            }
        };

        await _routeService.UpsertAsync(route, ct);
    }

    public async Task UnregisterContentRoutesAsync(Guid contentMasterId, CancellationToken ct = default)
    {
        await _routeService.DeactivateByOwningContentAsync(contentMasterId, ct);
    }

    public async Task RegisterWidgetRoutesAsync(
        IRoutableViewComponent widget, Guid contentZoneItemMasterId, string parentRoute,
        string parentDefaultsJson, Guid parentPageMasterId, bool isActive, CancellationToken ct = default)
    {
        var widgetRoutes = await widget.GenerateRoutesAsync(parentRoute, contentZoneItemMasterId, ct);

        var parentDefaults = TryDeserialize<Dictionary<string, string>>(parentDefaultsJson)
            ?? new Dictionary<string, string>();

        foreach (var widgetRoute in widgetRoutes)
        {
            widgetRoute.Pattern = NormalizeWidgetPattern(parentRoute, widgetRoute.Pattern);

            var mergedDefaults = new Dictionary<string, string>(parentDefaults);
            var routeDefaults = TryDeserialize<Dictionary<string, string>>(widgetRoute.DefaultsJson)
                ?? new Dictionary<string, string>();
            foreach (var kvp in routeDefaults)
                mergedDefaults[kvp.Key] = kvp.Value;
            widgetRoute.DefaultsJson = JsonSerializer.Serialize(mergedDefaults);

            var dataTokens = TryDeserialize<Dictionary<string, string>>(widgetRoute.DataTokensJson)
                ?? new Dictionary<string, string>();
            dataTokens["ParentPageMasterId"] = parentPageMasterId.ToString();
            widgetRoute.DataTokensJson = JsonSerializer.Serialize(dataTokens);

            widgetRoute.ContentMeta.IsPublished = isActive;
            widgetRoute.ContentMeta.Title = widgetRoute.Pattern;
            widgetRoute.ContentMeta.Slug = widgetRoute.Pattern.TrimStart('/');
            if (widgetRoute.ContentMeta.Id == Guid.Empty)
                widgetRoute.ContentMeta.Id = Guid.NewGuid();
            if (widgetRoute.ContentMeta.MasterId == Guid.Empty)
                widgetRoute.ContentMeta.MasterId = widgetRoute.ContentMeta.Id;
            widgetRoute.ContentMeta.CreationDate = DateTime.UtcNow;
            widgetRoute.ContentMeta.ModificationDate = DateTime.UtcNow;
            widgetRoute.ContentMeta.PublicationDate = DateTime.UtcNow;
            widgetRoute.ContentMeta.CreatedBy = Guid.Empty;
            widgetRoute.ContentMeta.LastModifiedBy = Guid.Empty;

            await _routeService.UpsertAsync(widgetRoute, ct);
        }
    }

    public async Task TryRegisterWidgetRoutesAsync(
        string componentName, Guid contentZoneItemMasterId, Guid? parentPageMasterId,
        bool isActive, CancellationToken ct = default)
    {
        if (!parentPageMasterId.HasValue)
            return;

        var widget = _routableWidgets.FirstOrDefault(w =>
            string.Equals(w.ComponentName, componentName, StringComparison.OrdinalIgnoreCase));
        if (widget == null)
            return;

        var pageRoutes = await _routeService.GetByOwningContentAsync(parentPageMasterId.Value, ct);
        var pageRoute = pageRoutes.FirstOrDefault();
        if (pageRoute == null)
            return;

        await RegisterWidgetRoutesAsync(
            widget, contentZoneItemMasterId, pageRoute.Pattern, pageRoute.DefaultsJson,
            parentPageMasterId.Value, isActive, ct);
    }

    private static string NormalizeWidgetPattern(string parentRoute, string widgetPattern)
    {
        parentRoute = parentRoute.TrimEnd('/');
        if (!widgetPattern.StartsWith('/'))
            widgetPattern = "/" + widgetPattern;
        var fullPattern = parentRoute.Length == 0 ? widgetPattern : parentRoute + widgetPattern;
        return NormalizePattern(fullPattern);
    }

    private static string NormalizePattern(string pattern)
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
