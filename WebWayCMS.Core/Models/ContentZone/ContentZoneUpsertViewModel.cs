using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Models.ContentZone;

public sealed class ContentZoneUpsertViewModel : BaseContentViewModel
{
    [Required]
    [FormProperty(Label = "Name", EditorType = EditorType.Text, IsRequired = true, Order = 3, FormComponent = "Text")]
    public string Name { get; init; } = string.Empty;

    [FormProperty(Label = "Description", EditorType = EditorType.TextArea, Order = 4, FormComponent = "TextArea")]
    public string Description { get; init; } = string.Empty;
}