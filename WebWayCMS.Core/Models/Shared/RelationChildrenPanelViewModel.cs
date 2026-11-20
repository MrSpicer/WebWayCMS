using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Models.Shared;

/// <summary>
/// One containment's children on the parent's edit screen (<c>_RelationChildren.cshtml</c>): the child
/// type, the relation that places them, and the current drafts under this parent.
/// </summary>
public sealed class RelationChildrenPanelViewModel
{
    /// <summary>ViewData key the admin controller stores the panels under; the layout renders them under the form.</summary>
    public const string ViewDataKey = "Admin:RelationChildren";

    public string ChildContentType { get; init; } = string.Empty;
    /// <summary>The child type's singular name ("New {name}").</summary>
    public string ChildDisplayName { get; init; } = string.Empty;

    /// <summary>The child type's plural name (the panel heading).</summary>
    public string ChildPluralDisplayName { get; init; } = string.Empty;
    public string RelationName { get; init; } = string.Empty;
    public Guid ParentNodeId { get; init; }
    public bool Ordered { get; init; }

    /// <summary>Whether the viewer may create and reorder children (the child type's write roles).</summary>
    public bool CanWrite { get; init; }

    public IReadOnlyList<RelatedChild> Children { get; init; } = [];
}
