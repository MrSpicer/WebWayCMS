namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Maps a content-zone component name (as stored on a zone item) to the Razor component
/// type that renders it. Populated incrementally as ViewComponents are migrated to Razor
/// components; an unmapped name resolves to null and is skipped during rendering.
/// </summary>
public interface IContentZoneWidgetRegistry
{
    /// <summary>Returns the Razor component type for the given component name, or null if none is registered.</summary>
    Type? Resolve(string componentName);
}
