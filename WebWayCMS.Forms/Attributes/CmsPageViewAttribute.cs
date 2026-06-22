namespace WebWayCMS.Attributes;

/// <summary>
/// Marks a Blazor component as an alternate page-body view for a registered page controller. A host
/// project consuming the CMS drops a component decorated with this attribute into its entry assembly;
/// it then appears in the admin "View Name" dropdown for pages of the matching controller, and renders
/// in place of the default content zone when selected.
/// </summary>
/// <remarks>
/// The view component renders the page body only; it is hosted inside the CMS document shell and any
/// host chrome (see <see cref="CmsChromeAttribute"/>). It reads page context from the cascaded
/// <c>CmsRenderContext</c> and may include its own content zones.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CmsPageViewAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the page controller name this view is bound to (e.g. "Generic"), matching the
    /// registered controller name without the "Controller" suffix.
    /// </summary>
    public string ForController { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the view name shown in and persisted by the admin "View Name" dropdown.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public CmsPageViewAttribute()
    {
    }

    public CmsPageViewAttribute(string forController, string name)
    {
        ForController = forController;
        Name = name;
    }
}
