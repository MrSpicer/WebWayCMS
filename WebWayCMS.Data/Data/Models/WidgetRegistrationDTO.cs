namespace WebWayCMS.Data.Models;

public record WidgetRegistrationDTO : IContent
{
    public Guid ContentId { get; set; }
    public ContentDTO ContentMeta { get; set; } = new();

    public string ComponentName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string IconClass { get; set; } = string.Empty;
    public int Order { get; set; }
    public string? ConfigurationTypeName { get; set; }
    public string PropertyDefinitionsJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
}
