namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Resolves host-provided content-zone widget views ("sub-views"), discovered by convention from
/// components decorated with <see cref="WebWayCMS.Attributes.ContentZoneViewAttribute"/>. Feeds the
/// admin widget editor's "View" dropdown and the content-zone render path.
/// </summary>
public interface IContentZoneViewRegistry
{
    /// <summary>The registered view names for a content-zone component, sorted; empty when none.</summary>
    IReadOnlyList<string> GetComponentViews(string componentName);

    /// <summary>The view component type for a (component, view), or <c>null</c> when not registered.</summary>
    Type? Resolve(string componentName, string viewName);
}
