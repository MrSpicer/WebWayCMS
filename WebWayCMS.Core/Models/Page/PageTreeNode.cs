namespace WebWayCMS.Models.Page;

public class PageTreeNode
{
    public string Path { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid? PageNodeId { get; set; }
    public bool IsPublished { get; set; }
    public bool HasPendingChanges { get; set; }
    public bool IsHidden { get; set; }
    /// <summary>When the page's current version was written; null for path-only placeholder nodes.</summary>
    public DateTime? ModificationDate { get; set; }
    /// <summary>The page's descendant pages at every depth (its parent relation), deleted with it.</summary>
    public int DescendantCount { get; set; }
    public List<PageTreeNode> Children { get; set; } = new();
}
