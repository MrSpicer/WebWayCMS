using WebWayCMS.Data.Models;

namespace WebWayCMS.Controllers.Admin.Handlers;

/// <summary>
/// One child of a parent through a containment (its current draft), as the parent's children panel and
/// MCP <c>list_children</c> show it. <see cref="Ordinal"/> is its position when the relation is ordered.
/// </summary>
public sealed record RelatedChild(Guid Id, string Title, ContentVersionState State, int Ordinal);
