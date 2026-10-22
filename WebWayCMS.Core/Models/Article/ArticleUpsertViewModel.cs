using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Models.Article;

public sealed class ArticleUpsertViewModel : BaseContentViewModel
{
    /// <summary>
    /// The article list this article belongs to (required). The save path checks that it names an
    /// existing list, so an empty or foreign id is rejected rather than tripping the FK.
    /// </summary>
    [Display(Name = "Article List")]
    [FormProperty(Label = "Article List", HelpText = "The article list this article belongs to.", EditorType = EditorType.Guid,
        EntityType = "ArticleList", IsRequired = true, Order = 3, FormComponent = "EntityPicker")]
    public Guid ArticleListId { get; init; }

    [Required]
    [FormProperty(Label = "Body", EditorType = EditorType.RichText, IsRequired = true, Order = 4, FormComponent = "RichText")]
    public string Body { get; init; } = string.Empty;

    [FormProperty(Label = "Summary", EditorType = EditorType.TextArea, Order = 5, FormComponent = "TextArea")]
    public string? Summary { get; init; }

    [Display(Name = "Author")]
    [StringLength(200)]
    [Required]
    [FormProperty(Label = "Author", EditorType = EditorType.Text, Order = 6, GroupWithNext = true, FormComponent = "Text")]
    public string AuthorName { get; init; } = string.Empty;
}
