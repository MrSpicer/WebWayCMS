using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Models.Article;

public sealed class ArticleUpsertViewModel : BaseContentViewModel
{
    [Required]
    [FormProperty(Label = "Body", EditorType = EditorType.RichText, IsRequired = true, Order = 3, FormComponent = "RichText")]
    public string Body { get; init; } = string.Empty;

    [FormProperty(Label = "Summary", EditorType = EditorType.TextArea, Order = 4, FormComponent = "TextArea")]
    public string? Summary { get; init; }

    [Display(Name = "Author")]
    [StringLength(200)]
    [Required]
    [FormProperty(Label = "Author", EditorType = EditorType.Text, Order = 5, GroupWithNext = true, FormComponent = "Text")]
    public string AuthorName { get; init; } = string.Empty;

    [FormProperty(EditorType = EditorType.Hidden, FormComponent = "Hidden")]
    public Guid ArticleListId { get; init; }
}