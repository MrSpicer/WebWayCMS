using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Models.ContentBlock;

public sealed class ContentBlockUpsertViewModel : BaseContentViewModel
{
    [Required]
    [FormProperty(Label = "Content", EditorType = EditorType.RichText, IsRequired = true, Order = 3)]
    public string Content { get; init; } = string.Empty;
}