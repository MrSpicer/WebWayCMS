using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Models.Article;

public sealed class ArticleUpsertViewModel : BaseContentViewModel
{
    [Required]
    [FormProperty(Label = "Body", EditorType = EditorType.RichText, IsRequired = true, Order = 3)]
    public string Body { get; init; } = string.Empty;

    [FormProperty(Label = "Summary", EditorType = EditorType.TextArea, Order = 4)]
    public string Summary { get; init; } = string.Empty;

    [Display(Name = "Author")]
    [StringLength(200)]
    [FormProperty(Label = "Author", EditorType = EditorType.Text, Order = 5, GroupWithNext = true)]
    public string AuthorName { get; init; } = string.Empty;

    [FormProperty(EditorType = EditorType.Hidden)]
    public Guid ArticleListId { get; init; }
}