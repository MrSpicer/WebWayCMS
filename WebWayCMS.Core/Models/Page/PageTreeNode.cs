namespace WebWayCMS.Models.Page;

public class PageTreeNode
{
    public string Path { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid? PageNodeId { get; set; }
    public bool IsPublished { get; set; }
    public bool IsHidden { get; set; }
    public List<PageTreeNode> Children { get; set; } = new();
}
