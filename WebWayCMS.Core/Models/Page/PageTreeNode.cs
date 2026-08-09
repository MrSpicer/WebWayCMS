namespace WebWayCMS.Models.Page;

public class PageTreeNode
{
    public string Route { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid? PageId { get; set; }
    public Guid? PageMasterId { get; set; }
    public int PageVersion { get; set; }
    public bool IsPublished { get; set; }
    public bool IsHidden { get; set; }
    public List<PageTreeNode> Children { get; set; } = new();
}
