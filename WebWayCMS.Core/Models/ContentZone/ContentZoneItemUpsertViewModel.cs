using WebWayCMS.Attributes;

namespace WebWayCMS.Models.ContentZone;

public sealed class ContentZoneItemUpsertViewModel
{
    [FormProperty(Label = "Id", EditorType = EditorType.Hidden)]
    public Guid? Id { get; init; }

    [FormProperty(Label = "ContentZoneId", EditorType = EditorType.Hidden)]
    public Guid ContentZoneId { get; init; }

    [FormProperty(Label = "MasterId", EditorType = EditorType.Hidden)]
    public Guid MasterId { get; init; }

    [FormProperty(Label = "Version", EditorType = EditorType.Hidden)]
    public int Version { get; init; }

    [FormProperty(Label = "Component Name", EditorType = EditorType.Text, IsRequired = true, Order = 1)]
    public string ComponentName { get; init; } = string.Empty;

    [FormProperty(Label = "Component Properties (JSON)", EditorType = EditorType.TextArea, Order = 2)]
    public string ComponentPropertiesJson { get; init; } = string.Empty;

    [FormProperty(Label = "Active", EditorType = EditorType.Checkbox, Order = 3)]
    public bool IsActive { get; init; }
}
