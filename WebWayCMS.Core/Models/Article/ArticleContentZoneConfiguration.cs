using WebWayCMS.Attributes;
using WebWayCMS.ContentZones;

namespace WebWayCMS.Models.Article;

/// <summary>
/// Configuration for the Article widget. The item's display mode picks the configurator:
/// <b>Single</b> renders the article named by the URL slug (on the widget's own route) or
/// <see cref="ArticleId"/>; <b>List</b> renders the articles of <see cref="ArticleListId"/>.
/// The view variants come from the inherited <see cref="WidgetConfiguration"/> pickers.
/// </summary>
public class ArticleContentZoneConfiguration : WidgetConfiguration
{
    [FormProperty(
        Label = "Article",
        HelpText = "The article to show when the URL does not name one. Leave empty to show only articles opened from a list.",
        EditorType = EditorType.Guid,
        EntityType = "Article",
        DisplayModes = WidgetDisplayMode.Single,
        Order = 1,
        FormComponent = "EntityPicker"
    )]
    public Guid? ArticleId { get; set; }

    [FormProperty(
        Label = "Article List",
        HelpText = "The article list to display. Nothing renders until one is chosen.",
        EditorType = EditorType.Guid,
        EntityType = "ArticleList",
        DisplayModes = WidgetDisplayMode.List,
        Order = 1,
        FormComponent = "EntityPicker"
    )]
    public Guid? ArticleListId { get; set; }

    [FormProperty(
        Label = "Max Items",
        HelpText = "The most articles to show. Leave empty (or 0) to show them all.",
        EditorType = EditorType.Number,
        Min = 0,
        DisplayModes = WidgetDisplayMode.List,
        Order = 2,
        FormComponent = "Number"
    )]
    // Nullable: the admin form serializer writes null for an empty number field (form-components.js),
    // which a non-nullable int would reject, dropping the whole saved configuration.
    public int? MaxItems { get; set; }

    [FormProperty(
        Label = "Sort Order",
        EditorType = EditorType.Dropdown,
        DropdownOptions = ArticleSortOrder.Newest + ":Newest first," + ArticleSortOrder.Oldest + ":Oldest first," + ArticleSortOrder.Title + ":Title",
        DisplayModes = WidgetDisplayMode.List,
        Order = 3,
        FormComponent = "Dropdown"
    )]
    public string? SortOrder { get; set; }
}
