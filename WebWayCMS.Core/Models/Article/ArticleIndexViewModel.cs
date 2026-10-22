namespace WebWayCMS.Models.Article;

/// <summary>The admin Articles page: every article, optionally filtered to one list.</summary>
public sealed class ArticleIndexViewModel
{
    public List<ArticleItemViewModel> Articles { get; set; } = new();

    /// <summary>Every article list, for the filter and the List column.</summary>
    public List<ArticleListItemViewModel> ArticleLists { get; set; } = new();

    /// <summary>The list the page is filtered to (<c>?list={nodeId}</c>), or null for all articles.</summary>
    public Guid? SelectedListId { get; set; }

    /// <summary>The title of <see cref="SelectedListId"/>, or null when unfiltered or the list is gone.</summary>
    public string? SelectedListTitle { get; set; }
}
