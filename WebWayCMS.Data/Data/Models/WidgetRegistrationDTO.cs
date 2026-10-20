namespace WebWayCMS.Data.Models;

public record WidgetRegistrationDTO : IVersionedContent
{
    public Guid VersionId { get; set; }
    public ContentVersion Version { get; set; } = new();

    public string ComponentName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string IconClass { get; set; } = string.Empty;
    public int Order { get; set; }
    public string? ConfigurationTypeName { get; set; }
    public string PropertyDefinitionsJson { get; set; } = "[]";

    /// <summary>
    /// Whether the widget can render a list (<c>[ContentZoneComponent(SupportsList = true)]</c>).
    /// A code capability, not admin data: the registration seeder syncs it from the attribute on
    /// every boot.
    /// </summary>
    public bool SupportsList { get; set; }
    public bool IsActive { get; set; } = true;
}
