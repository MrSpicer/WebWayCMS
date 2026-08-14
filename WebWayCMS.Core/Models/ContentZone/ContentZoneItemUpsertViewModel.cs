using WebWayCMS.Attributes;

namespace WebWayCMS.Models.ContentZone;

public sealed class ContentZoneItemUpsertViewModel
{
    [FormProperty(Label = "NodeId", EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public Guid? NodeId { get; init; }

    [FormProperty(Label = "ContentZoneNodeId", EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public Guid ContentZoneNodeId { get; init; }

    [FormProperty(Label = "ExpectedVersionNumber", EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public int? ExpectedVersionNumber { get; init; }

    [FormProperty(Label = "Component Name", EditorType = EditorType.Text, IsRequired = true, Order = 1, FormComponent = "Text")]
    public string ComponentName { get; init; } = string.Empty;

    [FormProperty(Label = "Component Properties (JSON)", EditorType = EditorType.TextArea, Order = 2, FormComponent = "TextArea")]
    public string ComponentPropertiesJson { get; init; } = string.Empty;

    [FormProperty(Label = "Active", EditorType = EditorType.Checkbox, Order = 3, FormComponent = "Checkbox")]
    public bool IsActive { get; init; }
}
