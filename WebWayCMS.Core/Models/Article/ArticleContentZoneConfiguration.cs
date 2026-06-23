using WebWayCMS.Attributes;

namespace WebWayCMS.Models.Article;

public class ArticleContentZoneConfiguration
{
    [FormProperty(
        Label = "Mode",
        HelpText = "Choose whether to display a single article or a list of articles.",
        EditorType = EditorType.Dropdown,
        DropdownOptions = "Single:Single Article,List:Article List",
        Order = 0
    )]
    public string? Mode { get; set; }

    [FormProperty(
        Label = "Article List",
        HelpText = "Select an article list to display.",
        EditorType = EditorType.Guid,
        EntityType = "ArticleList",
        Order = 1
    )]
    public Guid? ArticleListId { get; set; }

    [FormProperty(
        Label = "Article",
        HelpText = "Select a specific article to display.",
        EditorType = EditorType.Guid,
        EntityType = "Article",
        Order = 2
    )]
    public Guid? Id { get; set; }

    public ArticleViewModel? Article { get; set; }

    public ArticleUpsertViewModel? UpsertModel { get; set; }
}
