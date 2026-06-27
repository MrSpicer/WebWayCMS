namespace WebWayCMS.Models.Page;

/// <summary>
/// Represents a node in the page route tree. Intermediate nodes (route segments
/// without a corresponding page) have a null PageId.
/// </summary>
public class PageTreeNode
{
    public string Route { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid? PageId { get; set; }
    public Guid? PageMasterId { get; set; }
    public int PageVersion { get; set; }
    public string ControllerName { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public bool IsHidden { get; set; }
    public List<PageTreeNode> Children { get; set; } = new();
}