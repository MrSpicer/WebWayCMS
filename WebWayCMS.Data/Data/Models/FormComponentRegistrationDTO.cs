namespace WebWayCMS.Data.Models;

/// <summary>
/// A form component registration. Registrations are not versioned — they are seeded and re-synced
/// from code by <c>CmsFormComponentSeeder</c>, and edited in place via the admin UI.
/// </summary>
public record FormComponentRegistrationDTO
{
    public Guid Id { get; set; }

    public string ComponentName { get; set; } = string.Empty;
    public string ViewComponentName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string IconClass { get; set; } = string.Empty;
    public int Order { get; set; }
    public string DataTypeNamesJson { get; set; } = "[]";
    public string? EditorTypeAlias { get; set; }
    public bool IsDefaultForType { get; set; }
    public string WriteViewName { get; set; } = "Write";
    public string ReadViewName { get; set; } = "Read";
    public bool IsActive { get; set; } = true;
}
