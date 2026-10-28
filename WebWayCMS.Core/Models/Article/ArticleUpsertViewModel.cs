using System.ComponentModel.DataAnnotations;

using WebWayCMS.Attributes;

namespace WebWayCMS.Models.Article;

public sealed class ArticleUpsertViewModel : BaseContentViewModel
{
    /// <summary>
    /// The article list this article belongs to (required): a containment relation, so the article is
    /// visible only while its list is, and is deleted with it. The relation binder rejects an empty or
    /// unknown list on this field.
    /// </summary>
    [Display(Name = "Article List")]
    [ContentRelation("articlelists", Kind = ContentRelationKind.Containment, Required = true)]
    [FormProperty(Label = "Article List", HelpText = "The article list this article belongs to.", Order = 3)]
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
