using WebWayCMS.Attributes;

namespace WebWayCMS.Attributes;

/// <summary>
/// Marks a ViewComponent as a form field editor available to the form-building system.
/// Components are discovered by attribute and registered into the FormComponent content type
/// the same way widgets (<see cref="ContentZoneComponentAttribute"/>) and page types
/// (<see cref="PageControllerAttribute"/>) work.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CMSFormComponentAttribute : Attribute
{
    /// <summary>
    /// Registry key, e.g. "RichText". Defaults to the class name minus "ViewComponent" suffix
    /// when not set explicitly.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// CLR types this component can edit, e.g. <c>new[] { typeof(int), typeof(long) }</c>.
    /// </summary>
    public Type[] DataTypes { get; set; } = Array.Empty<Type>();

    /// <summary>
    /// This is the default component for its <see cref="DataTypes"/>.
    /// When two components claim IsDefaultForType for the same CLR type,
    /// the resolver breaks ties by lowest <see cref="Order"/> then <see cref="Name"/> ordinal.
    /// </summary>
    public bool IsDefaultForType { get; set; }

    /// <summary>
    /// The legacy <see cref="EditorType"/> shorthand this component answers to.
    /// Stored as <see cref="object"/> because nullable enums are not valid attribute parameter types.
    /// The seeder and registry cast this back to <c>EditorType?</c>.
    /// </summary>
    public object? EditorType { get; set; }

    /// <summary>
    /// Display name for the admin UI.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Description shown as help text in the admin UI.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Category for grouping related components in the UI.
    /// </summary>
    public string Category { get; set; } = "General";

    /// <summary>
    /// CSS icon class for display in the UI.
    /// </summary>
    public string IconClass { get; set; } = string.Empty;

    /// <summary>
    /// Sort order for display within a category. Lower values appear first.
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// Name of the write-mode view. Default is "Write".
    /// </summary>
    public string WriteViewName { get; set; } = "Write";

    /// <summary>
    /// Name of the read-mode view. Default is "Read".
    /// </summary>
    public string ReadViewName { get; set; } = "Read";

    /// <summary>
    /// Initializes a new instance with a registry key name.
    /// </summary>
    public CMSFormComponentAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Parameterless constructor; <see cref="Name"/> defaults to the class name minus "ViewComponent".
    /// </summary>
    public CMSFormComponentAttribute()
    {
        Name = string.Empty;
    }
}
