namespace WebWayCMS.Attributes;

/// <summary>
/// Marks a Blazor component as an alternate render view ("sub-view") for an existing content-zone
/// widget. A host project consuming the CMS drops a component decorated with this attribute into its
/// entry assembly; it then appears in the admin widget editor's "View" dropdown for the matching
/// component, and renders in place of the widget's default markup when selected.
/// </summary>
/// <remarks>
/// The view shares the target widget's configuration model: it receives the same
/// <c>[Parameter] object Config</c> the widget receives, so it renders the same data differently.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ContentZoneViewAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the content-zone component name this view is bound to (the value persisted in
    /// <c>ContentZoneItemDTO.ComponentName</c>, e.g. "ContentBlock").
    /// </summary>
    public string ForComponent { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the view name shown in and persisted by the admin widget editor's "View" dropdown.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public ContentZoneViewAttribute()
    {
    }

    public ContentZoneViewAttribute(string forComponent, string name)
    {
        ForComponent = forComponent;
        Name = name;
    }
}
