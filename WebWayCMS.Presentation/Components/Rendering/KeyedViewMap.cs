namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// A two-level, case-insensitive map of <c>key -&gt; (viewName -&gt; component Type)</c>. Shared by the
/// page-view and content-zone-view registries, which differ only in what their key represents
/// (a page controller name vs. a content-zone component name).
/// </summary>
internal sealed class KeyedViewMap
{
    private readonly Dictionary<string, Dictionary<string, Type>> _byKey =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a view component; entries with a blank key or view name are ignored.</summary>
    public void Add(string key, string viewName, Type type)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(viewName))
            return;

        if (!_byKey.TryGetValue(key, out var views))
        {
            views = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            _byKey[key] = views;
        }
        views[viewName] = type;
    }

    /// <summary>The registered view names for a key, sorted; empty when the key is blank or unknown.</summary>
    public IReadOnlyList<string> Views(string key)
        => string.IsNullOrWhiteSpace(key) || !_byKey.TryGetValue(key, out var views)
            ? Array.Empty<string>()
            : views.Keys.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The component type for a (key, viewName), or <c>null</c> when not registered.</summary>
    public Type? Resolve(string key, string viewName)
        => !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(viewName)
            && _byKey.TryGetValue(key, out var views) && views.TryGetValue(viewName, out var type)
            ? type
            : null;
}
