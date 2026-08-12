using WebWayCMS.Attributes;

namespace WebWayCMS.Models.ContentZone;

public sealed class ContentZoneItemUpsertViewModel
{
    [FormProperty(Label = "Id", EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public Guid? Id { get; init; }

    [FormProperty(Label = "ContentZoneId", EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public Guid ContentZoneId { get; init; }

    [FormProperty(Label = "MasterId", EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public Guid MasterId { get; init; }

    [FormProperty(Label = "Version", EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public int Version { get; init; }

    [FormProperty(Label = "Component Name", EditorType = EditorType.Text, IsRequired = true, Order = 1, FormComponent = "Text")]
    public string ComponentName { get; init; } = string.Empty;

    [FormProperty(Label = "Component Properties (JSON)", EditorType = EditorType.TextArea, Order = 2, FormComponent = "TextArea")]
    public string ComponentPropertiesJson { get; init; } = string.Empty;

    [FormProperty(Label = "Active", EditorType = EditorType.Checkbox, Order = 3, FormComponent = "Checkbox")]
    public bool IsActive { get; init; }
}