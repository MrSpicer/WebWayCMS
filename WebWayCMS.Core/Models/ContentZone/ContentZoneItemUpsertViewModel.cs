using System.Text.Json.Serialization;

using WebWayCMS.Attributes;

namespace WebWayCMS.Models.ContentZone;

public sealed class ContentZoneItemUpsertViewModel
{
    [FormProperty(Label = "NodeId", EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public Guid? NodeId { get; init; }

    /// <summary>
    /// The zone the item sits in: an ordered containment (the relation <c>ZoneRelations.Zone</c> is named
    /// after this property). Set on create (e.g. <c>?ContentZoneNodeId={zone}</c>); an update never moves
    /// the item, which only add, reorder, move and restore do.
    /// </summary>
    [ContentRelation("contentzones", Kind = ContentRelationKind.Containment, Required = true, Ordered = true, ShowInParentAdmin = false)]
    [FormProperty(Label = "ContentZoneNodeId", EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public Guid ContentZoneNodeId { get; init; }

    /// <summary>The zone's name, for the form's breadcrumb (display only; not a form field or MCP field).</summary>
    [JsonIgnore]
    public string? ZoneName { get; init; }

    [FormProperty(Label = "ExpectedVersionNumber", EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public int? ExpectedVersionNumber { get; init; }

    [FormProperty(Label = "Component Name", EditorType = EditorType.Text, IsRequired = true, Order = 1, FormComponent = "Text")]
    public string ComponentName { get; init; } = string.Empty;

    [FormProperty(Label = "Component Properties (JSON)", EditorType = EditorType.TextArea, Order = 2, FormComponent = "TextArea")]
    public string ComponentPropertiesJson { get; init; } = string.Empty;

    [FormProperty(Label = "Metadata (JSON)", EditorType = EditorType.TextArea, Order = 3, FormComponent = "TextArea")]
    public string? MetadataJson { get; init; }

    [FormProperty(Label = "Active", EditorType = EditorType.Checkbox, Order = 4, FormComponent = "Checkbox")]
    public bool IsActive { get; init; }
}
