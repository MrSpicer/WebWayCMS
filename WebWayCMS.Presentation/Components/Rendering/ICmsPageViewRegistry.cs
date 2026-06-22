namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Resolves host-provided page-body views, discovered by convention from components decorated with
/// <see cref="WebWayCMS.Attributes.CmsPageViewAttribute"/>. Feeds the admin "View Name" dropdown and
/// the public render path.
/// </summary>
public interface ICmsPageViewRegistry
{
    /// <summary>The registered view names for a page controller, sorted; empty when none.</summary>
    IReadOnlyList<string> GetControllerViews(string controllerName);

    /// <summary>The view component type for a (controller, view), or <c>null</c> when not registered.</summary>
    Type? Resolve(string controllerName, string viewName);
}
